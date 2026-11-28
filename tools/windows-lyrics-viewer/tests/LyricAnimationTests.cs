namespace MelismaLyricsViewer;

internal static class LyricAnimationTests
{
    internal static void Run(Action<bool, string> check)
    {
        check((int)MelismaLyricAnimation.AndroidEnlargement == 0 &&
            Enum.GetValues<MelismaLyricAnimation>().Length == 7,
            "Existing Android enlargement stays default with seven animation choices");
        check(Enum.GetValues<MelismaLyricAnimation>().Distinct().Count() == 7,
            "All seven saved animation styles are distinct");
        var bubbleRandom = new RandomLyricAnimation(n => n - 1);
        check(bubbleRandom.Resolve(MelismaLyricAnimation.Random,1)==MelismaLyricAnimation.SoapBubbles &&
            bubbleRandom.Resolve(MelismaLyricAnimation.Random,2)!=MelismaLyricAnimation.SoapBubbles,
            "Random includes bubbles without immediate repeats");
        check(LyricAnimationMotion.BubbleAge(999,1000)==-1f &&
            LyricAnimationMotion.BubbleAge(1900,1000)==0.5f &&
            LyricAnimationMotion.BubbleAge(2800,1000)==-1f,
            "Bubbles are playhead-timed and expire");
        var random = new RandomLyricAnimation(_ => 0);
        var first = random.Resolve(MelismaLyricAnimation.Random, 10);
        check(first == random.Resolve(MelismaLyricAnimation.Random, 10), "Random stable across frames/displays/seeks");
        var second = random.Resolve(MelismaLyricAnimation.Random, 11);
        check(first != second && (int)second < 4, "Random chooses another moving effect on new track");
        check(random.Resolve(MelismaLyricAnimation.Glow, 11) == MelismaLyricAnimation.Glow && random.Resolve(MelismaLyricAnimation.Random, 11) == second, "Explicit style wins; return to Random retains song choice");
        check(LyricAnimationMotion.UsesEnlargement(
                MelismaLyricAnimation.AndroidEnlargement) &&
            !LyricAnimationMotion.UsesEnlargement(
                MelismaLyricAnimation.BouncingBall) &&
            !LyricAnimationMotion.UsesEnlargement(
                MelismaLyricAnimation.Underline) &&
            !LyricAnimationMotion.UsesEnlargement(
                MelismaLyricAnimation.Glow),
            "Only Android style changes the word size; other styles leave ink stationary");
        check(!LyricAnimationMotion.RequiresWordGeometry(
                MelismaLyricAnimation.HighlightOnly),
            "Highlight only adds no rendered shape or motion");
        check(LyricAnimationMotion.RequiresWordGeometry(
                MelismaLyricAnimation.Glow) &&
            LyricAnimationMotion.RequiresWordGeometry(
                MelismaLyricAnimation.Underline) &&
            LyricAnimationMotion.RequiresWordGeometry(
                MelismaLyricAnimation.BouncingBall),
            "Alternative styles follow real active-word geometry");

        check(LyricAnimationMotion.BallTravel(0f) == 0f &&
            LyricAnimationMotion.BallTravel(0.34f) == 1f &&
            LyricAnimationMotion.BallTravel(1f) == 1f,
            "Bouncing ball moves from previous to active word without a late teleport");
        check(LyricAnimationMotion.BallLift(0f) == 0f &&
            LyricAnimationMotion.BallLift(0.5f) > 0.99f &&
            LyricAnimationMotion.BallLift(1f) < 0.001f,
            "Bouncing ball starts and finishes at the same landing baseline");
        check(LyricAnimationMotion.BallTravel(-2f) == 0f &&
            LyricAnimationMotion.BallTravel(2f) == 1f &&
            LyricAnimationMotion.BallLift(-2f) == 0f,
            "Seeking and clamped time never move bouncing ball outside its word");

        var u0 = LyricAnimationMotion.Underline(0f);
        var u30 = LyricAnimationMotion.Underline(0.30f);
        var u70 = LyricAnimationMotion.Underline(0.70f);
        var u100 = LyricAnimationMotion.Underline(1f);
        check(u0.Start == 0f && u0.End == 0f &&
            u30.End > u0.End && u70.End > u30.End &&
            u100.End == 1f,
            "Underline progresses monotonically across genuine word duration");
        check(u30.Start >= 0f && u70.Start >= 0f &&
            u100.Start >= 0f && u100.Start < u100.End &&
            u100.End - u100.Start <= 0.321f,
            "Underline remains within source word ink, with a bounded moving trail");

        check(Math.Abs(LyricAnimationMotion.Glow(0f) - 0.32f) < 0.001f &&
            LyricAnimationMotion.Glow(0.5f) >= 0.99f &&
            Math.Abs(LyricAnimationMotion.Glow(1f) - 0.32f) < 0.001f,
            "Glow brightens during the sung word and returns to its resting level");
        check(LyricAnimationMotion.Glow(0.15f) ==
              LyricAnimationMotion.Glow(0.85f) ||
              Math.Abs(LyricAnimationMotion.Glow(0.15f) -
                  LyricAnimationMotion.Glow(0.85f)) < 0.0001f,
            "Glow envelope is smooth and symmetric about the middle of the word");
    }
}
