using System.Globalization;
using System.Text.RegularExpressions;

namespace MelismaLyricsViewer;

internal static class LrcParser
{
    private static readonly Regex Timestamp =
        new(@"\[(?<m>\d{1,3}):(?<s>\d{1,2})(?:\.(?<f>\d{1,3}))?\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // Enhanced LRC permits optional word timestamps, e.g. <00:12.34>Hello <00:12.78>world.
    private static readonly Regex WordTimestamp =
        new(@"<(?<m>\d{1,3}):(?<s>\d{1,2})(?:\.(?<f>\d{1,3}))?>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static long Ms(Match match)
    {
        int minute = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        int second = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        var frac = match.Groups["f"].Value;
        int ms = frac.Length == 0 ? 0 :
            int.Parse(frac.PadRight(3, '0'), CultureInfo.InvariantCulture);
        return (minute * 60L + second) * 1000 + ms;
    }

    public static List<LrcLine> Parse(string value)
    {
        var entries = new List<LrcLine>();
        foreach (var line in value.Replace("\r", "").Split('\n'))
        {
            var matches = Timestamp.Matches(line);
            if (matches.Count == 0) continue;

            var rawText = Timestamp.Replace(line, "").Trim();
            var wordMatches = WordTimestamp.Matches(rawText);
            string text = WordTimestamp.Replace(rawText, "").Trim();
            if (text.Length == 0) text = "♪";
            List<LrcWord>? words = null;

            if (wordMatches.Count > 0)
            {
                var parsedWords = new List<LrcWord>();
                for (int i = 0; i < wordMatches.Count; i++)
                {
                    var match = wordMatches[i];
                    int start = match.Index + match.Length;
                    int finish = i + 1 < wordMatches.Count
                        ? wordMatches[i + 1].Index : rawText.Length;
                    var part = rawText.Substring(start, finish - start);
                    if (i == 0) part = part.TrimStart();
                    if (i == wordMatches.Count - 1) part = part.TrimEnd();
                    if (part.Length > 0)
                        parsedWords.Add(new LrcWord(Ms(match), part));
                }
                // If leading un-timed content exists, retain plain line mode.
                if (parsedWords.Count > 0 &&
                    string.Concat(parsedWords.Select(w => w.Text)) == text)
                    words = parsedWords;
            }

            foreach (Match m in matches)
                entries.Add(new LrcLine(Ms(m), text, words));
        }
        entries.Sort((a, b) => a.AtMs.CompareTo(b.AtMs));
        return entries;
    }
}
