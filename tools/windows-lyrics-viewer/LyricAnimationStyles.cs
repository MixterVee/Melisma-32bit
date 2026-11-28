namespace MelismaLyricsViewer;

/// <summary>
/// Presentation only. Never change timestamped word/line selection to
/// implement an effect. Android enlargement remains the existing default.
/// </summary>
internal enum MelismaLyricAnimation
{
    AndroidEnlargement,
    BouncingBall,
    Underline,
    Glow,
    HighlightOnly,
    Random,
    SoapBubbles
}

internal sealed class RandomLyricAnimation
{
    private ulong? _epoch;
    private MelismaLyricAnimation _selected;
    private readonly Func<int, int> _next;
    internal RandomLyricAnimation(Func<int, int>? next = null) =>
        _next = next ?? (n => System.Random.Shared.Next(n));

    internal MelismaLyricAnimation Resolve(MelismaLyricAnimation setting, ulong epoch)
    {
        if (setting != MelismaLyricAnimation.Random) return setting;
        if (_epoch != epoch)
        {
            MelismaLyricAnimation[] pool = [MelismaLyricAnimation.AndroidEnlargement,
                MelismaLyricAnimation.BouncingBall, MelismaLyricAnimation.Underline,
                MelismaLyricAnimation.Glow, MelismaLyricAnimation.SoapBubbles];
            if (_epoch.HasValue) pool = pool.Where(x => x != _selected).ToArray();
            _selected = pool[_next(pool.Length)];
            _epoch = epoch;
        }
        return _selected;
    }
}

internal static class LyricAnimationMotion
{
    private static float Smooth(float progress)
    {
        float t = Math.Clamp(progress, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // The ball sits over the active word, travels smoothly from the previous
    // word during the first third, then rises and lands once per sung word.
    internal static float BallTravel(float progress) =>
        Smooth(progress / 0.34f);

    // Y lift is positive in screen-independent units (rendering subtracts it).
    // Exactly zero at word onset and end so the ball lands cleanly.
    internal static float BallLift(float progress) =>
        MathF.Sin(MathF.PI * Math.Clamp(progress, 0f, 1f)) *
        MathF.Sin(MathF.PI * Math.Clamp(progress, 0f, 1f));

    // Word-following underline draws a 32%-word-wide indicator. Clamping
    // allows it to appear and disappear without drawing outside the word.
    internal static (float Start, float End) Underline(float progress)
    {
        float end = Smooth(progress);
        return (Math.Max(0f, end - 0.32f), end);
    }

    // A glow grows and recedes around its original word. No re-layout.
    internal static float Glow(float progress) =>
        0.32f + 0.68f * BallLift(progress);

    internal const int BubbleLifetimeMs = 1800;
    internal const int MaxVisibleBubbles = 18;
    internal static float BubbleAge(long position, long birth)
    {
        long elapsed = position - birth;
        return elapsed < 0 || elapsed >= BubbleLifetimeMs ? -1f : elapsed / (float)BubbleLifetimeMs;
    }
    internal static bool RequiresWordGeometry(MelismaLyricAnimation style) =>
        style != MelismaLyricAnimation.HighlightOnly && style != MelismaLyricAnimation.SoapBubbles;

    internal static bool UsesEnlargement(MelismaLyricAnimation style) =>
        style == MelismaLyricAnimation.AndroidEnlargement;
}
