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

    // If playback stopped briefly, _current is null but _previous still
    // contains the outgoing album cover. Reuse that image when the new
    // track arrives before the user's configured artwork/background fades
    // have finished, instead of fading the new cover in from a blank panel.
    internal static bool ReuseOutgoingAfterStop(
        float backgroundProgress, float coverProgress) =>
        backgroundProgress < 1f || coverProgress < 1f;

}
