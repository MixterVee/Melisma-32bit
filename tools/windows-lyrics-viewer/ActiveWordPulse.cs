namespace MelismaLyricsViewer;

/// <summary>
/// A small Android-style grow/lift effect for the currently sung word.
/// Uses the same authoritative word timestamps as cyan highlighting.
/// This is a visual effect only; it does not alter lyrics or playback time.
/// </summary>
internal static class ActiveWordPulse
{
    internal readonly record struct Focus(
        int WordIndex, float Progress, float Scale, float LiftEm);

    internal static Focus? At(IReadOnlyList<LrcWord>? words,
        long timeMs, bool simpleMode)
    {
        if (words is not { Count: > 0 }) return null;
        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (timeMs < word.AtMs) break;

            // TTML/YRC gives us the real duration. An onset-only LRC
            // supplies no duration; use a short 320ms purely VISUAL pulse
            // without changing the onset-only highlight semantics.
            long duration = word.EndMs > word.AtMs
                ? word.EndMs - word.AtMs : 320L;
            long end = word.AtMs + duration;
            if (i + 1 < words.Count && words[i + 1].AtMs > word.AtMs)
                end = Math.Min(end, words[i + 1].AtMs);
            if (timeMs >= end) continue;

            float progress = Math.Clamp((timeMs - word.AtMs) /
                (float)Math.Max(1, end - word.AtMs), 0f, 1f);
            // Android's wordScale spline rises while the word is sung,
            // overshoots slightly, then returns to normal at the word end.
            // On smaller Windows TV fonts a touch more amplitude makes the
            // movement legible without shuffling any line-layout positions.
            float peak = simpleMode ? 1.045f : 1.085f;
            float scale = progress < 0.60f
                ? 1f + (peak - 1f) * Smooth(progress / 0.60f)
                : 1f + (peak - 1f) *
                    (1f - Smooth((progress - 0.60f) / 0.40f));
            float lift = (scale - 1f) * (simpleMode ? 0.18f : 0.26f);
            return new Focus(i, progress, scale, lift);
        }
        return null;
    }

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
