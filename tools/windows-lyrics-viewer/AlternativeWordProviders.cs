using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MelismaLyricsViewer;

/// <summary>
/// Better Lyrics supports account-free cached TTML and word-timed QQ QRC.
/// The TTML endpoint can return 401 when the song has no public cache entry;
/// that is not an error and is never treated as a line-only match.
/// API documentation: https://docs.betterlyrics.org/reference/get-lyrics
/// </summary>
internal static class BetterLyricsWordProvider
{
    private const string Base = "https://api.betterlyrics.org";

    internal static string Query(string title, string artist, string album, long durationMs)
    {
        string E(string s) => Uri.EscapeDataString(s.Trim());
        string duration = durationMs > 0
            ? Math.Round(durationMs / 1000.0).ToString(CultureInfo.InvariantCulture)
            : "";
        return $"s={E(title)}&a={E(artist)}&al={E(album)}&d={duration}";
    }

    public static async Task<(List<LrcLine> Lines, string Source)?> FindAsync(
        HttpClient client, string title, string artist, string album,
        long durationMs, CancellationToken token)
    {
        // Do not intentionally query without enough identity data.
        if (title.Trim().Length < 2 || artist.Trim().Length < 2)
            return null;
        string args = Query(title, artist, album, durationMs);

        // First try the wider TTML corpus (only publicly cached requests
        // can succeed without a private API key).
        try
        {
            using var response = await client.GetAsync($"{Base}/getLyrics?{args}",
                HttpCompletionOption.ResponseHeadersRead, token);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(token));
                if (doc.RootElement.TryGetProperty("ttml", out var payload) &&
                    payload.ValueKind == JsonValueKind.String)
                {
                    var lines = WordTimedLyrics.ParseTtml(payload.GetString() ?? "");
                    if (WordTimedLyrics.HasWordTimings(lines) &&
                        PlausibleDuration(lines, durationMs))
                        return (lines, "Better Lyrics · TTML");
                }
            }
            // 401/404/429 are expected misses on this public endpoint.
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            or XmlException or TaskCanceledException or InvalidOperationException) { }

        // The QQ service is documented as word-timed and does not require an
        // API key for cache misses. Preserve the actual QRC word durations.
        try
        {
            using var response = await client.GetAsync($"{Base}/qq/getLyrics?{args}",
                HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));
            if (!doc.RootElement.TryGetProperty("lyrics", out var payload) ||
                payload.ValueKind != JsonValueKind.String) return null;

            var lines = QrcWordParser.Parse(payload.GetString() ?? "", title, artist);
            return WordTimedLyrics.HasWordTimings(lines) &&
                PlausibleDuration(lines, durationMs)
                ? (lines, "Better Lyrics · QQ word-timed")
                : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            or XmlException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static bool PlausibleDuration(IReadOnlyList<LrcLine> lines, long durationMs)
    {
        if (durationMs <= 0 || lines.Count == 0) return true;
        // An unrelated longer recording should never be treated as a match.
        // Songs with long instrumental outro are still allowed.
        long lastWord = lines.Max(line => line.Words is { Count: > 0 }
            ? line.Words.Max(w => Math.Max(w.AtMs, w.EndMs))
            : line.AtMs);
        return lastWord <= durationMs + 8000;
    }
}

/// <summary>
/// QQ QRC: [lineStartMs,lineDurationMs]Word (wordStartMs,wordDurationMs)
/// Its tokens appear AFTER each word, unlike NetEase YRC. Timing is absolute.
/// https://docs.betterlyrics.org/docs/qrc-format
/// </summary>
internal static class QrcWordParser
{
    private static readonly Regex Header = new(
        @"^\[(?<s>\d+),(?<d>\d+)\]", RegexOptions.Compiled);
    private static readonly Regex Token = new(
        @"\((?<s>\d+),(?<d>\d+)\)", RegexOptions.Compiled);
    private static readonly Regex Credit = new(
        @"^(?:lyrics\s+by|composed\s+by|arranged\s+by|producer|作词|作曲|编曲|制作人)\s*[:：]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Exact = new(
        @"[^\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string Key(string s) => Exact.Replace(
        s.Normalize(NormalizationForm.FormKC).ToLowerInvariant(), "");

    public static List<LrcLine> Parse(string xml, string title = "", string artist = "")
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > 2_000_000) return [];

        // Parse the XML envelope securely, never use regex to parse attributes.
        string body;
        if (xml.TrimStart().StartsWith('<'))
        {
            using var source = new StringReader(xml);
            using var reader = XmlReader.Create(source, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 2_000_000
            });
            var tree = XDocument.Load(reader);
            body = tree.Descendants().FirstOrDefault(
                e => e.Name.LocalName.StartsWith("Lyric_", StringComparison.Ordinal))
                ?.Attributes().FirstOrDefault(a => a.Name.LocalName == "LyricContent")
                ?.Value ?? "";
        }
        else body = xml;

        if (body.Length == 0) return [];
        var entries = new List<LrcLine>();
        string? headerTitle = null, headerArtist = null;
        foreach (string raw in body.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("[ti:", StringComparison.OrdinalIgnoreCase))
                headerTitle = line[4..].TrimEnd(']');
            if (line.StartsWith("[ar:", StringComparison.OrdinalIgnoreCase))
                headerArtist = line[4..].TrimEnd(']');

            var match = Header.Match(line);
            if (!match.Success) continue;
            if (!long.TryParse(match.Groups["s"].Value, out long lineStart))
                continue;

            string content = line[match.Length..];
            var words = new List<LrcWord>();
            int previous = 0;
            foreach (Match m in Token.Matches(content))
            {
                string part = Spaces.Replace(content[previous..m.Index], " ");
                previous = m.Index + m.Length;
                if (part.Length == 0) continue;
                if (!long.TryParse(m.Groups["s"].Value, out long start) ||
                    !long.TryParse(m.Groups["d"].Value, out long duration)) continue;
                words.Add(new LrcWord(start, part,
                    duration > 0 ? start + duration : -1));
            }
            if (words.Count == 0 || !string.IsNullOrWhiteSpace(content[previous..]))
                continue;

            string lyric = string.Concat(words.Select(w => w.Text)).Trim();
            if (lyric.Length == 0 || Credit.IsMatch(lyric)) continue;
            if (!string.IsNullOrEmpty(title) && Key(lyric) == Key(title)) continue;
            if (!string.IsNullOrEmpty(artist) && Key(lyric) == Key(artist)) continue;
            // QQ often repeats "Song - Artist" as the first timed line.
            // It is a title card, not something that should be sung.
            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(artist) &&
                Key(lyric) == Key(title + " " + artist)) continue;

            // Drop the singer-only calls that sometimes appear before a line.
            if ((lyric.EndsWith(':') || lyric.EndsWith('：')) &&
                words.Count <= 2) continue;

            words[0] = words[0] with { Text = words[0].Text.TrimStart() };
            words[^1] = words[^1] with { Text = words[^1].Text.TrimEnd() };
            lyric = string.Concat(words.Select(w => w.Text));
            entries.Add(new LrcLine(lineStart, lyric,
                words.Count > 1 ? words : null));
        }

        if (!string.IsNullOrEmpty(headerTitle) && !string.IsNullOrEmpty(title) &&
            Key(headerTitle) != Key(title)) return [];
        if (!string.IsNullOrEmpty(headerArtist) && !string.IsNullOrEmpty(artist) &&
            // Artists in the tag can contain collaborations.
            !Key(headerArtist).Contains(Key(artist.Split(',')[0]),
                StringComparison.Ordinal)) return [];

        entries.Sort((a, b) => a.AtMs.CompareTo(b.AtMs));
        return entries;
    }
}
