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
            peak.Value.Scale > 1.045f && peak.Value.Scale < 1.060f,
            "Held word has restrained, readable growth near its midpoint");
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

        // The video showed rapid "popping" on syllables that last only
        // 100–200ms. Peak zoom now depends on actual word duration, and
        // uses a smooth zero-velocity envelope rather than a steep tween.
        IReadOnlyList<LrcWord> shortSyllable =
        [
            new LrcWord(30000, "pop", 30120)
        ];
        var shortPeak = ActiveWordPulse.At(shortSyllable, 30060, false);
        check(shortPeak is { Scale: > 1.005f and <= 1.012f },
            "120ms syllable has a barely perceptible swell, not an 8.5% pop");
        var shortStart = ActiveWordPulse.At(shortSyllable, 30000, false);
        var shortEnd = ActiveWordPulse.At(shortSyllable, 30119, false);
        check(shortStart is { Scale: <= 1.0001f } &&
              shortEnd is { Scale: < 1.001f },
            "Short word starts and settles at near-normal size");

        IReadOnlyList<LrcWord> heldWord =
        [
            new LrcWord(40000, "long", 40900)
        ];
        var heldPeak = ActiveWordPulse.At(heldWord, 40450, false);
        check(heldPeak is { Scale: > 1.05f and <= 1.059f },
            "Held 900ms word grows gently, without excessive enlargement");
        bool smoothFrames = true;
        float previous = 1f;
        for (long t = 40000; t <= 40875; t += 25)
        {
            var frame = ActiveWordPulse.At(heldWord, t, false);
            if (!frame.HasValue ||
                Math.Abs(frame.Value.Scale - previous) > 0.012f)
                smoothFrames = false;
            previous = frame?.Scale ?? 1f;
        }
        check(smoothFrames,
            "At 40fps, held-word scale has no abrupt frame-to-frame jumps");
        var pausedFrame = ActiveWordPulse.At(heldWord, 40450, false);
        check(pausedFrame == ActiveWordPulse.At(heldWord, 40450, false),
            "Paused word remains at the same scale, with no wall-clock drift");
        var gentle = ActiveWordPulse.At(heldWord, 40450, true);
        check(gentle is { Scale: > 1f } &&
              gentle.Value.Scale < heldPeak!.Value.Scale,
            "Simple mode reduces the new smooth animation intensity");
    }
}
