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
        check(LyricsInterlude.Around(intro, 5500) ==
                  new InterludeSpan(0, 6000) &&
              LyricsInterlude.Around(intro, 5999) != null &&
              LyricsInterlude.Around(intro, 6000) == null,
            "Preview stays on the next lyric after dots vanish, until singing begins");
        check(LyricPreviewMotion.UpcomingIndex(-1, 57) == 0 &&
              LyricPreviewMotion.UpcomingIndex(9, 57) == 10,
            "Intro and instrumental gap always select the forthcoming lyric");
        check(LyricPreviewMotion.Reveal(0) == 0f &&
              LyricPreviewMotion.Reveal(350) > 0f &&
              LyricPreviewMotion.Reveal(750) == 1f,
            "Found lyrics begin fading in immediately, finish within 750ms");
        check(LyricPreviewMotion.Ahead(new InterludeSpan(5000, 16000), 5000) == 0f &&
              LyricPreviewMotion.Ahead(new InterludeSpan(5000, 16000), 5900) == 1f &&
              LyricPreviewMotion.Ahead(new InterludeSpan(5000, 16000), 15500) == 1f,
            "Next line smoothly moves into focus, then stays steady through gap end");
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
        check(CoverFade.ReuseOutgoingAfterStop(0.4f, 0.7f) &&
              CoverFade.ReuseOutgoingAfterStop(1f, 0.7f) &&
              CoverFade.ReuseOutgoingAfterStop(0.4f, 1f),
            "Rapid Foobar restart keeps the outgoing cover while either configured fade is in progress");
        check(!CoverFade.ReuseOutgoingAfterStop(1f, 1f),
            "Expired outgoing artwork cannot be resurrected onto an unrelated song");
        check(CoverFade.OldOpacity(0f, true) == 1f &&
              CoverFade.NewOpacity(0f) == 0f &&
              CoverFade.NewOpacity(0.6f) > 0f &&
              CoverFade.NewOpacity(0.6f) < 1f,
            "New embedded cover reveals over intact outgoing cover, not instantly over blank");

        string first = LyricPreviewMotion.DocumentKey(
            10, "Travelin' Soldier", "Dixie Chicks", 55, 1000, 180000);
        string again = LyricPreviewMotion.DocumentKey(
            11, "Travelin' Soldier", "Dixie Chicks", 55, 1000, 180000);
        check(first != again,
            "Replaying identical cached lyrics restarts the independent reveal animation");
        check(first == LyricPreviewMotion.DocumentKey(
            10, "Travelin' Soldier", "Dixie Chicks", 55, 1000, 180000),
            "Repeated paint of one document preserves fade progress");
        check(LyricPreviewMotion.Reveal(0) == 0f &&
              LyricPreviewMotion.Reveal(375) > 0f &&
              LyricPreviewMotion.Reveal(375) < 1f &&
              LyricPreviewMotion.Reveal(750) == 1f,
            "New track fades in over 750ms without postponing word timestamps");
    }
}
