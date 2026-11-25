namespace MelismaLyricsViewer;

internal readonly record struct InterludeSpan(long StartMs, long EndMs);

/// <summary>
/// Android Melisma inserts an interlude before the first lyric and in
/// instrumental gaps at least three seconds long, fading it out 500ms early.
/// The WinForms port uses the actual Foobar lyric timeline, never wall-clock
/// guesses, so pause and seek naturally preserve the correct position.
/// </summary>
internal static class LyricsInterlude
{
    internal const long MinimumGapMs = 3000;
    internal const long PreHideMs = 500;

    // The dot animation ends 500ms before singing, but the lyric preview
    // must stay focused on the upcoming line through that final half-second.
    internal static InterludeSpan? Around(IReadOnlyList<LrcLine> lines, long playheadMs) =>
        Find(lines, playheadMs, hideEarly: false);

    internal static InterludeSpan? At(IReadOnlyList<LrcLine> lines, long playheadMs) =>
        Find(lines, playheadMs, hideEarly: true);

    /// <summary>
    /// An interlude is a real layout row *before* the next vocal line in
    /// Android Melisma. Query by next-line index while building the layout,
    /// so its position never jumps when the playhead enters a gap.
    /// </summary>
    internal static InterludeSpan? Before(IReadOnlyList<LrcLine> lines, int nextIndex)
    {
        if (nextIndex < 0 || nextIndex >= lines.Count) return null;
        long start = nextIndex == 0 ? 0 :
            EstimatedVocalEnd(lines[nextIndex - 1], lines[nextIndex].AtMs);
        long end = Math.Max(0, lines[nextIndex].AtMs);
        return end - start >= MinimumGapMs
            ? new InterludeSpan(start, end) : null;
    }

    private static InterludeSpan? Find(IReadOnlyList<LrcLine> lines,
        long playheadMs, bool hideEarly)
    {
        if (lines.Count == 0 || playheadMs < 0) return null;
        long lastVocalEnd = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            long nextStart = Math.Max(0, line.AtMs);
            long gap = nextStart - lastVocalEnd;
            if (gap >= MinimumGapMs && playheadMs >= lastVocalEnd &&
                playheadMs < nextStart - (hideEarly ? PreHideMs : 0))
                return new InterludeSpan(lastVocalEnd, nextStart);
            if (playheadMs < nextStart) return null;
            long following = i + 1 < lines.Count ? lines[i + 1].AtMs : long.MaxValue;
            lastVocalEnd = Math.Max(lastVocalEnd, EstimatedVocalEnd(line, following));
        }
        // Like the Android withInterludes implementation, do not invent an
        // outro gap when the document has no later vocal line to bound it.
        return null;
    }

    private static long EstimatedVocalEnd(LrcLine line, long following)
    {
        long end = -1, lastOnset = -1;
        if (line.Words is { Count: > 0 })
        {
            foreach (var word in line.Words)
            {
                lastOnset = Math.Max(lastOnset, word.AtMs);
                if (word.EndMs > word.AtMs) end = Math.Max(end, word.EndMs);
            }
            // Onset-only enhanced LRC must not pretend to know vocal end;
            // allow a little room after the final reported onset.
            if (lastOnset > end) end = lastOnset + 1200;
        }
        if (end <= line.AtMs)
        {
            // Plain LRC has line onset only. Use the number of syllables as
            // a conservative display estimate, not as source lyric data.
            long estimate = Math.Clamp(line.Text.Length * 110L, 2000L, 8000L);
            end = line.AtMs + estimate;
        }
        return Math.Min(end, following);
    }
}
