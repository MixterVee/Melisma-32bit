namespace MelismaLyricsViewer;

internal static class WordSpacingTests
{
    public static void Run(Action<bool, string> check)
    {
        var timed = new List<LrcLine>
        {
            new(45000, "Maybe you can show me howto love, maybe",
                Timed(45000, "Maybe ", "you ", "can ", "show ", "me ",
                    "how", "to ", "love, ", "maybe")),
            new(51000, "I'mgoing through",
                Timed(51000, "I'm", "going ", "through")),
            new(56000, "havetoo do too much",
                Timed(56000, "have", "too ", "do ", "too ", "much")),
            new(61000, "sunrise",
                Timed(61000, "sun", "rise")),
            new(64000, "I can't sleep",
                Timed(64000, "I ", "can't ", "sleep"))
        };
        var reference = new List<LrcLine>
        {
            new(45100, "Maybe you can show me how to love, maybe"),
            new(51100, "I'm going through"),
            new(56100, "have too do too much"),
            new(61100, "sunrise"),
            new(64100, "I can't sleep")
        };

        var (outcome, changed) = LyricWordSpacing.Restore(timed, reference);
        check(changed == 3, "Only three missing-English-space lines need repair");
        check(outcome[0].Text == "Maybe you can show me how to love, maybe",
            "Screenshot: howto is displayed as how to");
        check(outcome[1].Text == "I'm going through",
            "Screenshot: I'mgoing is displayed as I'm going");
        check(outcome[2].Text == "have too do too much",
            "Screenshot: havetoo is displayed as have too");
        check(outcome[3].Text == "sunrise",
            "True within-word syllable split stays combined as sunrise");
        check(outcome[4].Text == "I can't sleep",
            "Already-correct lyrics stay unchanged");
        check(outcome[0].Words!.Count == timed[0].Words!.Count,
            "Never merges or splits actual source-timed tokens");
        check(outcome[0].Words![6].Text.StartsWith(" to"),
            "Inserted space belongs to the following timed token");
        check(outcome.All((line, i) => line.Words != null &&
                line.Words.Zip(timed[i].Words!, (a, b) =>
                    a.AtMs == b.AtMs && a.EndMs == b.EndMs).All(x => x)),
            "Every original word start and end timestamp is identical");

        var wrongWords = new List<LrcLine>
        {
            new(45000, "Maybe you can show me howto love, maybe",
                Timed(45000, "Maybe ", "you ", "can ", "show ", "me ",
                    "how", "to ", "love, ", "maybe"))
        };
        var wrongReference = new List<LrcLine>
        {
            new(45100, "Maybe you can show me how to live, maybe")
        };
        var (untouched, changes) = LyricWordSpacing.Restore(
            wrongWords, wrongReference);
        check(changes == 0 && untouched[0].Text == wrongWords[0].Text,
            "Never borrow text from a different vocal lyric");
        var (far, farCount) = LyricWordSpacing.Restore(wrongWords,
            [new LrcLine(180000, "Maybe you can show me how to love, maybe")]);
        check(farCount == 0 && far[0].Text == wrongWords[0].Text,
            "Do not borrow chorus spacing from a different song position");
        var (noProvider, missingCount) = LyricWordSpacing.Restore(
            wrongWords, []);
        check(missingCount == 0 && noProvider[0].Text == wrongWords[0].Text,
            "Missing reference does not create guessed spaces");
    }

    private static IReadOnlyList<LrcWord> Timed(long start,
        params string[] words) =>
        words.Select((word, i) =>
            new LrcWord(start + i * 220L, word, start + i * 220L + 180))
            .ToList();
}
