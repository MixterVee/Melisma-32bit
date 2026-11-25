using System.IO.MemoryMappedFiles;

namespace MelismaLyricsViewer;

// Foobar Bridge v0.3 publishes the actual front cover in a separate optional
// read-only mapping. Only bytes from the SAME track generation may be used.
internal sealed record NativeArtworkSample(
    ulong Generation, int Sequence, int State, byte[]? Bytes);

internal static class NativeArtworkState
{
    internal const int Pending = 0;
    internal const int HasCover = 1;
    internal const int NoCover = 2;
    internal const int MaxCoverBytes = 8 * 1024 * 1024;
    internal static bool Matches(ulong playbackGeneration, NativeArtworkSample sample) =>
        playbackGeneration > 0 && sample.Generation == playbackGeneration;
}

internal sealed class NativeArtworkReader : IDisposable
{
    private const string MappingName = @"Local\MelismaFoobarArtwork_v1";
    private const int HeaderSize = 32;
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private NativeArtworkSample? _last;

    public bool TryRead(out NativeArtworkSample sample)
    {
        sample = new NativeArtworkSample(0, 0, NativeArtworkState.Pending, null);
        try
        {
            if (_view == null)
            {
                _mapping = MemoryMappedFile.OpenExisting(MappingName,
                    MemoryMappedFileRights.Read);
                _view = _mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            }
            if (_view.ReadUInt32(0) != 0x4152544D || _view.ReadUInt32(4) != 1)
                return false;
            for (int i = 0; i < 3; i++)
            {
                int seq = _view.ReadInt32(8);
                if ((seq & 1) != 0) continue;
                int state = _view.ReadInt32(12);
                ulong gen = _view.ReadUInt64(16);
                uint size = _view.ReadUInt32(24);
                if (state is < 0 or > 2 ||
                    size > NativeArtworkState.MaxCoverBytes ||
                    (state == NativeArtworkState.HasCover && size < 128) ||
                    (state != NativeArtworkState.HasCover && size != 0))
                    return false;

                // The cover may be megabytes. Avoid re-copying it on every
                // 25/65/120ms lyric paint tick when the sequence is unchanged.
                if (_last != null && _last.Generation == gen &&
                    _last.Sequence == seq && _last.State == state)
                {
                    sample = _last;
                    return true;
                }

                byte[]? bytes = null;
                if (state == NativeArtworkState.HasCover)
                {
                    bytes = new byte[checked((int)size)];
                    _view.ReadArray(HeaderSize, bytes, 0, bytes.Length);
                }
                int after = _view.ReadInt32(8);
                if (seq != after || (after & 1) != 0) continue;
                sample = _last = new NativeArtworkSample(gen, seq, state, bytes);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ObjectDisposedException or ArgumentException or OverflowException)
        {
            Dispose();
        }
        return false;
    }

    public void Dispose()
    {
        _view?.Dispose();
        _mapping?.Dispose();
        _view = null;
        _mapping = null;
        _last = null;
    }
}
