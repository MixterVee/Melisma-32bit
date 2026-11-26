namespace MelismaLyricsViewer;

internal static class AndroidParityTests
{
    internal static void Run(Action<bool, string> check)
    {
        IReadOnlyList<LrcLine> lyric =
        [
            new LrcLine(6000, "First", [new LrcWord(6000, "First", 6600)]),
            new LrcLine(16000, "Second", [new LrcWord(16000, "Second", 16800)])
        ];
        check(LyricsInterlude.Before(lyric, 0) ==
            new InterludeSpan(0, 6000),
            "Android inserts an intro interlude row BEFORE the first vocal");
        check(LyricsInterlude.Before(lyric, 1) ==
            new InterludeSpan(6600, 16000),
            "Android inserts the second row between vocal lines");
        check(LyricsInterlude.Before(lyric, 2) == null,
            "No interlude is inserted after the final lyric");
        check(AndroidInterludeRow.IntroCenter(0f, 50f, 62f) == -56f,
            "Intro dots are positioned BEFORE the next lyric, not after it");
        check(AndroidInterludeRow.BetweenCenter(60f, 230f) == 145f,
            "Gap dots occupy their own row between two lyrics");
        check(AndroidInterludeRow.FocusFraction == 0.42f &&
            AndroidInterludeRow.DotFontMultiplier == 1.3f &&
            AndroidInterludeRow.DotGapMultiplier == 0.14f,
            "Android uses 42%-height focus, 1.3em dots and 0.14em dot gap");

        var gap = new InterludeSpan(1000, 13000);
        var first = AndroidInterludeRow.DotBounds(gap, 0);
        var second = AndroidInterludeRow.DotBounds(gap, 1);
        var third = AndroidInterludeRow.DotBounds(gap, 2);
        check(first.StartMs == gap.StartMs &&
            first.EndMs == second.StartMs &&
            second.EndMs == third.StartMs &&
            third.EndMs == gap.EndMs - 500,
            "Each dot occupies a consecutive third and final dot hides 500ms early");
        check(AndroidInterludeRow.DotProgress(gap, 1000, 0) == 0f &&
            AndroidInterludeRow.DotProgress(gap, 1000, 1) == 0f &&
            AndroidInterludeRow.DotProgress(gap, first.EndMs, 0) == 1f &&
            AndroidInterludeRow.DotProgress(gap, first.EndMs, 1) == 0f,
            "Dots progress in sequence instead of all bouncing in a short loop");
        check(Math.Abs(AndroidInterludeRow.DotScale(0f) - 0.75f) < 0.001f &&
            Math.Abs(AndroidInterludeRow.DotScale(0.7f) - 1.05f) < 0.001f &&
            Math.Abs(AndroidInterludeRow.DotScale(1f) - 1f) < 0.001f,
            "Android dot scale swells and settles gently");
        check(AndroidInterludeRow.DotOpacity(0f, false) == 0.35f &&
            AndroidInterludeRow.DotOpacity(1f, false) == 1f,
            "Dot opacity follows the Android non-simple animation");
        // Video regression: in v0.6.16 the last dot was still raised as
        // Windows hid all three 500ms before the next vocal line. The
        // third dot must settle FIRST and disappear only at lyric entry.
        check(AndroidInterludeRow.FinalDotLandingFactor(gap,
                  gap.EndMs - 650) == 1f &&
              AndroidInterludeRow.FinalDotLandingFactor(gap,
                  gap.EndMs - 400) is > 0f and < 1f &&
              AndroidInterludeRow.FinalDotLandingFactor(gap,
                  gap.EndMs - 120) == 0f,
            "Final dot moves smoothly downward and reaches baseline before singing");
        check(AndroidInterludeRow.FinalDotLandingFactor(gap,
                  gap.EndMs - 80) == 0f &&
              AndroidInterludeRow.FinalDotsOpacity(gap,
                  gap.EndMs - 120) == 1f,
            "Third dot remains visibly landed before fade-out begins");
        check(AndroidInterludeRow.FinalDotsOpacity(gap,
                  gap.EndMs - 55) is > 0f and < 1f &&
              AndroidInterludeRow.FinalDotsOpacity(gap,
                  gap.EndMs) == 0f,
            "Dots only fade out in the final 110ms immediately before vocals");
        check(LyricsInterlude.Around(lyric, 15750) != null &&
              LyricsInterlude.At(lyric, 15750) == null,
            "Final 500ms stay inside the interlude's landing window even after pre-hide");

        check(Math.Abs(AndroidWordScale.Target(0f) - 0.95f) < 0.001f &&
            Math.Abs(AndroidWordScale.Target(0.7f) - 1.0505f) < 0.001f &&
            Math.Abs(AndroidWordScale.Target(1f) - 1f) < 0.001f,
            "Word enlargement follows Android spring target keyframes");

        var held = new AndroidSpring(1f, 0.88f, 0.64f);
        held.SetGoal(1.0505f);
        float largestJump = 0f, previous = 1f;
        for (int i = 0; i < 60; i++)
        {
            float now = held.Step(1f / 40f);
            largestJump = Math.Max(largestJump, Math.Abs(now - previous));
            previous = now;
        }
        check(float.IsFinite(previous) && largestJump < 0.02f &&
            Math.Abs(previous - 1.0505f) < 0.012f,
            "Android analytic spring enlarges smoothly even with 40fps frames");

        check(AndroidInterludeRow.Focus(gap, 2000, 130f, 235f, 40f) >= 129.5f &&
            AndroidInterludeRow.Focus(gap, 12500, 130f, 235f, 40f) > 130f &&
            Math.Abs(AndroidInterludeRow.Focus(gap, gap.EndMs, 130f, 235f, 40f)
                - 235f) < 0.01f,
            "Scrolling focuses the dot row, then glides to the next vocal");
    }
}
