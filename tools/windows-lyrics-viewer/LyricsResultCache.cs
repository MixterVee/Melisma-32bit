using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MelismaLyricsViewer;

/// <summary>
/// Android Melisma can display saved/prefetched lyrics without a network
/// lookup. This small Windows cache keeps the last good selected result
/// per tagged track; it is read before asking any provider and does not
/// alter or write back to the MP3 file.
/// </summary>
internal sealed class LyricsResultCache
{
    internal sealed record Entry(
        int FormatVersion,
        string Title,
        string Artist,
        string Album,
        long DurationMs,
        string SourceId,
        string SourceName,
        DateTime SavedUtc,
        List<LrcLine> Lines, string? PlainLyrics = null)
    {
        public bool WordTimed =>
            Lines.Count > 0 &&
            Lines.Count(l => l.Words is { Count: > 1 }) >
            Lines.Count / 2;
        public long FirstCueMs => Lines.Count > 0
            ? Lines.Min(line => line.AtMs) : long.MaxValue;
    }

    private readonly string _root;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private const int MaxBytes = 1_800_000;

    internal LyricsResultCache(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "MelismaUniversal", "WindowsLyricsViewer", "lyrics-cache");
    }

    private string PathFor(string title, string artist, string album) =>
        FileForKey(title.Trim().ToUpperInvariant() + "\n" +
            artist.Trim().ToUpperInvariant());

    // Read old v0.6.7-v0.6.9 entries without breaking existing caches.
    private string LegacyPath(string title, string artist, string album) =>
        FileForKey(title.Trim().ToUpperInvariant() + "\n" +
            artist.Trim().ToUpperInvariant() + "\n" +
            album.Trim().ToUpperInvariant());

    private string FileForKey(string key)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_root,
            Convert.ToHexString(digest.AsSpan(0, 12)) + ".json");
    }

    internal bool TryLoad(string title, string artist, string album,
        long durationMs, out Entry? entry)
    {
        entry = null;
        try
        {
            string path = PathFor(title, artist, album);
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                path = LegacyPath(title, artist, album);
                info = new FileInfo(path);
            }
            if (!info.Exists || info.Length is < 32 or > MaxBytes)
                return false;
            using var file = File.OpenRead(path);
            var result = JsonSerializer.Deserialize<Entry>(file);
            if (result == null || result.FormatVersion != 1 ||
                result.SavedUtc.Kind != DateTimeKind.Utc ||
                (result.SavedUtc > DateTime.UtcNow ||
                 DateTime.UtcNow - result.SavedUtc > MaxAge) ||
                !string.Equals(result.Title, title, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(result.Artist, artist, StringComparison.OrdinalIgnoreCase) ||
                durationMs > 0 && result.DurationMs > 0 &&
                    Math.Abs(durationMs - result.DurationMs) > 15000 ||
                !(Valid(result.Lines) || ValidPlain(result.PlainLyrics)) ||
                result.SourceId.Length is < 2 or > 48 ||
                result.SourceName.Length is < 2 or > 160)
                return false;
            entry = result;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                JsonException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    internal void Save(string title, string artist, string album,
        long durationMs, string sourceId, string sourceName,
        IReadOnlyList<LrcLine> lines)
    {
        if (!Valid(lines)) return;
        Write(title, artist, album, durationMs, sourceId, sourceName,
            lines, null);
    }

    internal void SavePlain(string title, string artist, string album,
        long durationMs, string plain)
    {
        if (!ValidPlain(plain)) return;
        Write(title, artist, album, durationMs, "lrclib", "LRCLIB", [], plain);
    }

    private void Write(string title, string artist, string album,
        long durationMs, string sourceId, string sourceName,
        IReadOnlyList<LrcLine> lines, string? plain)
    {
        if (string.IsNullOrWhiteSpace(title) ||
            sourceId.Length is < 2 or > 48 ||
            sourceName.Length is < 2 or > 160) return;

        string? pending = null;
        try
        {
            Directory.CreateDirectory(_root);
            string target = PathFor(title, artist, album);
            pending = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var file = File.Create(pending))
            {
                var record = new Entry(1, title, artist, album,
                    durationMs, sourceId, sourceName,
                    DateTime.UtcNow, lines.ToList(), plain);
                JsonSerializer.Serialize(file, record);
            }
            if (new FileInfo(pending).Length > MaxBytes) return;
            File.Move(pending, target, overwrite: true);
            pending = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            JsonException or NotSupportedException or ArgumentException)
        {
            // Cache failure must not affect lyric display or music playback.
        }
        finally
        {
            if (pending != null)
                try { File.Delete(pending); } catch (IOException) { }
        }
    }

    private static bool ValidPlain(string? plain) =>
        !string.IsNullOrWhiteSpace(plain) && plain.Length <= 30000;

    internal static bool ShouldReplace(Entry cached,
        LyricsSourceOrchestrator.Answer candidate)
    {
        if (candidate.Lines is not { Count: > 0 }) return false;
        if (candidate.WordTimed && !cached.WordTimed) return true;
        if (cached.WordTimed && !candidate.WordTimed) return false;
        if (candidate.Total < cached.Lines.Count * 0.75f)
            return false;

        long first = candidate.Lines.Min(x => x.AtMs);
        if (first > cached.FirstCueMs + 5000 &&
            candidate.Total < cached.Lines.Count * 1.25f)
            return false;
        if (first < cached.FirstCueMs - 1500) return true;
        return candidate.Id == cached.SourceId ||
            candidate.Total > cached.Lines.Count * 1.15f;
    }

    private static bool Valid(IReadOnlyList<LrcLine>? lines)
    {
        if (lines is not { Count: > 0 and <= 700 }) return false;
        long last = -1;
        foreach (var line in lines)
        {
            if (line == null || line.AtMs < 0 || line.AtMs < last ||
                line.Text.Length is < 1 or > 3000 ||
                line.Words?.Count > 300) return false;
            last = line.AtMs;
        }
        return true;
    }
}
