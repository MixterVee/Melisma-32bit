namespace MelismaLyricsViewer;

internal static class CoverageTests
{
    public static void Run(Action<bool, string> check)
    {
        // Foobar is playing at 24s; real first TTML cue starts at 60.964s.
        var word = new List<LrcLine>
        {
            new(60964, "Chorus", [new LrcWord(60964, "Cho"),
                new LrcWord(61500, "rus")]),
            new(66862, "Chorus next", [new LrcWord(66862, "Chorus "),
                new LrcWord(67500, "next")])
        };
        var line = new List<LrcLine>
        {
            new(14000, "First verse"),
            new(20500, "Second phrase"),
            new(24000, "Third phrase"),
            new(33800, "Fourth phrase"),
            new(42000, "Fifth phrase"),
            new(61000, "Chorus duplicate")
        };
        check(LyricCoverage.MissingIntroLineCount(word, line) == 5,
            "Detect first-minute missing vocals");
        var (merged, added) = LyricCoverage.AddMissingIntro(word, line);
        check(added == 5 && merged.Count == 7,
            "Only missing intro cues added");
        int active = merged.FindLastIndex(x => x.AtMs <= 24000);
        check(active == 2, "24s playback now selects third vocal line");
        check(merged.Take(5).All(x => x.Words == null),
            "Fallback lyrics remain line-timed");
        check(merged.Skip(5).All(x => x.Words is { Count: > 1 }),
            "Genuine timed chorus stays word-timed");
        var early = new List<LrcLine>
        {
            new(11000, "First", [new LrcWord(11000, "Fi"), new LrcWord(11300, "rst")])
        };
        var (unchanged, skipped) = LyricCoverage.AddMissingIntro(early, line);
        check(skipped == 0 && unchanged.Count == 1,
            "Complete original word-timed intro is not replaced");
        check(LyricCoverage.MissingIntroLineCount(word, null) == 0,
            "Never invent intro timestamps without fallback");
    }
}
