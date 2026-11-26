namespace MelismaLyricsViewer;

/// <summary>
/// Keep lyric row text, the omitted active-word region and the spring-scaled
/// replacement word on the SAME horizontal baseline. In Lyrics mode each
/// wrapped row is centered; Cinema intentionally retains its left edge.
/// The word's centre must never change just because its font size changes.
/// </summary>
internal static class LyricRowAlignment
{
    internal static float RowLeft(float columnLeft, float columnWidth,
        float rowWidth, bool center)
    {
        return center ? columnLeft + (columnWidth - rowWidth) * 0.5f
            : columnLeft;
    }

    internal static float WordCenter(float rowLeft, float beforeWidth,
        float throughWidth) => rowLeft + (beforeWidth + throughWidth) * 0.5f;

    internal static float GrownWordLeft(float wordCenter,
        float grownWordWidth) => wordCenter - grownWordWidth * 0.5f;
}
