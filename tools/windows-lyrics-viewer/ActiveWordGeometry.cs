using System.Drawing;
using System.Drawing.Drawing2D;

namespace MelismaLyricsViewer;

/// <summary>
/// A word is an ink region in the UNMODIFIED complete lyric row.
/// Scaling around its fixed centre prevents horizontal/vertical drift.
// No separate word font measurement or glyph re-layout is permitted.
/// </summary>
internal static class ActiveWordGeometry
{
    internal static PointF Pivot(RectangleF inkBounds) =>
        new(inkBounds.Left + inkBounds.Width / 2f,
            inkBounds.Top + inkBounds.Height / 2f);

    // GDI+ MatrixOrder.Append uses the x -> (x-pivot)*scale + pivot
    // transform. This helper is shared by production and Windows tests.
    internal static Matrix CenteredTransform(PointF pivot, float scale)
    {
        var matrix = new Matrix();
        matrix.Translate(-pivot.X, -pivot.Y, MatrixOrder.Append);
        matrix.Scale(scale, scale, MatrixOrder.Append);
        matrix.Translate(pivot.X, pivot.Y, MatrixOrder.Append);
        return matrix;
    }

    internal static RectangleF ScaledBounds(RectangleF inkBounds, float scale)
    {
        var center = Pivot(inkBounds);
        float width = inkBounds.Width * scale;
        float height = inkBounds.Height * scale;
        return new RectangleF(center.X - width / 2f, center.Y - height / 2f,
            width, height);
    }
}
