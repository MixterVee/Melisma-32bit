using System.Globalization;
using System.Text;

namespace MelismaLyricsViewer;

/// <summary>
/// Timed NetEase YRC can omit spaces between adjacent English words. Reuse
/// spelling/spacing only from a *matching* alternate provider lyric line.
/// Keep the actual timed provider's characters and every word timestamp.
/// </summary>
internal static class LyricWordSpacing
{
    private const long MaxAlignmentDistanceMs = 12000;

    private static string Letters(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char ch in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(ch)) continue;
            // These glyphs have equivalent typography but often differ by
            // provider; apostrophes and dashes are compared consistently.
            char normalized = ch switch
            {
                '\u2018' or '\u2019' => '\'',
                '\u2010' or '\u2011' or '\u2013' or '\u2014' => '-',
                _ => ch
            };
            builder.Append(char.ToLowerInvariant(normalized));
        }
        return builder.ToString();
    }

    internal static (List<LrcLine> Lines, int FixedLines) Restore(
        IReadOnlyList<LrcLine> source, IReadOnlyList<LrcLine> reference)
    {
        var repaired = new List<LrcLine>(source.Count);
        int fixedLines = 0;
        foreach (LrcLine line in source)
        {
            if (line.Words is not { Count: > 1 } ||
                line.Text.Length == 0 || !line.Text.Any(char.IsLetter))
            {
                repaired.Add(line);
                continue;
            }

            string key = Letters(line.Text);
            if (key.Length < 5)
            {
                repaired.Add(line);
                continue;
            }

            var best = reference
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.Text) &&
                    Math.Abs(candidate.AtMs - line.AtMs) <= MaxAlignmentDistanceMs &&
                    Letters(candidate.Text) == key)
                .OrderBy(candidate => Math.Abs(candidate.AtMs - line.AtMs))
                .FirstOrDefault();

            if (best == null || !TryRealign(line.Words, best.Text,
                    out var updatedWords, out string updatedText) ||
                updatedText == line.Text)
            {
                repaired.Add(line);
                continue;
            }

            // The reference supplies spacing *only*. No lyric words or
            // timestamps are copied from a different provider/recording.
            repaired.Add(line with { Text = updatedText, Words = updatedWords });
            fixedLines++;
        }
        return (repaired, fixedLines);
    }

    private static bool TryRealign(IReadOnlyList<LrcWord> words,
        string referenceText, out IReadOnlyList<LrcWord> result,
        out string text)
    {
        result = words;
        text = string.Concat(words.Select(w => w.Text));

        // Must match all non-whitespace characters in exactly the same
        // order. Refuse even slightly different lyrics rather than
        // inventing text or reassigning timings.
        if (Letters(text) != Letters(referenceText)) return false;

        var output = new List<LrcWord>(words.Count);
        int cursor = 0;
        foreach (LrcWord timed in words)
        {
            var chars = new StringBuilder();
            foreach (char actual in timed.Text)
            {
                if (char.IsWhiteSpace(actual)) continue;
                bool spaceBefore = false;
                while (cursor < referenceText.Length &&
                       char.IsWhiteSpace(referenceText[cursor]))
                {
                    spaceBefore = output.Count > 0 || chars.Length > 0;
                    cursor++;
                }
                if (spaceBefore &&
                    (chars.Length == 0 || !char.IsWhiteSpace(chars[^1])))
                {
                    chars.Append(' ');
                }
                if (cursor >= referenceText.Length) return false;
                chars.Append(actual);
                cursor++;
            }
            if (chars.Length == 0) return false;
            output.Add(timed with { Text = chars.ToString() });
        }
        string merged = string.Concat(output.Select(w => w.Text)).Trim();
        if (merged.Length == 0 || Letters(merged) != Letters(text))
            return false;
        if (merged.Length != output.Sum(w => w.Text.Length)) return false;
        result = output;
        text = merged;
        return true;
    }
}
