namespace MelismaLyricsViewer;

/// <summary>Alpha weights for the Cinema cover-to-cover crossfade.</summary>
internal static class CoverFade
{
    // With a new cover present, leave the old image in place and reveal the
    // new image over it. Without one, fade the old image to the placeholder.
    internal static float OldOpacity(float progress, bool newCoverExists) =>
        newCoverExists ? 1f : 1f - Math.Clamp(progress, 0f, 1f);

    internal static float NewOpacity(float progress) =>
        Math.Clamp(progress, 0f, 1f);
}
