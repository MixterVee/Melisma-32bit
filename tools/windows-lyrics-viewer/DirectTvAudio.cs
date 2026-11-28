using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using NAudio.Wave;

namespace MelismaLyricsViewer;

/// <summary>Opt-in default Windows render endpoint loopback. Shared-mode audio
/// only, after all player DSP. No Foobar commands, no microphone, no file access.
/// The independent authenticated TCP stream must never block the capture thread.</summary>
internal sealed class DirectTvAudio : IDisposable
{
    internal const int Port = 49832;
    private readonly TcpClient _client;
    private readonly WasapiLoopbackCapture _capture;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte[]> _packets = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly int _bits;
    private readonly bool _float;
    private int _failed, _disposed;
    internal bool IsConnected => Volatile.Read(ref _failed) == 0 && Volatile.Read(ref _disposed) == 0;
    internal event Action<string>? Disconnected;

    private DirectTvAudio(TcpClient client, WasapiLoopbackCapture capture, int bits, bool floating)
    {
        _client = client;
        _capture = capture;
        _bits = bits;
        _float = floating;
        _capture.DataAvailable += OnAudio;
        _capture.RecordingStopped += (_, e) =>
        {
            if (Volatile.Read(ref _disposed) == 0)
                Fail("Windows audio capture ended" + (e.Exception == null ? "." : ": " + e.Exception.Message));
        };
    }

    internal static async Task<DirectTvAudio> ConnectAsync(
        TvReceiverDevice device, string code, CancellationToken token)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
            throw new ArgumentException("Enter the six-digit TV pairing code.");
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true, SendBufferSize = 16384 };
        WasapiLoopbackCapture? capture = null;
        DirectTvAudio? sender = null;
        try
        {
            capture = new WasapiLoopbackCapture();
            var format = capture.WaveFormat;
            Guid floatGuid = new("00000003-0000-0010-8000-00aa00389b71");
            Guid pcmGuid = new("00000001-0000-0010-8000-00aa00389b71");
            bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                (format is WaveFormatExtensible ext && ext.SubFormat == floatGuid);
            bool pcm = format.Encoding == WaveFormatEncoding.Pcm ||
                (format is WaveFormatExtensible extPcm && extPcm.SubFormat == pcmGuid);
            if (format.Channels is < 1 or > 2 || format.SampleRate is < 8000 or > 96000 ||
                !(floating && format.BitsPerSample == 32 || pcm && format.BitsPerSample is 16 or 24 or 32))
                throw new InvalidOperationException("Use a stereo or mono Windows shared-mode output (8–96 kHz, PCM/float).");
            await client.ConnectAsync(device.Address, Port, token);
            var stream = client.GetStream();
            var hello = new byte[22];
            Encoding.ASCII.GetBytes("MELAUDIO" + code).CopyTo(hello, 0);
            BinaryPrimitives.WriteInt32BigEndian(hello.AsSpan(14), format.SampleRate);
            BinaryPrimitives.WriteInt32BigEndian(hello.AsSpan(18), format.Channels);
            await stream.WriteAsync(hello, token);
            var ack = new byte[1];
            await stream.ReadExactlyAsync(ack, token);
            if (ack[0] != 1) throw new IOException("TV audio was rejected. Install receiver v0.2.0 and pair lyrics first.");
            sender = new DirectTvAudio(client, capture, format.BitsPerSample, floating);
            sender._capture.StartRecording();
            _ = Task.Run(() => sender.WriteAsync(sender._stop.Token));
            return sender;
        }
        catch
        {
            if (sender != null) sender.Dispose();
            else { capture?.Dispose(); client.Dispose(); }
            throw;
        }
    }

    private void OnAudio(object? sender, WaveInEventArgs e)
    {
        if (!IsConnected || e.BytesRecorded == 0) return;
        try
        {
            int bytes = _bits / 8;
            int count = e.BytesRecorded / bytes;
            var data = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                int offset = i * bytes;
                int sample;
                if (_float)
                {
                    float f = BitConverter.ToSingle(e.Buffer, offset);
                    sample = float.IsFinite(f) ? (int)(Math.Clamp(f, -1f, 1f) * 32767f) : 0;
                }
                else if (_bits == 16) sample = BinaryPrimitives.ReadInt16LittleEndian(e.Buffer.AsSpan(offset, 2));
                else if (_bits == 24) sample = (short)(e.Buffer[offset + 1] | e.Buffer[offset + 2] << 8);
                else sample = BinaryPrimitives.ReadInt32LittleEndian(e.Buffer.AsSpan(offset, 4)) >> 16;
                BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), (short)sample);
            }
            // Reject congestion rather than accumulating a growing karaoke lag.
            if (data.Length > 262144 || !_packets.Writer.TryWrite(data))
                Fail("TV audio cannot keep up. Lyrics remain connected; reconnect audio on a stronger LAN.");
        }
        catch (Exception ex) { Fail("Audio capture conversion failed: " + ex.Message); }
    }

    private async Task WriteAsync(CancellationToken token)
    {
        try
        {
            var stream = _client.GetStream();
            var header = new byte[4];
            await foreach (var packet in _packets.Reader.ReadAllAsync(token))
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(750);
                BinaryPrimitives.WriteInt32BigEndian(header, packet.Length);
                await stream.WriteAsync(header, deadline.Token);
                await stream.WriteAsync(packet, deadline.Token);
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) Fail("TV audio connection lost: " + ex.Message);
        }
    }

    private void Fail(string message)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0 || Volatile.Read(ref _disposed) != 0) return;
        _packets.Writer.TryComplete();
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        _client.Dispose();
        // Notify outside the capture thread: the UI owns disposal.
        _ = Task.Run(() => Disconnected?.Invoke(message));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _packets.Writer.TryComplete();
        _stop.Cancel();
        _client.Dispose();
        _capture.DataAvailable -= OnAudio;
        try { _capture.StopRecording(); } catch { }
        _capture.Dispose();
        // The writer may still be unwinding; don't dispose its token source here.
    }
}
