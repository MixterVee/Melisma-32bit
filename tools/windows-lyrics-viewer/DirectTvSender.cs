using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace MelismaLyricsViewer;

/// <summary>
/// Browser-free, audio-free, native LAN transport to Melisma Receiver on
/// Android TV. The Windows host pushes images over a single authenticated
/// TCP connection; no inbound Windows firewall hole, MP3 or Google login.
/// </summary>
internal sealed record TvReceiverDevice(string Name, IPAddress Address, int Port)
{
    public override string ToString() => $"{Name} — {Address}";
}

internal static class TvReceiverDiscovery
{
    internal const int DiscoveryPort = 47916;
    internal const int ReceiverPort = 49831;
    internal const string Probe = "MELISMA_DISCOVER_V1";
    internal const string Reply = "MELISMA_TV_V1|";

    // Broadcasting to unused IPs can make Windows deliver an asynchronous
    // ICMP "port unreachable" as WSAECONNRESET on ReceiveAsync. This is not
    // a broken TV connection; the old picker aborted its entire scan.
    internal static bool RecoverableReceiveError(SocketError error) =>
        error is SocketError.ConnectionReset or SocketError.ConnectionRefused or
            SocketError.NetworkReset or SocketError.HostUnreachable or
            SocketError.NetworkUnreachable;

    internal static async Task<IReadOnlyList<TvReceiverDevice>> ScanAsync(
        TimeSpan duration, CancellationToken cancellationToken = default)
    {
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        sender.EnableBroadcast = true;
        // Windows SIO_UDP_CONNRESET: don't turn ICMP replies from
        // unreachable broadcast targets into fatal UDP receive exceptions.
        // Other platforms can simply use the recoverable-error catch below.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                sender.Client.IOControl((IOControlCode)0x9800000C,
                    [0, 0, 0, 0], null);
            }
            catch (SocketException) { /* Fall back to ReceiveAsync retry. */ }
            catch (NotSupportedException) { /* Fall back to retry. */ }
        }
        byte[] probe = Encoding.ASCII.GetBytes(Probe);
        var broadcastTargets = new HashSet<string> { IPAddress.Broadcast.ToString() };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or
                NetworkInterfaceType.Tunnel) continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    unicast.IPv4Mask == null) continue;
                byte[] address = unicast.Address.GetAddressBytes();
                byte[] mask = unicast.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];
                for (int i = 0; i < 4; i++)
                    broadcast[i] = (byte)(address[i] | (~mask[i] & 0xff));
                broadcastTargets.Add(new IPAddress(broadcast).ToString());
            }
        }
        foreach (string destination in broadcastTargets)
        {
            try
            {
                await sender.SendAsync(probe, probe.Length,
                    new IPEndPoint(IPAddress.Parse(destination), DiscoveryPort));
            }
            catch (SocketException) { /* Some VPN/NIC interfaces block broadcast. */ }
        }

        var found = new Dictionary<string, TvReceiverDevice>(StringComparer.Ordinal);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration);
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                UdpReceiveResult reply;
                try
                {
                    reply = await sender.ReceiveAsync(timeout.Token);
                }
                catch (SocketException ex) when
                    (RecoverableReceiveError(ex.SocketErrorCode))
                {
                    // One unreachable broadcast address must not abort
                    // discovery of a different, healthy Android TV.
                    continue;
                }
                string value = Encoding.UTF8.GetString(reply.Buffer);
                if (!value.StartsWith(Reply, StringComparison.Ordinal)) continue;
                string[] parts = value.Split('|', 3);
                if (parts.Length != 3 ||
                    !int.TryParse(parts[2], out int port) ||
                    port is < 1024 or > 65535) continue;
                string name = parts[1].Trim();
                if (name.Length is < 1 or > 60) continue;
                found[reply.RemoteEndPoint.Address.ToString()] = new(
                    name, reply.RemoteEndPoint.Address, port);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        return found.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

internal sealed class DirectTvSender : IDisposable
{
    private readonly TcpClient _client;
    private readonly Channel<byte[]> _images = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
    private readonly CancellationTokenSource _stop = new();
    private int _connected = 1;
    private int _disposed;
    private long _sentFrames;

    private DirectTvSender(TcpClient client) => _client = client;

    internal bool IsConnected => Volatile.Read(ref _connected) == 1;
    internal long SentFrames => Interlocked.Read(ref _sentFrames);
    internal event Action<string>? Disconnected;

    internal static async Task<DirectTvSender> ConnectAsync(
        TvReceiverDevice device, string pairingCode,
        CancellationToken cancellationToken = default)
    {
        if (pairingCode.Length != 6 || !pairingCode.All(char.IsAsciiDigit))
            throw new ArgumentException("Enter the six-digit code shown on the TV.");
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            await client.ConnectAsync(device.Address, device.Port, cancellationToken);
            NetworkStream stream = client.GetStream();
            byte[] hello = Encoding.ASCII.GetBytes("MELISMA1" + pairingCode);
            await stream.WriteAsync(hello, cancellationToken);
            byte[] response = new byte[1];
            await stream.ReadExactlyAsync(response, cancellationToken);
            if (response[0] != 1)
                throw new UnauthorizedAccessException(
                    "Pairing code rejected by TV. Check the six digits shown in Melisma Receiver.");
            var sender = new DirectTvSender(client);
            _ = sender.WriteFramesAsync(sender._stop.Token);
            return sender;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal bool Publish(byte[] jpeg)
    {
        if (!IsConnected || jpeg.Length < 4 ||
            jpeg[0] != 0xff || jpeg[1] != 0xd8 ||
            jpeg[^2] != 0xff || jpeg[^1] != 0xd9) return false;
        return _images.Writer.TryWrite(jpeg);
    }

    private async Task WriteFramesAsync(CancellationToken token)
    {
        try
        {
            using var stream = _client.GetStream();
            await foreach (byte[] jpeg in _images.Reader.ReadAllAsync(token))
            {
                byte[] length = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(length, jpeg.Length);
                await stream.WriteAsync(length, token);
                await stream.WriteAsync(jpeg, token);
                Interlocked.Increment(ref _sentFrames);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or
            OperationCanceledException or ObjectDisposedException)
        {
            if (!token.IsCancellationRequested)
                Disconnected?.Invoke("TV connection lost: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _connected, 0);
            _client.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Exchange(ref _connected, 0);
        _images.Writer.TryComplete();
        _stop.Cancel();
        _client.Dispose();
        _stop.Dispose();
    }
}
