namespace MelismaLyricsViewer;

/// <summary>
/// Presentation-only easing for readable upcoming lyrics. Never shifts the
/// source timestamps or changes which word is currently being sung.
/// </summary>
internal static class LyricPreviewMotion
{
    internal const int RevealDurationMs = 750;
    internal const int FocusAheadDurationMs = 850;

    private static float Ease(float value)
    {
        float t = Math.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // Playback epoch distinguishes two separate plays of the SAME song
    // even when the document is served instantly from the same disk cache.
    // Without this, lyrics could appear fully opaque on song restart or a
    // rapid Foobar close/open with no blank paint frame in between.
    internal static string DocumentKey(ulong trackEpoch, string title,
        string artist, int lineCount, long firstLineMs, long lastLineMs) =>
        trackEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "\\u001f" + title + "\\u001f" + artist + "\\u001f" +
        lineCount + "\\u001f" + firstLineMs + "\\u001f" + lastLineMs;

    // Fade newly available lyrics into view even if the first vocal is
    // dozens of seconds later. Uses display time, not playback time.
    internal static float Reveal(long millisecondsSinceAvailable) =>
        Ease(millisecondsSinceAvailable / (float)RevealDurationMs);

    // During long instrumental gaps, gently bring the next lyric to the
    // reading position. Hold there until its timestamp, including the
    // final half-second after the bouncing dots have disappeared.
    internal static float Ahead(InterludeSpan gap, long playheadMs) =>
        Ease((playheadMs - gap.StartMs) / (float)FocusAheadDurationMs);

    internal static int UpcomingIndex(int activeIndex, int lineCount) =>
        lineCount <= 0 ? -1 : Math.Clamp(activeIndex + 1, 0, lineCount - 1);
}
