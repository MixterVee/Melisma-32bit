using System.IO.MemoryMappedFiles;
using System.Text;

namespace MelismaLyricsViewer;

internal sealed record NativeTagSample(ulong Generation, string Title, string Artist, string Album);

/// <summary>Read-only metadata side-channel from Foobar bridge v0.2.</summary>
internal sealed class NativeTrackMetadataReader : IDisposable
{
    private const string MappingName = @"Local\MelismaFoobarMetadata_v1";
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;

    public bool TryRead(out NativeTagSample sample)
    {
        sample = new NativeTagSample(0, "", "", "");
        try
        {
            if (_view == null)
            {
                _mapping = MemoryMappedFile.OpenExisting(MappingName, MemoryMappedFileRights.Read);
                _view = _mapping.CreateViewAccessor(0, 792, MemoryMappedFileAccess.Read);
            }
            if (_view.ReadUInt32(0) != 0x4D455441 || _view.ReadUInt32(4) != 1)
                return false;
            for (int i = 0; i < 3; i++)
            {
                int before = _view.ReadInt32(8);
                if ((before & 1) != 0) continue;
                ulong generation = _view.ReadUInt64(16);
                var data = new byte[768];
                _view.ReadArray(24, data, 0, data.Length);
                int after = _view.ReadInt32(8);
                if (before != after || (after & 1) != 0) continue;

                static string Field(byte[] data, int start)
                {
                    int end = Array.IndexOf(data, (byte)0, start, 256);
                    return Encoding.UTF8.GetString(data, start,
                        (end < 0 ? start + 256 : end) - start).Trim();
                }
                sample = new NativeTagSample(generation,
                    Field(data, 0), Field(data, 256), Field(data, 512));
                return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or
            UnauthorizedAccessException or ObjectDisposedException or ArgumentException)
        {
            Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        _view?.Dispose();
        _mapping?.Dispose();
        _view = null;
        _mapping = null;
    }
}
