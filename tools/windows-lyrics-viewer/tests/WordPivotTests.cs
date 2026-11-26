using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

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

        // Verify the ACTUAL GDI+ matrix used by production, not just scalar
        // bounding-box arithmetic. A wrong Prepend/Append order caused by
        // transform composition would shift letters during the animation.
        foreach (float scale in new[] { 0.95f, 1f, 1.0505f, 1.12f })
        {
            using var matrix = ActiveWordGeometry.CenteredTransform(pivot, scale);
            PointF[] transformed =
            [
                pivot,
                new PointF(pivot.X - 50f, pivot.Y),
                new PointF(pivot.X + 50f, pivot.Y),
                new PointF(pivot.X, pivot.Y - 20f)
            ];
            matrix.TransformPoints(transformed);
            check(Math.Abs(transformed[0].X - pivot.X) < 0.01f &&
                  Math.Abs(transformed[0].Y - pivot.Y) < 0.01f &&
                  Math.Abs((transformed[1].X + transformed[2].X) / 2f -
                      pivot.X) < 0.01f &&
                  Math.Abs(transformed[3].Y -
                      (pivot.Y - 20f * scale)) < 0.02f,
                $"GDI+ matrix scales uniformly about the original ink pivot at {scale:F4}x");
        }

        // Pixel-level WinForms regression. Draw the SAME complete centered
        // lyric row under a clipped word region at 100% and at 105.05%.
        // Its glyph centre must stay put even when anti-aliased pixels change.
        const string fullLine = "In the middle of the night";
        using var font = new Font("Segoe UI", 29f, FontStyle.Bold);
        using var bitmap = new Bitmap(860, 150);
        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        var rowRect = new RectangleF(10, 10, 840, 125);
        int begin = fullLine.IndexOf("middle", StringComparison.Ordinal);
        using var measured = (StringFormat)format.Clone();
        measured.SetMeasurableCharacterRanges(
            [new CharacterRange(begin, "middle".Length)]);
        var regions = g.MeasureCharacterRanges(fullLine, font, rowRect, measured);
        RectangleF measuredInk;
        try { measuredInk = regions[0].GetBounds(g); }
        finally { foreach (var region in regions) region.Dispose(); }
        var renderedPivot = ActiveWordGeometry.Pivot(measuredInk);
        check(measuredInk.Width > 25f && measuredInk.Height > 12f,
            "GDI+ finds the active glyphs inside the original complete lyric row");

        RectangleF PaintBounds(float scale)
        {
            g.ResetClip();
            g.ResetTransform();
            g.Clear(Color.Black);
            var snapshot = g.Save();
            try
            {
                using var transform = ActiveWordGeometry.CenteredTransform(
                    renderedPivot, scale);
                g.MultiplyTransform(transform, MatrixOrder.Append);
                g.SetClip(RectangleF.Inflate(measuredInk, 2f, 5f),
                    CombineMode.Intersect);
                g.DrawString(fullLine, font, Brushes.White, rowRect, format);
            }
            finally { g.Restore(snapshot); }
            int minX = bitmap.Width, maxX = -1;
            int minY = bitmap.Height, maxY = -1;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).R < 100) continue;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
            return maxX < minX ? RectangleF.Empty :
                new RectangleF(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        var drawn100 = PaintBounds(1f);
        var drawn105 = PaintBounds(1.0505f);
        check(!drawn100.IsEmpty && !drawn105.IsEmpty &&
            Math.Abs(ActiveWordGeometry.Pivot(drawn100).X -
                     ActiveWordGeometry.Pivot(drawn105).X) <= 1.5f &&
            Math.Abs(ActiveWordGeometry.Pivot(drawn100).Y -
                     ActiveWordGeometry.Pivot(drawn105).Y) <= 1.5f,
            "Rendered word ink enlarges around a fixed pixel centre, with no horizontal drag");

    }
}
