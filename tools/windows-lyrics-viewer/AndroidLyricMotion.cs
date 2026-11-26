namespace MelismaLyricsViewer;

/// <summary>
/// Placement and timing copied from the Android Melisma lyric layout:
/// LyricsMetrics.dotFontSizePx=font*1.3, .dotGapPx=dotFont*.14;
/// LyricsLayout.interludeLayout: 3 timed bullet units in their own row,
/// inserted by Lyrics.withInterludes before the next lead line.
/// </summary>
internal static class AndroidInterludeRow
{
    internal const float FocusFraction = 0.42f;
    internal const float DotFontMultiplier = 1.3f;
    internal const float DotGapMultiplier = 0.14f;
    internal const int PreHideMs = 500;

    internal static float Height(float lyricFontPx, bool compact = false) =>
        lyricFontPx * (DotFontMultiplier * 1.1f + (compact ? 0.18f : 0.36f));

    internal static float IntroCenter(float nextCenter, float nextTextHeight,
        float dotRowHeight) =>
        nextCenter - (nextTextHeight + dotRowHeight) * 0.5f;

    internal static float BetweenCenter(float priorCenter, float nextCenter) =>
        (priorCenter + nextCenter) * 0.5f;

    private static float Ease(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    // Focus the dots (not the upcoming lyrics) in the scrolling viewport.
    // Animate onto the dot row from the last vocal and into the next vocal,
    // keeping the upcoming text legible throughout an instrumental gap.
    internal static float Focus(InterludeSpan gap, long positionMs,
        float dotCenter, float nextCenter, float? lastVocalCenter)
    {
        float focus = dotCenter;
        if (lastVocalCenter.HasValue)
            focus = lastVocalCenter.Value +
                (dotCenter - lastVocalCenter.Value) *
                Ease((positionMs - gap.StartMs) / 480f);
        float approach = Ease((positionMs - (gap.EndMs - 780)) / 780f);
        return focus + (nextCenter - focus) * approach;
    }

    internal static (long StartMs, long EndMs) DotBounds(InterludeSpan gap, int index)
    {
        index = Math.Clamp(index, 0, 2);
        long total = Math.Max(1L, gap.EndMs - gap.StartMs);
        long padding = -(PreHideMs + 50L) / 3L;
        long third = total / 3L;
        long firstEnd = Math.Max(0, third + padding);
        long secondEnd = Math.Max(firstEnd, third * 2L + padding * 2L);
        long thirdEnd = Math.Max(secondEnd, total - PreHideMs);
        long from = index == 0 ? 0L : index == 1 ? firstEnd : secondEnd;
        long to = index == 0 ? firstEnd : index == 1 ? secondEnd : thirdEnd;
        return (gap.StartMs + from, gap.StartMs + to);
    }

    internal static float DotProgress(InterludeSpan gap, long positionMs, int index)
    {
        var (from, to) = DotBounds(gap, index);
        return to <= from ? (positionMs >= to ? 1f : 0f)
            : Math.Clamp((positionMs - from) / (float)(to - from), 0f, 1f);
    }

    internal static float DotScale(float t) =>
        t <= 0.7f ? 0.75f + 0.30f * Ease(t / 0.7f)
            : 1.05f - 0.05f * Ease((t - 0.7f) / 0.3f);
    internal static float DotLift(float t) =>
        t <= 0.9f ? -0.12f * Ease(t / 0.9f)
            : -0.12f * (1f - Ease((t - 0.9f) / 0.1f));

    // Android's third dot completes its 500ms-prehide sequence before the
    // next vocal. The Windows port hid the row while the damped Y-offset
    // spring still had a negative (raised) value. Add a deterministic landing
    // envelope near the end of the REAL lyric gap so the dot visibly returns
    // to the baseline before it disappears. It never changes lyric timing.
    internal const int FinalLandingWindowMs = 650;
    internal const int FinalLandingHoldMs = 120;
    internal const int FinalExitFadeMs = 110;

    internal static float FinalDotLandingFactor(InterludeSpan gap, long positionMs)
    {
        long remaining = gap.EndMs - positionMs;
        if (remaining >= FinalLandingWindowMs) return 1f;
        if (remaining <= FinalLandingHoldMs) return 0f;
        return Ease((remaining - FinalLandingHoldMs) /
            (float)(FinalLandingWindowMs - FinalLandingHoldMs));
    }

    internal static float FinalDotsOpacity(InterludeSpan gap, long positionMs)
    {
        // Keep the final *landed* dot briefly in view, then hand off to the
        // incoming lyric rather than vanishing while still lifted.
        long remaining = gap.EndMs - positionMs;
        return Ease(remaining / (float)FinalExitFadeMs);
    }
    internal static float DotOpacity(float t, bool simple) =>
        (simple ? 0.27f : 0.35f) +
        (1f - (simple ? 0.27f : 0.35f)) * Ease(t / 0.6f);
}

/// <summary>
/// Analytic underdamped spring adapted from Android Melisma's Spring.kt
/// (originally Fraktality spr, MIT). It is stable when a frame is delayed.
/// </summary>
internal sealed class AndroidSpring
{
    private readonly float _frequency, _damping;
    private float _value, _velocity, _goal;
    internal AndroidSpring(float initial, float frequency, float damping)
    {
        _value = _goal = initial;
        _frequency = frequency;
        _damping = damping;
    }
    internal float Value => _value;
    internal void SetGoal(float goal) => _goal = goal;
    internal void Snap(float value) { _value = _goal = value; _velocity = 0f; }

    internal float Step(float dt)
    {
        if (dt <= 0f) return _value;
        dt = Math.Clamp(dt, 0f, 1f / 15f);
        float f = _frequency * (2f * MathF.PI);
        float d = _damping;
        float c = MathF.Sqrt(Math.Max(0.0001f, 1f - d * d));
        float q = MathF.Exp(-d * f * dt);
        float i = MathF.Cos(dt * f * c);
        float j = MathF.Sin(dt * f * c);
        float z = j / c;
        float y = j / (f * c);
        float offset = _value - _goal;
        float value = (offset * (i + z * d) + _velocity * y) * q + _goal;
        _velocity = (_velocity * (i - z * d) - offset * (z * f)) * q;
        _value = value;
        return value;
    }
}

internal static class AndroidWordScale
{
    // Android LyricsAnim.wordScale: 0->.95, .7->1.0505, 1->1.
    private static float Ease(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }
    internal static float Target(float progress) =>
        progress <= 0.7f
            ? 0.95f + 0.1005f * Ease(progress / 0.7f)
            : 1.0505f - 0.0505f * Ease((progress - 0.7f) / 0.3f);
}
