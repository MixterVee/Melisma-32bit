using System.Drawing;

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

    internal static RectangleF ScaledBounds(RectangleF inkBounds, float scale)
    {
        var center = Pivot(inkBounds);
        float width = inkBounds.Width * scale;
        float height = inkBounds.Height * scale;
        return new RectangleF(center.X - width / 2f, center.Y - height / 2f,
            width, height);
    }
}
