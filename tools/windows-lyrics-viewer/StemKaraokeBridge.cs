using System.IO.MemoryMappedFiles;

namespace MelismaLyricsViewer;

internal enum KaraokeStemMode { Original = 0, Vocals = 1, Instrumental = 2, Blend = 3 }

internal sealed record StemKaraokeSnapshot(
    KaraokeStemMode Mode, int VocalPercent, int InstrumentalPercent,
    bool DspActive, long HeartbeatAgeMs, long DspAgeMs,
    int ProcessId, long SessionId, int RequestedSequence,
    int AppliedSequence)
{
    internal bool Pending => RequestedSequence != AppliedSequence;
    internal string Description => Mode switch
    {
        KaraokeStemMode.Original => "Original song",
        KaraokeStemMode.Vocals => "Vocals only",
        KaraokeStemMode.Instrumental => "Instrumental · Karaoke",
        KaraokeStemMode.Blend => "Custom vocal / instrumental blend",
        _ => "Unknown"
    };
}

/// <summary>
/// Local-only control/status transport owned by Stem Separator v2.8.3 in
/// Foobar2000. Never create the mapping ourselves: that could falsely
/// imply the plugin is installed, active or controlling the real audio.
/// </summary>
internal sealed class StemKaraokeBridge : IDisposable
{
    internal const string MappingName = @"Local\MelismaStemKaraoke_v1";
    internal const int MappingSize = 72;
    internal const int Magic = unchecked((int)0x4D534B31);
    internal const int Version = 1;

    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private long _lastOpenAttempt;
    private bool _disposed;

    internal bool TryRead(out StemKaraokeSnapshot? status)
    {
        status = null;
        if (_disposed) return false;
        long now = Environment.TickCount64;
        if (_view == null)
        {
            if (now - _lastOpenAttempt < 1000) return false;
            _lastOpenAttempt = now;
            try
            {
                _map = MemoryMappedFile.OpenExisting(MappingName,
                    MemoryMappedFileRights.ReadWrite);
                _view = _map.CreateViewAccessor(
                    0, MappingSize, MemoryMappedFileAccess.ReadWrite);
            }
            catch (Exception ex) when (ex is FileNotFoundException or
                IOException or UnauthorizedAccessException or
                ArgumentException)
            {
                CloseMapping();
                return false;
            }
        }

        try
        {
            var view = _view!;
            if (view.ReadInt32(0) != Magic || view.ReadInt32(4) != Version)
                return false;
            long beat = view.ReadInt64(48);
            long age = now - beat;
            if (beat <= 0 || age is < 0 or > 2200)
            {
                CloseMapping();
                return false;
            }
            int mode = view.ReadInt32(28);
            int vocal = view.ReadInt32(32);
            int music = view.ReadInt32(36);
            if (mode is < 0 or > 3 ||
                vocal is < 0 or > 100 ||
                music is < 0 or > 100) return false;

            long dspTick = view.ReadInt64(56);
            long dspAge = dspTick == 0 ? long.MaxValue : now - dspTick;
            status = new StemKaraokeSnapshot(
                (KaraokeStemMode)mode, vocal, music,
                view.ReadInt32(40) == 1 && dspAge is >= 0 and <= 2400,
                age, dspAge, view.ReadInt32(44),
                view.ReadInt64(64), view.ReadInt32(8), view.ReadInt32(12));
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or
            UnauthorizedAccessException or ArgumentException)
        {
            CloseMapping();
            return false;
        }
    }

    /// <summary>
    /// The C++ worker applies changes at most 75ms later and acknowledges
    /// its sequence. A request being written is never portrayed as success.
    /// </summary>
    internal bool Request(KaraokeStemMode mode, int vocals, int instrumental,
        out int requestSequence)
    {
        requestSequence = 0;
        if ((int)mode is < 0 or > 3 ||
            vocals is < 0 or > 100 ||
            instrumental is < 0 or > 100 ||
            !TryRead(out var status) || status == null) return false;

        try
        {
            var view = _view!;
            int next = Math.Max(status.RequestedSequence,
                status.AppliedSequence) + 1;
            if (next <= 0) next = 1;
            // All payload fields must precede request sequence. The native
            // worker reads sequence twice and discards partially replaced
            // requests. Do not modify any MP3 or Foobar playhead.
            view.Write(16, (int)mode);
            view.Write(20, vocals);
            view.Write(24, instrumental);
            Thread.MemoryBarrier();
            view.Write(8, next);
            Thread.MemoryBarrier();
            requestSequence = next;
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or
            UnauthorizedAccessException or ArgumentException)
        {
            CloseMapping();
            return false;
        }
    }

    private void CloseMapping()
    {
        _view?.Dispose();
        _map?.Dispose();
        _view = null;
        _map = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseMapping();
    }
}
