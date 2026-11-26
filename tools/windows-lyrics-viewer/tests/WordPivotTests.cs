using System.Drawing;

namespace MelismaLyricsViewer;

internal static class WordPivotTests
{
    internal static void Run(Action<bool, string> check)
    {
        var original = new RectangleF(337.25f, 180.5f, 128.5f, 46f);
        var pivot = ActiveWordGeometry.Pivot(original);
        check(pivot.X == 401.5f && pivot.Y == 203.5f,
            "Active-word pivot comes from the original complete-row glyph region");

        foreach (float scale in new[] { 0.95f, 1f, 1.0505f, 1.12f })
        {
            var resized = ActiveWordGeometry.ScaledBounds(original, scale);
            var center = ActiveWordGeometry.Pivot(resized);
            check(Math.Abs(center.X - pivot.X) < 0.001f &&
                  Math.Abs(center.Y - pivot.Y) < 0.001f,
                $"A {scale:P2} true glyph scale keeps both coordinates fixed");
            check(Math.Abs(resized.Width - original.Width * scale) < 0.01f &&
                  Math.Abs(resized.Height - original.Height * scale) < 0.01f,
                $"A {scale:P2} scale changes BOTH glyph dimensions equally");
            check(Math.Abs(original.Left - resized.Left -
                (resized.Right - original.Right)) < 0.001f &&
                  Math.Abs(original.Top - resized.Top -
                (resized.Bottom - original.Bottom)) < 0.001f,
                $"A {scale:P2} scale expands symmetrically in all directions");
        }

        check(ActiveWordGeometry.Pivot(
            ActiveWordGeometry.ScaledBounds(original, 1.05f)).X ==
            ActiveWordGeometry.Pivot(original).X,
            "Changing from 100% to 105% enlargement cannot move word's centre");
    }
}
