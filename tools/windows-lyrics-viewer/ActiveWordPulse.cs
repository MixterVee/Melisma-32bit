namespace MelismaLyricsViewer;

/// <summary>
/// Smooth timing-driven word emphasis. The cyan fill always uses the
/// provider's exact word timestamps; this scales a visual layer only.
///
/// In v0.6.7 a tiny word and a held note both grew by 8.5% over 60% of
/// their own duration. Fast words therefore visibly popped. Android
/// integrates animation with a physical spring, not that sharp one-word
/// tween. Use a gentle zero-velocity envelope and adapt the amplitude to
/// how long the word actually lasts. Short syllables barely swell; held
/// words have time for a more visible, graceful breath.
/// </summary>
internal static class ActiveWordPulse
{
    internal readonly record struct Focus(
        int WordIndex, float Progress, float Scale, float LiftEm);

    public static Focus? At(IReadOnlyList<LrcWord>? words,
        long timeMs, bool simpleMode)
    {
        if (words is not { Count: > 0 }) return null;

        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (timeMs < word.AtMs) break;

            // Word timestamps stay authoritative. For enhanced-LRC with
            // onsets only, create a brief VISUAL pulse, not a new lyric
            // timing duration. If a next word arrives, end the old visual
            // pulse exactly on that next onset.
            long duration = word.EndMs > word.AtMs
                ? word.EndMs - word.AtMs : 320L;
            long end = word.AtMs + duration;
            if (i + 1 < words.Count && words[i + 1].AtMs > word.AtMs)
                end = Math.Min(end, words[i + 1].AtMs);
            if (timeMs >= end) continue;

            float effectiveDuration = Math.Max(1L, end - word.AtMs);
            float progress = Math.Clamp(
                (timeMs - word.AtMs) / effectiveDuration, 0f, 1f);

            // A smooth sin² envelope starts and ends at zero velocity.
            // It never jumps to a new scale, even if a source provides
            // a very short syllable duration.
            float wave = MathF.Sin(MathF.PI * progress);
            float envelope = wave * wave;

            // Don't try to cram a 6–8% pop into a 120-ms word. Very short
            // words receive at most ~1% scale; 300-ms words ~1.7%, and
            // longer 700-ms words can swell ~5.5%.
            float longWord = Smooth((effectiveDuration - 140f) / 660f);
            float amplitude = 0.010f + 0.048f * longWord;
            if (simpleMode) amplitude *= 0.48f;

            float scale = 1f + amplitude * envelope;
            float lift = (scale - 1f) *
                (simpleMode ? 0.14f : 0.22f);
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
