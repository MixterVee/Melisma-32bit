using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MelismaLyricsViewer;

/// <summary>
/// Optional fallback when the foobar2000 Windows media session has no
/// thumbnail. Identifies a matching track on Apple's public iTunes Search
/// API and downloads its album cover. An actual media-session thumbnail
/// always takes priority over this best-effort catalog image.
/// </summary>
internal static class OnlineArtworkProvider
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly Regex Simplify =
        new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MelismaUniversal", "WindowsLyricsViewer", "artwork-cache");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(9) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "MelismaUniversalWindows/0.6.5 (https://github.com/MixterVee/Melisma-32bit)");
        return client;
    }

    private static string Key(string value) =>
        Simplify.Replace(value.Trim().ToLowerInvariant(), "");

    internal static bool IsMatch(
        string expectedTitle, string expectedArtist, string expectedAlbum,
        long durationMs, string trackName, string artistName,
        string albumName, long trackTimeMs)
    {
        string expected = Key(AndroidLyricsMatch.CleanTitle(expectedTitle));
        string actual = Key(AndroidLyricsMatch.CleanTitle(trackName));
        if (expected.Length < 2 || expected != actual) return false;
        string primary = Key(AndroidLyricsMatch.SplitArtists(expectedArtist)
            .FirstOrDefault() ?? expectedArtist);
        string listed = Key(artistName);
        if (primary.Length < 2 ||
            listed != primary &&
            !listed.Contains(primary, StringComparison.Ordinal))
            return false;
        if (durationMs > 0 && trackTimeMs > 0 &&
            Math.Abs(durationMs - trackTimeMs) > 15000) return false;
        if (!string.IsNullOrWhiteSpace(expectedAlbum) &&
            Key(expectedAlbum) != Key(albumName)) return false;
        return true;
    }

    private static string CacheKey(string title, string artist, string album)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{Key(title)}|{Key(artist)}|{Key(album)}"));
        return Convert.ToHexString(hash.AsSpan(0, 12)) + ".jpg";
    }

    public static async Task<byte[]?> FindAsync(string title, string artist,
        string album, long durationMs, CancellationToken cancellation)
    {
        if (title.Length < 2 || artist.Length < 2) return null;
        string cache = Path.Combine(CacheDir, CacheKey(title, artist, album));
        try
        {
            var entry = new FileInfo(cache);
            if (entry.Exists && entry.Length > 128 && entry.Length <= 6_000_000 &&
                DateTime.UtcNow - entry.LastWriteTimeUtc < TimeSpan.FromDays(30))
                return await File.ReadAllBytesAsync(cache, cancellation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failure to read an optional artwork cache must never block.
        }

        try
        {
            string query = Uri.EscapeDataString(title + " " + artist);
            string url = "https://itunes.apple.com/search?term=" + query +
                         "&media=music&entity=song&country=ca&limit=30";
            using var answer = await Client.GetAsync(url, cancellation);
            if (!answer.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(
                await answer.Content.ReadAsStringAsync(cancellation));
            if (!json.RootElement.TryGetProperty("results", out var tracks) ||
                tracks.ValueKind != JsonValueKind.Array) return null;

            string? selectedUrl = null;
            foreach (var track in tracks.EnumerateArray())
            {
                if (track.ValueKind != JsonValueKind.Object) continue;
                string Name(string key) =>
                    track.TryGetProperty(key, out var v) &&
                    v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                long d = track.TryGetProperty("trackTimeMillis", out var len) &&
                    len.ValueKind == JsonValueKind.Number && len.TryGetInt64(out var ms)
                        ? ms : 0;
                if (!IsMatch(title, artist, album, durationMs,
                    Name("trackName"), Name("artistName"), Name("collectionName"), d))
                    continue;
                selectedUrl = Name("artworkUrl100");
                if (selectedUrl.Length > 0) break;
            }
            if (string.IsNullOrWhiteSpace(selectedUrl)) return null;
            selectedUrl = selectedUrl.Replace("100x100", "600x600",
                StringComparison.Ordinal);
            if (!Uri.TryCreate(selectedUrl, UriKind.Absolute, out var pictureUrl) ||
                !string.Equals(pictureUrl.Scheme, Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                !(pictureUrl.Host.Equals("mzstatic.com", StringComparison.OrdinalIgnoreCase) ||
                  pictureUrl.Host.EndsWith(".mzstatic.com",
                    StringComparison.OrdinalIgnoreCase)))
                return null;

            using var art = await Client.GetAsync(pictureUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!art.IsSuccessStatusCode ||
                art.Content.Headers.ContentLength > 6_000_000 ||
                art.Content.Headers.ContentType?.MediaType is not
                    ("image/jpeg" or "image/png" or "image/webp")) return null;
            byte[] data = await art.Content.ReadAsByteArrayAsync(cancellation);
            if (data.Length is < 128 or > 6_000_000) return null;

            try
            {
                Directory.CreateDirectory(CacheDir);
                await File.WriteAllBytesAsync(cache, data, cancellation);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep the image in memory for this song without caching.
            }
            return data;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or JsonException or IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
