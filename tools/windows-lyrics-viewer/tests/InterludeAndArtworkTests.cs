namespace MelismaLyricsViewer;

internal static class InterludeAndArtworkTests
{
    internal static void Run(Action<bool, string> check)
    {
        IReadOnlyList<LrcLine> intro =
        [
            new LrcLine(6000, "Hello", [new LrcWord(6000, "Hello", 6600)]),
            new LrcLine(16000, "again", [new LrcWord(16000, "again", 17000)])
        ];
        check(LyricsInterlude.At(intro, 0) == new InterludeSpan(0, 6000),
            "Three dots appear in a six-second instrumental introduction");
        check(LyricsInterlude.At(intro, 5400) != null &&
            LyricsInterlude.At(intro, 5500) == null,
            "Dots disappear exactly 500ms before the next sung line");
        check(LyricsInterlude.At(intro, 6200) == null,
            "No instrumental dots are painted during a sung word");
        check(LyricsInterlude.At(intro, 6600) == new InterludeSpan(6600, 16000),
            "Precise word end times start interlude directly after vocals end");
        check(LyricsInterlude.At(intro, 15499) != null &&
            LyricsInterlude.At(intro, 15500) == null,
            "Interlude collapses before a later vocal entry too");
        check(LyricsInterlude.At(intro, 20000) == null,
            "No unbounded outro interlude after the last lyric");
        check(LyricsInterlude.At([], 0) == null,
            "The no-provider placeholder is handled by the canvas");
        check(LyricsInterlude.At([new LrcLine(2999, "Hello")], 0) == null,
            "Less than three seconds does not create intro dots");
        check(LyricsInterlude.At([new LrcLine(3000, "Hello")], 0) != null,
            "Exactly three seconds qualifies as an Android-style interlude");
        IReadOnlyList<LrcLine> onsetOnly =
        [
            new LrcLine(1000, "short phrase", [new LrcWord(1000, "short "),
                new LrcWord(2000, "phrase")]),
            new LrcLine(9000, "following")
        ];
        check(LyricsInterlude.At(onsetOnly, 3000) == null &&
            LyricsInterlude.At(onsetOnly, 3500) != null,
            "Onset-only words receive a cautious gap estimate without changing times");
        IReadOnlyList<LrcLine> plain =
        [new LrcLine(1000, "Hello"), new LrcLine(7500, "There")];
        check(LyricsInterlude.At(plain, 3500) != null,
            "Plain LRC can show a later instrumental gap");
        check(CoverFade.OldOpacity(0f, true) == 1f &&
            CoverFade.OldOpacity(0.5f, true) == 1f &&
            CoverFade.NewOpacity(0.5f) == 0.5f,
            "During cover crossfade the old image remains full until new overlays it");
        check(CoverFade.OldOpacity(1f, false) == 0f &&
            CoverFade.NewOpacity(1f) == 1f,
            "Missing new artwork fades to placeholder; loaded cover ends opaque");
    }
}
