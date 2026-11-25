using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MelismaLyricsViewer;

/// <summary>
/// Reads Apple/AMLL TTML timings from the actual <span begin/end> values.
/// Never manufactures syllable timestamps from line duration or word length.
/// </summary>
internal static class WordTimedLyrics
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly HashSet<string> ExcludedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "x-translation", "x-roman", "x-bg", "x-background"
    };

    public static List<LrcLine> ParseTtml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > 2_000_000)
            return [];

        using var source = new StringReader(xml);
        using var reader = XmlReader.Create(source, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2_000_000
        });
        XDocument doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var results = new List<LrcLine>();
        foreach (var p in doc.Descendants().Where(e => e.Name.LocalName == "p"))
        {
            if (results.Count >= 5000) break;
            if (Excluded(p)) continue;

            var fullText = new StringBuilder();
            var segments = new List<LrcWord>();
            bool untimedContent = false;
            AddSegments(p.Nodes(), fullText, segments, ref untimedContent);
            var rendered = Whitespace.Replace(fullText.ToString(), " ").Trim();
            if (rendered.Length == 0) continue;

            bool hasLineStart = TryTime(Attr(p, "begin"), out long lineStart);
            if (!hasLineStart && segments.Count > 0)
                lineStart = segments[0].AtMs;
            else if (!hasLineStart) continue;

            // Normalizing any whitespace in the middle could desync our
            // character offsets; when it matters, retain line-level timing.
            bool validWords = !untimedContent && segments.Count > 1 &&
                segments.All(w => w.AtMs >= 0) &&
                segments.Zip(segments.Skip(1), (a, b) => a.AtMs <= b.AtMs).All(x => x);

            List<LrcWord>? words = null;
            if (validWords)
            {
                // Align leading/trailing whitespace, preserving embedded
                // spaces as part of the next timed token for precise indexing.
                var fixedSegments = segments.ToList();
                fixedSegments[0] = fixedSegments[0] with
                {
                    Text = fixedSegments[0].Text.TrimStart()
                };
                fixedSegments[^1] = fixedSegments[^1] with
                {
                    Text = fixedSegments[^1].Text.TrimEnd()
                };
                if (string.Concat(fixedSegments.Select(w => w.Text)) == rendered)
                    words = fixedSegments;
            }

            results.Add(new LrcLine(lineStart, rendered, words));
        }
        results.Sort((a, b) => a.AtMs.CompareTo(b.AtMs));
        return results;
    }

    private static void AddSegments(IEnumerable<XNode> nodes,
        StringBuilder all, List<LrcWord> timed, ref bool untimed)
    {
        foreach (var node in nodes)
        {
            if (node is XText text)
            {
                string value = Whitespace.Replace(text.Value, " ");
                all.Append(value);
                if (!string.IsNullOrWhiteSpace(value))
                    untimed = true;
                else if (timed.Count > 0 && value.Length > 0 &&
                    !timed[^1].Text.EndsWith(' '))
                {
                    // TTML often writes the separator BETWEEN sibling spans
                    // rather than inside either span. Attach it to the
                    // preceding timed chunk so char offsets stay aligned.
                    timed[^1] = timed[^1] with
                    {
                        Text = timed[^1].Text + value
                    };
                }
                continue;
            }

            if (node is not XElement e || Excluded(e)) continue;
            var childSpans = e.Elements().Where(x => x.Name.LocalName == "span").ToList();
            if (e.Name.LocalName == "span" && childSpans.Count == 0)
            {
                string part = Whitespace.Replace(e.Value, " ");
                if (part.Length == 0) continue;
                all.Append(part);

                bool validStart = TryTime(Attr(e, "begin"), out long begin);
                bool validEnd = TryTime(Attr(e, "end"), out long end);
                if (!validStart)
                {
                    untimed = true;
                    continue;
                }
                // The word may have a known start but no known end. That
                // permits step highlighting only: no invented duration.
                timed.Add(new LrcWord(begin, part,
                    validEnd && end > begin ? end : -1));
                continue;
            }

            AddSegments(e.Nodes(), all, timed, ref untimed);
        }
    }

    private static bool Excluded(XElement element)
    {
        foreach (var a in element.Attributes())
        {
            if (a.Name.LocalName == "role" && ExcludedRoles.Contains(a.Value))
                return true;
        }
        return false;
    }

    private static string? Attr(XElement e, string name) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

    private static bool TryTime(string? text, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(text[..^2], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var milliseconds) &&
                milliseconds >= 0 && milliseconds < 24 * 3600_000)
            {
                value = (long)Math.Round(milliseconds);
                return true;
            }
            return false;
        }
        if (text.EndsWith('s'))
        {
            if (double.TryParse(text[..^1], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var seconds) &&
                seconds >= 0 && seconds < 86400)
            {
                value = (long)Math.Round(seconds * 1000);
                return true;
            }
            return false;
        }
        var tokens = text.Split(':');
        if (tokens.Length is not (2 or 3)) return false;
        int i = 0;
        int hours = 0;
        if (tokens.Length == 3 &&
            !int.TryParse(tokens[i++], NumberStyles.None,
                CultureInfo.InvariantCulture, out hours)) return false;
        if (!int.TryParse(tokens[i++], NumberStyles.None,
                CultureInfo.InvariantCulture, out int minutes)) return false;
        if (!double.TryParse(tokens[i], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double secondsValue)) return false;
        if (hours < 0 || hours > 23 || minutes < 0 || minutes >= 60 ||
            secondsValue < 0 || secondsValue >= 60) return false;
        value = (long)Math.Round((hours * 3600 + minutes * 60 + secondsValue) * 1000);
        return true;
    }

    public static bool HasWordTimings(IReadOnlyList<LrcLine> lines)
    {
        // Refrains, instrumental gaps, single-syllable calls and chorus
        // snippets often have legitimate line-only timing interspersed with
        // precisely timed words. Do not reject the entire TTML document
        // simply because fewer than half its lines have 2+ timed spans.
        int timed = lines.Count(x => x.Words is { Count: > 1 } &&
            x.Words.Any(w => w.AtMs >= 0));
        return timed >= Math.Min(3, Math.Max(1, lines.Count / 8));
    }
}

/// <summary>
/// Public, no-account AMLL TTML database provider. Strict title/artist
/// identification is preferred over displaying the wrong recording.
/// Source: https://amll.dev/reference/http-api/overview.html
/// </summary>
internal static class AmllWordProvider
{
    private const string Base = "https://api.amll.dev";
    private static readonly Regex KeepAlphanumeric =
        new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string Key(string text) =>
        KeepAlphanumeric.Replace(text.Normalize(NormalizationForm.FormKC)
            .ToLowerInvariant(), "");

    private static List<string> Strings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var raw) ||
            raw.ValueKind != JsonValueKind.Array) return [];
        return raw.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
    }

    // Different releases commonly add "(Remastered 2011)", "- Radio Edit"
    // or "(feat. ...)" to the *same recording*. Compare the underlying title
    // and primary artist, but never accept a completely different title.
    private static readonly Regex Edition = new(
        @"\s*(?:\((?:feat(?:uring)?|ft\.?|with|remaster(?:ed)?|radio edit|album version|single version|deluxe version|explicit|clean version)[^)]*\)|\[(?:feat(?:uring)?|ft\.?|remaster(?:ed)?|radio edit|deluxe)[^\]]*\])\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex YearEdition = new(
        @"\s*[-–]\s*(?:\d{4}\s*)?(?:remaster(?:ed)?|radio edit|single version)\b.*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string CoreTitle(string title)
    {
        string result = title.Trim();
        string previous;
        do
        {
            previous = result;
            result = Edition.Replace(result, "").Trim();
            result = YearEdition.Replace(result, "").Trim();
        } while (previous != result);
        return Key(result);
    }

    private static string PrimaryArtist(string artists)
    {
        return Regex.Split(artists,
            @"\s*(?:,|;|&|\band\b|\bfeat\.?|\bft\.?|\bfeaturing\b|\bx\b|\bwith\b)\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? artists.Trim();
    }

    internal static bool Matches(JsonElement item, string title, string artist)
    {
        string titleKey = Key(title);
        string core = CoreTitle(title);
        string primary = Key(PrimaryArtist(artist));
        if (core.Length < 2 || primary.Length < 2) return false;

        bool titleMatch = Strings(item, "musicNames").Any(name =>
            Key(name) == titleKey || CoreTitle(name) == core);
        bool artistMatch = Strings(item, "artistNames").Any(name =>
            Key(name) == primary ||
            Key(PrimaryArtist(name)) == primary);
        return titleMatch && artistMatch;
    }

    private static string UrlPart(string text) => Uri.EscapeDataString(text);

    public static async Task<List<LrcLine>?> FindAsync(
        HttpClient client, string title, string artist, CancellationToken token)
    {
        string primary = PrimaryArtist(artist);
        string coreTitle = Edition.Replace(title.Trim(), "").Trim();
        var queries = new List<string>
        {
            $"musicName={UrlPart(title)}&artistName={UrlPart(primary)}",
            $"musicName={UrlPart(coreTitle)}",
        };
        if (!string.Equals(coreTitle, title, StringComparison.OrdinalIgnoreCase))
            queries.Add($"musicName={UrlPart(title)}");

        var triedIds = new HashSet<long>();
        int downloaded = 0;
        foreach (var query in queries.Distinct())
        {
            token.ThrowIfCancellationRequested();
            using var response = await client.GetAsync(
                $"{Base}/v1/lyrics/search?{query}&pageSize=40",
                HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or
                HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway)
                return null;
            if (!response.IsSuccessStatusCode) continue;

            using var search = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(token));
            if (!search.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array) continue;

            foreach (var candidate in items.EnumerateArray())
            {
                if (candidate.ValueKind != JsonValueKind.Object ||
                    !Matches(candidate, title, artist) ||
                    !candidate.TryGetProperty("id", out var rawId) ||
                    !rawId.TryGetInt64(out long id) || id <= 0 ||
                    !triedIds.Add(id))
                    continue;

                // Some same-title entries have incomplete or line-only
                // TTML. Try another matching entry rather than giving up.
                if (++downloaded > 4) return null;
                using var lyricsResponse = await client.GetAsync(
                    $"{Base}/v1/lyrics/get?id={id}",
                    HttpCompletionOption.ResponseHeadersRead, token);
                if (!lyricsResponse.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(
                    await lyricsResponse.Content.ReadAsStringAsync(token));
                if (!doc.RootElement.TryGetProperty("data", out var entry) ||
                    entry.ValueKind != JsonValueKind.Object ||
                    !Matches(entry, title, artist) ||
                    !entry.TryGetProperty("lyrics", out var lyrics) ||
                    lyrics.ValueKind != JsonValueKind.String) continue;

                List<LrcLine> parsed;
                try { parsed = WordTimedLyrics.ParseTtml(lyrics.GetString() ?? ""); }
                catch (XmlException) { continue; }
                if (WordTimedLyrics.HasWordTimings(parsed)) return parsed;
            }
        }
        return null;
    }
}
