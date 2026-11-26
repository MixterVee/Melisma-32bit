using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MelismaLyricsViewer;

/// <summary>
/// Low-resolution, slowly evolving colour clouds for an idle-screen Living
/// takeover. Organic domain-warped noise changes the COLOURS; no artwork
/// image is dragged or zoomed across the screen.
/// </summary>
internal sealed class FluidLivingRenderer : IDisposable
{
    private const int Width = 192;
    private const int Height = 108;
    private readonly int[] _pixels = new int[Width * Height];
    private readonly Bitmap _bitmap = new(Width, Height, PixelFormat.Format32bppArgb);
    private Image? _outgoingArtwork;
    private Image? _incomingArtwork;
    private Color[] _outgoingPalette = PaletteFor(null);
    private Color[] _incomingPalette = PaletteFor(null);
    private readonly Color[] _palette = PaletteFor(null);
    private long _lastFrameTick = -1;

    public void Draw(Graphics g, Rectangle bounds, Image? artwork, float opacity) =>
        DrawTransition(g, bounds, artwork, artwork, 1f, opacity);

    // v0.6.22 restored album COVER fades but fed this renderer only
    // _artwork.Current, swapping Living's dominant palette immediately.
    // Interpolate the actual displayed colours between previous/current
    // artwork for the user's full configured background-fade duration.
    public void DrawTransition(Graphics g, Rectangle bounds,
        Image? outgoingArtwork, Image? incomingArtwork,
        float progress, float opacity)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || opacity <= 0.001f) return;
        progress = Math.Clamp(progress, 0f, 1f);
        long now = Environment.TickCount64;
        if (!ReferenceEquals(outgoingArtwork, _outgoingArtwork) ||
            !ReferenceEquals(incomingArtwork, _incomingArtwork))
        {
            _outgoingArtwork = outgoingArtwork;
            _incomingArtwork = incomingArtwork;
            _outgoingPalette = PaletteFor(outgoingArtwork);
            _incomingPalette = PaletteFor(incomingArtwork);
            _lastFrameTick = -1;
        }

        // Low-cost palette interpolation occurs only when rendering the
        // existing 10fps fluid frame, never in an extra animation timer.
        // At progress=0 the previous palette is preserved exactly; at 1
        // the incoming album's palette is fully adopted.
        if (_lastFrameTick < 0 || now - _lastFrameTick >= 100)
        {
            for (int i = 0; i < _palette.Length; i++)
                _palette[i] = Mix(_outgoingPalette[i], _incomingPalette[i], progress);
            Render(now / 1000f);
            _lastFrameTick = now;
        }

        var saved = g.Save();
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        if (opacity >= 0.999f)
        {
            g.DrawImage(_bitmap, bounds);
        }
        else
        {
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix
            {
                Matrix33 = Math.Clamp(opacity, 0f, 1f)
            });
            g.DrawImage(_bitmap, bounds, 0, 0, Width, Height,
                GraphicsUnit.Pixel, attributes);
        }
        g.Restore(saved);
    }

    internal static Color[] PaletteFor(Image? art)
    {
        // Same artwork-to-palette extraction as before the transition fix,
        // but only once per incoming/outgoing bitmap change.
        Color[] defaults =
        [
            Color.FromArgb(25, 76, 120),
            Color.FromArgb(102, 47, 142),
            Color.FromArgb(35, 125, 116),
            Color.FromArgb(148, 55, 87)
        ];
        if (art == null) return defaults;
        try
        {
            using var sample = new Bitmap(4, 4);
            using (var canvas = Graphics.FromImage(sample))
            {
                canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
                canvas.DrawImage(art, new Rectangle(0, 0, 4, 4));
            }
            Color[] extracted =
            [
                sample.GetPixel(0, 0), sample.GetPixel(3, 1),
                sample.GetPixel(1, 3), sample.GetPixel(3, 3)
            ];
            Color[] result = new Color[4];
            for (int i = 0; i < 4; i++)
                result[i] = Mix(defaults[i], extracted[i], 0.53f);
            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or
                                        ExternalException or
                                        OutOfMemoryException)
        {
            return defaults;
        }
    }

    internal static Color PaletteBetween(Color oldColor, Color newColor,
        float progress) => Mix(oldColor, newColor, Math.Clamp(progress, 0f, 1f));

    private void Render(float time)
    {
        // Keep motion slow and irregular like drifting liquid clouds.
        float t = time * 0.10f;
        var p0 = _palette[0];
        var p1 = _palette[1];
        var p2 = _palette[2];
        var p3 = _palette[3];
        for (int y = 0; y < Height; y++)
        {
            float v = y / (float)(Height - 1);
            for (int x = 0; x < Width; x++)
            {
                float u = x / (float)(Width - 1);

                // Two drifting noise fields distort the sampling coordinates,
                // producing coloured wisps that fold into each other.
                float warpX = Fbm(u * 2.1f + t * 0.37f,
                                  v * 2.5f - t * 0.17f);
                float warpY = Fbm(u * 2.2f - t * 0.23f + 18f,
                                  v * 2.1f + t * 0.31f + 7f);
                float du = u * 2.6f + (warpX - 0.5f) * 2.4f;
                float dv = v * 2.5f + (warpY - 0.5f) * 2.4f;
                float cloudA = Fbm(du + t * 0.19f, dv - t * 0.21f);
                float cloudB = Fbm(du * 0.83f - t * 0.14f + 35f,
                                   dv * 0.92f + t * 0.16f + 21f);
                float blendX = Smooth((cloudA - 0.22f) / 0.54f);
                float blendY = Smooth((cloudB - 0.22f) / 0.54f);

                float r0 = Lerp(p0.R, p1.R, blendX);
                float g0 = Lerp(p0.G, p1.G, blendX);
                float b0 = Lerp(p0.B, p1.B, blendX);
                float r1 = Lerp(p2.R, p3.R, blendX);
                float g1 = Lerp(p2.G, p3.G, blendX);
                float b1 = Lerp(p2.B, p3.B, blendX);

                // A soft luminosity field adds dimension without showing any
                // recognisable album artwork or sharply delineated shapes.
                float softness = 0.83f +
                    0.35f * Fbm(du * 1.13f + t * 0.11f + 54f,
                                dv * 1.13f - t * 0.07f + 11f);
                float vignette = 1f - 0.25f *
                    ((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f));
                float brightness = softness * vignette;
                int r = Channel(Lerp(r0, r1, blendY) * brightness);
                int g = Channel(Lerp(g0, g1, blendY) * brightness);
                int b = Channel(Lerp(b0, b1, blendY) * brightness);
                _pixels[y * Width + x] = unchecked((int)0xFF000000) |
                    (r << 16) | (g << 8) | b;
            }
        }
        var area = new Rectangle(0, 0, Width, Height);
        BitmapData bits = _bitmap.LockBits(area,
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(_pixels, 0, bits.Scan0, _pixels.Length);
        }
        finally
        {
            _bitmap.UnlockBits(bits);
        }
    }

    private static int Channel(float value) =>
        Math.Clamp((int)MathF.Round(value), 0, 255);

    private static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
        Channel(Lerp(a.R, b.R, amount)),
        Channel(Lerp(a.G, b.G, amount)),
        Channel(Lerp(a.B, b.B, amount)));

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Smooth(float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        return v * v * (3f - 2f * v);
    }

    private static float Fbm(float x, float y) =>
        0.57f * Noise(x, y) +
        0.29f * Noise(x * 2.04f + 9.2f, y * 2.04f + 3.8f) +
        0.14f * Noise(x * 4.13f + 15.1f, y * 4.13f + 6.7f);

    private static float Noise(float x, float y)
    {
        int xi = (int)MathF.Floor(x);
        int yi = (int)MathF.Floor(y);
        float xf = Smooth(x - xi);
        float yf = Smooth(y - yi);
        float a = Lerp(Hash(xi, yi), Hash(xi + 1, yi), xf);
        float b = Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), xf);
        return Lerp(a, b, yf);
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 65535u) / 65535f;
        }
    }

    public void Dispose() => _bitmap.Dispose();
}
