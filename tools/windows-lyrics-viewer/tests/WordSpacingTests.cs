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
        check(outcome.Select((line, i) => line.Words != null &&
                line.Words.Zip(timed[i].Words!, (a, b) =>
                    a.AtMs == b.AtMs && a.EndMs == b.EndMs).All(x => x)).All(x => x),
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
        // Regression from the user's Travelin' Soldier screenshot: genuine
        // provider word timing exists, but several English lyric lines
        // were cached as uninterrupted character runs.
        var soldier = new List<LrcLine>
        {
            new(19500, "Hewaswaitin'forthe",
                Timed(19500, "He", "was", "waitin'", "for", "the")),
            new(21800, "businhisarmygreens",
                Timed(21800, "bus", "in", "his", "army", "greens")),
            new(24400, "Satdowninabooth",
                Timed(24400, "Sat", "down", "in", "a", "booth"))
        };
        check(LyricWordSpacing.NeedsRepair(soldier),
            "Screenshot: cached word-timed joined English lyrics are flagged for reference repair");
        var plainText = "He was waitin' for the\\nbus in his army greens\\nSat down in a booth";
        var (restoredPlain, plainFixed) = LyricWordSpacing.RestoreFromPlain(
            soldier, plainText.Replace("\\n", "\n"));
        check(plainFixed == 3,
            "Matching untimed LRCLIB lines repair all three English word boundaries");
        check(restoredPlain[0].Text == "He was waitin' for the" &&
              restoredPlain[1].Text == "bus in his army greens" &&
              restoredPlain[2].Text == "Sat down in a booth",
            "Spaces are displayed exactly as written by the matching plain-lyrics provider");
        check(restoredPlain.Select((line, i) =>
                line.Words is { Count: > 0 } &&
                string.Concat(line.Words.Select(w => w.Text)) == line.Text &&
                line.Words.Zip(soldier[i].Words!, (a, b) =>
                    a.AtMs == b.AtMs && a.EndMs == b.EndMs).All(v => v))
            .All(v => v),
            "Restored plain text never changes original word timing or karaoke character offsets");
        check(!LyricWordSpacing.NeedsRepair(restoredPlain),
            "Repaired cache is recognized as corrected, so repeated plays stay instant");

        var (incorrect, incorrectFixed) = LyricWordSpacing.RestoreFromPlain(
            soldier, "She was waitin' for the\\nbus in his army greens".Replace("\\n", "\n"));
        check(incorrectFixed == 1 &&
              incorrect[0].Text == soldier[0].Text,
            "Never copy spacing from a different lyric line, even if it looks similar");

        var (ambiguous, ambiguousFixed) = LyricWordSpacing.RestoreFromPlain(
            [soldier[0]], "He was waitin' for the\\nHe waswaitin' for the"
                .Replace("\\n", "\n"));
        check(ambiguousFixed == 0 &&
              ambiguous[0].Text == soldier[0].Text,
            "Conflicting whitespace patterns in repeated plain lines are rejected");

        check(!LyricWordSpacing.NeedsRepair(
                [new LrcLine(20000, "sunrise",
                    Timed(20000, "sun", "rise"))]),
            "Single words split into legitimate timed syllables are not treated as spacing failures");
    }

    private static IReadOnlyList<LrcWord> Timed(long start,
        params string[] words) =>
        words.Select((word, i) =>
            new LrcWord(start + i * 220L, word, start + i * 220L + 180))
            .ToList();
}
