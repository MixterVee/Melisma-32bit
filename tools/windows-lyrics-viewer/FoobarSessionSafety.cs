namespace MelismaLyricsViewer;

/// <summary>
/// A Foobar track generation is only meaningful within one live Foobar
/// process. On shutdown the v0.3 shared-memory reader can retain an orphaned
/// mapping, so a generation number alone is not proof of current playback.
/// </summary>
internal static class FoobarSessionSafety
{
    internal static bool AcceptNativeTags(bool clockFresh, int playbackState,
        ulong clockGeneration, NativeTagSample? tags) =>
        clockFresh && playbackState is 1 or 2 && clockGeneration > 0 &&
        tags is { Title.Length: > 0 } &&
        tags.Generation == clockGeneration;

    // Once a genuine native bridge has been observed, a stale Windows GSMTC
    // snapshot must not reintroduce the previous song after the bridge stops.
    // Users without a bridge retain their legacy Windows session fallback.
    internal static bool AcceptWindowsFallback(bool everHadNativeBridge,
        bool nativeBridgeFresh) =>
        !everHadNativeBridge && !nativeBridgeFresh;

    internal static bool NewSong(ulong previousGeneration,
        ulong currentGeneration, bool newlyConnected) =>
        currentGeneration > 0 &&
        (newlyConnected || previousGeneration != currentGeneration);

    internal static bool MustClearTrack(bool previouslyHadTrack,
        bool clockFresh, int playbackState) =>
        previouslyHadTrack && (!clockFresh || playbackState == 0);
}
