namespace MelismaLyricsViewer;

internal static class FoobarRestartTests
{
    internal static void Run(Action<bool, string> check)
    {
        var travelling = new NativeTagSample(1, "Travelin' Soldier", "Dixie Chicks", "Home");
        var different = new NativeTagSample(1, "Another Song", "Another Artist", "Other Album");
        check(FoobarSessionSafety.AcceptNativeTags(true, 1, 1, travelling),
            "Matching fresh Foobar bridge tags can load word-timed lyrics");
        check(!FoobarSessionSafety.AcceptNativeTags(false, 1, 1, travelling),
            "Stale shared-memory tags are rejected even if generation matches");
        check(!FoobarSessionSafety.AcceptNativeTags(true, 0, 1, travelling) &&
              !FoobarSessionSafety.AcceptNativeTags(true, 1, 0, travelling),
            "Stopped/uninitialized Foobar cannot claim an existing song");
        check(!FoobarSessionSafety.AcceptNativeTags(true, 1, 2, travelling),
            "Previous song metadata is rejected when playback generation changes");
        check(FoobarSessionSafety.AcceptNativeTags(true, 1, 1, different),
            "A freshly reconnected bridge's new tags replace an older song");

        // A restarted v0.3 bridge can reuse generation=1. The reconnect
        // transition, not just generation inequality, must force reload.
        check(FoobarSessionSafety.NewSong(1, 1, newlyConnected: true),
            "Same numerical generation after Foobar restart still loads new song");
        check(!FoobarSessionSafety.NewSong(1, 1, newlyConnected: false),
            "Same track in unchanged live session does not refetch every frame");
        check(FoobarSessionSafety.NewSong(1, 2, newlyConnected: false),
            "New track while Foobar stays open invalidates existing lyrics");
        check(!FoobarSessionSafety.NewSong(0, 0, newlyConnected: true),
            "No song has been started in new Foobar session");

        check(FoobarSessionSafety.MustClearTrack(true, false, 1) &&
              FoobarSessionSafety.MustClearTrack(true, true, 0),
            "Closing Foobar or stopping playback clears the current lyric document");
        check(!FoobarSessionSafety.MustClearTrack(true, true, 2) &&
              !FoobarSessionSafety.MustClearTrack(false, false, 0),
            "Pause retains lyrics and a stopped empty display needs no reset");
        check(!FoobarSessionSafety.AcceptWindowsFallback(true, false) &&
              !FoobarSessionSafety.AcceptWindowsFallback(true, true),
            "Stale Windows media metadata cannot resurrect last track after native bridge was used");
        check(FoobarSessionSafety.AcceptWindowsFallback(false, false),
            "Legacy Windows-only installs still accept media-session fallback");

        var restored = new NativeTagSample(4582301, "New Song", "New Artist", "New Album");
        check(FoobarSessionSafety.AcceptNativeTags(true, 1, 4582301, restored) &&
              !FoobarSessionSafety.AcceptNativeTags(true, 1, 4582301, travelling),
            "Bridge v0.4 generation epoch distinguishes a restarted Foobar process");
    }
}
