using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace MelismaLyricsViewer;

/// <summary>
/// Windows port of Melisma Android's LrcRedProvider:
/// https://github.com/MelismaApp/melisma/blob/v2.11.0/app/src/main/java/com/melisma/app/lyrics/provider/LrcRedProvider.kt
/// Public title+primary artist search -> score candidates -> ISRC TTML.
/// </summary>
internal static class LrcRedWordProvider
{
    private const string BaseUrl = "https://lrc.red";

    public static async Task<List<LrcLine>?> FindAsync(
        HttpClient client, string title, string artist, long durationMs,
        CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return null;
        string primary = AndroidLyricsMatch.SplitArtists(artist).FirstOrDefault() ?? artist;
        string uri = $"{BaseUrl}/search.json?q={Uri.EscapeDataString($"{title} {primary}")}";
        using var searchResponse = await GetAsync(client, uri, cancellation);
        if (!searchResponse.IsSuccessStatusCode) return null;
        using var response = JsonDocument.Parse(
            await searchResponse.Content.ReadAsStringAsync(cancellation));
        if (!response.RootElement.TryGetProperty("hits", out var hits) ||
            hits.ValueKind != JsonValueKind.Array) return null;

        var ranked = new List<(string Isrc, float Score, long DurationMs)>();
        foreach (var hit in hits.EnumerateArray())
        {
            if (hit.ValueKind != JsonValueKind.Object) continue;
            string code = Field(hit, "isrc");
            string candidateTitle = Field(hit, "title");
            string candidateArtist = Field(hit, "artist");
            double seconds = hit.TryGetProperty("duration", out var time) &&
                time.ValueKind == JsonValueKind.Number ? time.GetDouble() : 0;
            long candidateDurationMs = seconds > 0 && seconds < 86400
                ? (long)(seconds * 1000d) : 0;
            if (!Regex.IsMatch(code, @"^[A-Za-z0-9]{12}$")) continue;
            float score = AndroidLyricsMatch.Score(title, artist, durationMs,
                candidateTitle, candidateArtist, candidateDurationMs);
            if (score < 0.62f) continue;
            ranked.Add((code.ToUpperInvariant(), score, candidateDurationMs));
        }
        foreach (var hit in ranked.OrderByDescending(x => x.Score).Take(3))
        {
            cancellation.ThrowIfCancellationRequested();
            using var responseTtml = await GetAsync(client,
                $"{BaseUrl}/s/{Uri.EscapeDataString(hit.Isrc)}.ttml", cancellation);
            if (!responseTtml.IsSuccessStatusCode) continue;
            string xml = await responseTtml.Content.ReadAsStringAsync(cancellation);
            // The Android provider rewrites lrc: prefix aliases to itunes:;
            // our Windows TTML parser uses local names and needs no rewriting.
            List<LrcLine> lines;
            try { lines = WordTimedLyrics.ParseTtml(xml); }
            catch (XmlException) { continue; }
            if (!WordTimedLyrics.HasWordTimings(lines)) continue;
            if (!BetterLyricsWordProvider.PlausibleDuration(lines, durationMs))
                continue;
            return lines;
        }
        return null;
    }

    private static string Field(JsonElement item, string name) =>
        item.TryGetProperty(name, out var raw) &&
            raw.ValueKind == JsonValueKind.String
            ? raw.GetString() ?? "" : "";

    private static async Task<HttpResponseMessage> GetAsync(
        HttpClient client, string url, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Public lrc.red terms require naming the app, official URL, and a
        // contact path. GitHub Issues is the project's public contact point.
        request.Headers.UserAgent.ParseAdd(
            "MelismaUniversalWindows/0.6.2 (Windows; https://github.com/MixterVee/Melisma-32bit; contact https://github.com/MixterVee/Melisma-32bit/issues)");
        request.Headers.Accept.ParseAdd(
            "application/ttml+xml, application/xml, application/json, text/plain");
        return await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellation);
    }
}

/// <summary>
/// Closely follows Android's LyricsProvider.Matching.score for finding the
/// right recording without requiring exact title/artist tag equality.
/// </summary>
internal static class AndroidLyricsMatch
{
    private static readonly Regex Punctuation = new(
        @"[\p{P}\s]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Trailing = new(
        @"\s+-\s+(?:\d{4}\s+)?(?:remaster(?:ed)?(?:\s+\d{4})?|single version|album version|radio edit|live(?:\s+at\s+.*)?|acoustic|instrumental|mono|stereo|from\s+.*|feat\..*|ft\..*)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Parenthesis = new(
        @"\s*[\(\[](?:feat\.?|ft\.?|featuring|with|prod\.?|remaster(?:ed)?|live|acoustic|radio edit|single version|album version|explicit|clean|bonus track|deluxe)[^\)\]]*[\)\]]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string CleanTitle(string title)
    {
        string text = Trailing.Replace(Parenthesis.Replace(title, ""), "").Trim();
        return text.Length > 0 ? text : title;
    }

    public static string[] SplitArtists(string artist) =>
        Regex.Split(artist, @"\s*(?:,|、|&|\s+x\s+|\s+feat\.\s+|\s+ft\.\s+|;|\s+with\s+)\s*",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

    public static float Score(
        string title, string artist, long durationMs,
        string candidateTitle, string candidateArtist, long candidateDurationMs)
    {
        float nameScore = Math.Max(Similarity(title, candidateTitle),
            Similarity(CleanTitle(title), CleanTitle(candidateTitle)));
        // Even an exact artist/duration must not make a different song a
        // valid candidate. Android's weighted score is the base; this extra
        // guard prevents a known false positive with same-artist songs.
        if (nameScore < 0.45f) return 0f;
        var artists = SplitArtists(artist);
        string primary = artists.FirstOrDefault() ?? artist;
        float artistScore;
        if (artist.Length == 0 || candidateArtist.Length == 0)
            artistScore = 0.5f;
        else if (!ComparableScripts(artist, candidateArtist))
            artistScore = 0.5f;
        else
        {
            artistScore = Math.Max(
                Math.Max(Similarity(artist, candidateArtist),
                    Similarity(primary, candidateArtist)),
                artists.Select(a => Similarity(a, candidateArtist))
                    .DefaultIfEmpty(0).Max());
            if (primary.Length > 0 &&
                candidateArtist.Contains(primary, StringComparison.OrdinalIgnoreCase))
                artistScore = 1f;
            // Android treats strongly contradicting artists as an absolute
            // veto: the title and duration cannot override this.
            if (artistScore < 0.35f) return 0f;
        }
        float durationScore;
        if (durationMs <= 0 || candidateDurationMs <= 0)
            durationScore = 0.5f;
        else
        {
            long delta = Math.Abs(durationMs - candidateDurationMs);
            durationScore = delta <= 2000 ? 1f :
                delta <= 5000 ? 0.75f : delta <= 10000 ? 0.35f : 0f;
        }
        return nameScore * 0.5f + artistScore * 0.3f +
            durationScore * 0.2f;
    }

    private static bool ComparableScripts(string a, string b)
    {
        bool latinA = a.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        bool latinB = b.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        return (latinA && latinB) ||
            (!latinA && !latinB);
    }

    private static float Similarity(string a, string b)
    {
        string x = Punctuation.Replace(a.ToLowerInvariant(), " ").Trim();
        string y = Punctuation.Replace(b.ToLowerInvariant(), " ").Trim();
        if (x.Length == 0 || y.Length == 0) return 0f;
        if (x == y) return 1f;
        int[] previous = Enumerable.Range(0, y.Length + 1).ToArray();
        int[] current = new int[y.Length + 1];
        for (int i = 1; i <= x.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= y.Length; j++)
                current[j] = Math.Min(Math.Min(
                    current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return Math.Clamp(1f - previous[y.Length] /
            (float)Math.Max(x.Length, y.Length), 0f, 1f);
    }
}
