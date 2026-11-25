namespace MelismaLyricsViewer;

internal static class ActiveWordPulseTests
{
    internal static void Run(Action<bool, string> check)
    {
        IReadOnlyList<LrcWord> words =
        [
            new LrcWord(10000, "I ", 10300),
            new LrcWord(10300, "love ", 11000),
            new LrcWord(11000, "you", 11500)
        ];

        check(ActiveWordPulse.At(words, 9999, false) == null,
            "Before the first word there is no visual pulse");
        var start = ActiveWordPulse.At(words, 10000, false);
        check(start.HasValue && start.Value.WordIndex == 0 &&
            Math.Abs(start.Value.Scale - 1f) < 0.001f,
            "Active word starts at its actual source timestamp and normal size");
        var middle = ActiveWordPulse.At(words, 10450, false);
        check(middle.HasValue && middle.Value.WordIndex == 1 &&
            middle.Value.Scale > 1.02f && middle.Value.Scale <= 1.0851f,
            "Second word gently grows using its actual time");
        var peak = ActiveWordPulse.At(words, 10720, false);
        check(peak.HasValue && peak.Value.WordIndex == 1 &&
            peak.Value.Scale > 1.075f,
            "Active word has visible emphasis near midpoint");
        var finish = ActiveWordPulse.At(words, 10999, false);
        check(finish.HasValue && finish.Value.WordIndex == 1 &&
            finish.Value.Scale < 1.01f,
            "Emphasis settles at the end of recorded word duration");
        var following = ActiveWordPulse.At(words, 11000, false);
        check(following.HasValue && following.Value.WordIndex == 2,
            "At the exact next onset emphasis moves to next word");
        check(ActiveWordPulse.At(words, 11500, false) == null,
            "Word emphasis disappears on the real final end timestamp");

        var simple = ActiveWordPulse.At(words, 10720, true);
        check(simple.HasValue && simple.Value.Scale > 1f &&
            simple.Value.Scale < peak!.Value.Scale,
            "Simple mode has a smaller word grow animation");

        var seek = ActiveWordPulse.At(words, 10500, false);
        check(seek.HasValue && seek.Value.WordIndex == 1,
            "Seeking backward uses reported playhead, not a local animation clock");
        var pause1 = ActiveWordPulse.At(words, 10700, false);
        var pause2 = ActiveWordPulse.At(words, 10700, false);
        check(pause1 == pause2,
            "Paused playback freezes the animation deterministically");

        IReadOnlyList<LrcWord> onsetOnly =
        [
            new LrcWord(20000, "hello "),
            new LrcWord(20600, "there")
        ];
        check(ActiveWordPulse.At(onsetOnly, 20120, false) is { WordIndex: 0 },
            "Onset-only enhanced LRC receives a visual pulse without fake word times");
        check(ActiveWordPulse.At(onsetOnly, 20400, false) == null,
            "Onset-only visual pulse ends independently of lyric highlighting");
        check(ActiveWordPulse.At([], 20300, false) == null,
            "Line-only lyrics have no word grow effect");
    }
}
