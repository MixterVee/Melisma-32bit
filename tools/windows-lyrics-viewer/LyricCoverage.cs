namespace MelismaLyricsViewer;

/// <summary>
/// Word-timed TTML documents can omit the beginning of the song. Do not
/// mistake word-level detail for complete vocal coverage. A verified line-
/// timed LRCLIB document can supply missing early vocal cues without
/// fabricating word timing or altering later genuine TTML syllables.
/// </summary>
internal static class LyricCoverage
{
    private const long MinimumGapMs = 4000;
    private const long SafeSeamMs = 900;

    public static int MissingIntroLineCount(
        IReadOnlyList<LrcLine>? wordLines,
        IReadOnlyList<LrcLine>? comparison)
    {
        if (wordLines is not { Count: > 0 } ||
            comparison is not { Count: > 0 }) return 0;

        long firstWordCue = wordLines.Min(x => x.AtMs);
        long firstReferenceCue = comparison.Min(x => x.AtMs);
        if (firstWordCue - firstReferenceCue < MinimumGapMs) return 0;

        // Keep away from the overlapping boundary: a slightly earlier cue
        // may already be the same vocal line timed differently.
        return comparison.Count(x => x.AtMs >= 0 &&
            x.AtMs < firstWordCue - SafeSeamMs);
    }

    public static (List<LrcLine> Lines, int IntroCount) AddMissingIntro(
        IReadOnlyList<LrcLine> wordLines,
        IReadOnlyList<LrcLine>? lineFallback)
    {
        var original = wordLines.OrderBy(x => x.AtMs).ToList();
        int missing = MissingIntroLineCount(original, lineFallback);
        if (missing < 2 || lineFallback is not { Count: > 0 })
            return (original, 0);

        long seam = original[0].AtMs - SafeSeamMs;
        var intro = lineFallback
            .Where(x => x.AtMs >= 0 && x.AtMs < seam &&
                !string.IsNullOrWhiteSpace(x.Text))
            .OrderBy(x => x.AtMs)
            // Source is line-timed: preserve exact line times and text, but
            // never borrow word timing from another recording.
            .Select(x => new LrcLine(x.AtMs, x.Text, null))
            .ToList();
        if (intro.Count < 2) return (original, 0);

        // If the selected word source already contains its intro, the
        // MissingIntroLineCount threshold above keeps it untouched.
        intro.AddRange(original);
        intro.Sort((a, b) => a.AtMs.CompareTo(b.AtMs));
        return (intro, intro.Count - original.Count);
    }
}
