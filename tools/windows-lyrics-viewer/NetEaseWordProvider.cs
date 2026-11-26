using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MelismaLyricsViewer;

/// <summary>
/// Port of Android Melisma's NeteaseProvider and YrcParser. Uses the public
/// NetEase search/get + song/lyric endpoints to find original YRC word
/// timestamps. If NetEase blocks anonymous requests or supplies only LRC,
/// returns no word timing so other providers continue unmodified.
/// </summary>
internal static class NetEaseWordProvider
{
    private const string Host = "https://music.163.com";
    private const float MatchThreshold = 0.62f;

    public static async Task<List<LrcLine>?> FindAsync(
        HttpClient client, string title, string artist, long durationMs,
        CancellationToken cancellation)
    {
        // Android will search by title when the player omits artist tags.
        // Require duration in that case so another artist's version cannot win.
        if (title.Length < 2 || (artist.Length < 2 && durationMs <= 0)) return null;
        string primary = AndroidLyricsMatch.SplitArtists(artist).FirstOrDefault() ??
            artist;
        string clean = AndroidLyricsMatch.CleanTitle(title);
        var queries = new[] { title + " " + primary, title,
            clean + " " + primary }.Distinct().ToList();
        foreach (string query in queries)
        {
            cancellation.ThrowIfCancellationRequested();
            string searchUrl = Host + "/api/search/get?s=" +
                Uri.EscapeDataString(query) +
                "&type=1&offset=0&total=true&limit=12";
            using var response = await GetAsync(client, searchUrl, cancellation);
            if (!response.IsSuccessStatusCode) continue;
            using var json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellation));
            if (!json.RootElement.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("songs", out var songs) ||
                songs.ValueKind != JsonValueKind.Array) continue;

            var matches = new List<(long Id, float Score)>();
            foreach (var song in songs.EnumerateArray())
            {
                if (song.ValueKind != JsonValueKind.Object ||
                    !song.TryGetProperty("id", out var idElement) ||
                    !idElement.TryGetInt64(out long id) || id <= 0) continue;
                string candidateTitle = GetText(song, "name");
                long candidateDuration = GetLong(song, "duration");
                if (candidateDuration <= 0)
                    candidateDuration = GetLong(song, "dt");
                var artists = new List<string>();
                if (song.TryGetProperty("artists", out var rawArtists) ||
                    song.TryGetProperty("ar", out rawArtists))
                {
                    if (rawArtists.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var listed in rawArtists.EnumerateArray())
                        {
                            if (listed.ValueKind == JsonValueKind.Object)
                            {
                                string name = GetText(listed, "name");
                                if (name.Length > 0) artists.Add(name);
                            }
                        }
                    }
                }
                float score = AndroidLyricsMatch.Score(title, artist, durationMs,
                    candidateTitle, string.Join(", ", artists), candidateDuration);
                if (artist.Length < 2 &&
                    (candidateDuration <= 0 ||
                     Math.Abs(candidateDuration - durationMs) > 5000 ||
                     !string.Equals(AndroidLyricsMatch.CleanTitle(title),
                        AndroidLyricsMatch.CleanTitle(candidateTitle),
                        StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (score >= MatchThreshold) matches.Add((id, score));
            }

            // Query a small number of high-confidence recordings. A result
            // with only line timing must not suppress another genuine YRC.
            foreach (var candidate in matches.OrderByDescending(m => m.Score).Take(2))
            {
                using var lyrics = await GetAsync(client, Host +
                    "/api/song/lyric?id=" +
                    candidate.Id.ToString(CultureInfo.InvariantCulture) +
                    "&lv=-1&kv=-1&tv=-1&rv=-1&yv=-1&ytv=-1&yrv=-1",
                    cancellation);
                if (!lyrics.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(
                    await lyrics.Content.ReadAsStringAsync(cancellation));
                string yrc = ExtractLyric(doc.RootElement, "yrc");
                if (yrc.Length == 0)
                    yrc = ExtractLyric(doc.RootElement, "klyric");
                var parsed = NeteaseYrcParser.Parse(yrc);
                string lrc = ExtractLyric(doc.RootElement, "lrc");
                if (WordTimedLyrics.HasWordTimings(parsed) &&
                    BetterLyricsWordProvider.PlausibleDuration(parsed, durationMs))
                {
                    // NetEase returns a normally spaced LRC *alongside*
                    // the YRC syllables in the very same HTTP response.
                    // Previously we threw this text away, even when YRC
                    // joined whole English words without spaces. Restore
                    // ONLY exact-matching line separators; retain every
                    // NetEase word onset/end timestamp unchanged.
                    if (!string.IsNullOrWhiteSpace(lrc))
                    {
                        var reference = LrcParser.Parse(lrc);
                        if (reference.Count > 0)
                            parsed = LyricWordSpacing.Restore(
                                parsed, reference).Lines;
                    }
                    return parsed;
                }

                // Android Melisma accepts the provider's genuine plain LRC
                // when YRC word timing is missing. The Windows port had been
                // throwing those real line timestamps away and leaving users
                // with untimed text instead. Keep the first plausible line-
                // timed answer immediately; other concurrent providers can upgrade it.
                if (!string.IsNullOrWhiteSpace(lrc))
                {
                    var lineTimed = LrcParser.Parse(lrc);
                    if (lineTimed.Count > 0 &&
                        BetterLyricsWordProvider.PlausibleDuration(lineTimed, durationMs))
                        return lineTimed;
                }
            }
        }
        return null;
    }

    private static string GetText(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var v) &&
            v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long GetLong(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var v) &&
            v.ValueKind == JsonValueKind.Number &&
            v.TryGetInt64(out long x) ? x : 0;

    private static string ExtractLyric(JsonElement document, string key)
    {
        if (!document.TryGetProperty(key, out var data) ||
            data.ValueKind != JsonValueKind.Object) return "";
        return GetText(data, "lyric");
    }

    private static async Task<HttpResponseMessage> GetAsync(
        HttpClient client, string url, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Referer", Host + "/");
        request.Headers.TryAddWithoutValidation("Origin", Host);
        request.Headers.TryAddWithoutValidation("Cookie", "appver=8.9.70; os=pc");
        return await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellation);
    }
}

/// <summary>
/// Android Melisma's YRC format: [lineStart,duration] then multiple
/// (wordStart,wordDuration,0)word tokens. Preserve exact timestamps;
/// no estimated word or syllable positions.
/// </summary>
internal static class NeteaseYrcParser
{
    private static readonly Regex Header =
        new(@"^\[(?<s>\d+),(?<d>\d+)\]", RegexOptions.Compiled);
    private static readonly Regex Word =
        new(@"\((?<s>\d+),(?<d>\d+),-?\d+\)(?<text>[^(]*)",
            RegexOptions.Compiled);
    // NetEase sometimes publishes timed credits at 0s, 1s and 2s before
    // the first actual vocal. Those are metadata, not karaoke cues.
    private static readonly Regex Credit =
        new(@"^(?:制作人|作词|作曲|编曲|填词|录音|混音|母带|监制|和声|词|曲|Producer|Produced\s+by|Written\s+by|Composed\s+by|Arranged\s+by|Lyrics?\s+by|Lyricist|Composer|Mix(?:ed)?\s+by|Mastered\s+by)\s*[:：]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);

    public static List<LrcLine> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 2_000_000)
            return [];
        var result = new List<LrcLine>();
        foreach (string source in raw.Split('\n'))
        {
            string line = source.Trim();
            if (line.Length == 0 || line.StartsWith('{')) continue;
            Match header = Header.Match(line);
            if (!header.Success ||
                !long.TryParse(header.Groups["s"].Value, out long lineStart))
                continue;
            string body = line[header.Length..];
            var words = new List<LrcWord>();
            var text = new System.Text.StringBuilder();
            bool previousEndedWithSpace = true;
            foreach (Match token in Word.Matches(body))
            {
                if (!long.TryParse(token.Groups["s"].Value, out long start) ||
                    !long.TryParse(token.Groups["d"].Value, out long duration) ||
                    start < 0 || duration < 0 || duration > 60000) continue;
                string rawWord = token.Groups["text"].Value;
                string trimmed = rawWord.Trim();
                if (trimmed.Length == 0)
                {
                    previousEndedWithSpace = true;
                    continue;
                }
                bool continuingWord = !previousEndedWithSpace &&
                    !rawWord.StartsWith(' ');
                if (!continuingWord && words.Count > 0)
                {
                    text.Append(' ');
                    words[^1] = words[^1] with
                    {
                        Text = words[^1].Text + " "
                    };
                }
                text.Append(trimmed);
                words.Add(new LrcWord(start, trimmed,
                    duration > 0 ? start + duration : -1));
                previousEndedWithSpace = rawWord.EndsWith(' ');
            }
            if (words.Count == 0 || text.Length == 0) continue;
            string lyricText = text.ToString();
            if (Credit.IsMatch(lyricText)) continue;
            long first = Math.Min(lineStart, words[0].AtMs);
            if (result.Count > 0 && first < result[^1].AtMs) continue;
            result.Add(new LrcLine(first, lyricText,
                words.Count > 1 ? words : null));
        }
        return result;
    }
}
