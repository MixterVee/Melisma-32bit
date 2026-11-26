namespace MelismaLyricsViewer;

internal static class LyricAlignmentTests
{
    internal static void Run(Action<bool, string> check)
    {
        float center = LyricRowAlignment.RowLeft(100f, 800f, 400f, true);
        float left = LyricRowAlignment.RowLeft(100f, 800f, 400f, false);
        check(center == 300f && left == 100f,
            "Lyrics mode centers each wrapped row while Cinema stays left aligned");
        check(LyricRowAlignment.RowLeft(100f, 800f, 250f, true) == 375f,
            "Shorter lyric rows receive correct independent horizontal centering");
        float sameBaselineWordCenter = LyricRowAlignment.WordCenter(
            center, 125f, 225f);
        check(sameBaselineWordCenter == 475f,
            "Sung word uses the same centered row origin as its normal text");
        check(LyricRowAlignment.GrownWordLeft(sameBaselineWordCenter, 50f) == 450f &&
              LyricRowAlignment.GrownWordLeft(sameBaselineWordCenter, 55f) == 447.5f,
            "Enlarging a word changes measured width but NEVER its fixed center");
        check(LyricRowAlignment.GrownWordLeft(sameBaselineWordCenter, 55f) +
            55f / 2f == sameBaselineWordCenter,
            "Spring-scaled text grows equally to the left and right, with no drag");

        var spring = new AndroidSpring(1f, 0.88f, 0.64f);
        spring.SetGoal(AndroidWordScale.Target(0.7f));
        for (int frame = 0; frame < 30; frame++) spring.Step(1f / 40f);
        check(spring.Value > 1.01f,
            "Actual drawn word font scale increases above 100% during held words");
    }
}
