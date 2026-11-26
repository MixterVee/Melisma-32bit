using System.Drawing;

namespace MelismaLyricsViewer;

internal static class VisualFadeTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Native artwork is often published BEFORE Foobar's async media
        // metadata arrives. The latter must NOT reset the accepted sequence.
        check(!NativeArtworkTransitionGuard.NeedsReset(250, 250) &&
              NativeArtworkTransitionGuard.NeedsReset(249, 250),
            "Metadata for same track generation preserves the already accepted native cover");

        byte[] sameCover = [2, 7, 13, 25, 44, 6];
        byte[] sameCoverAgain = [2, 7, 13, 25, 44, 6];
        byte[] otherCover = [2, 7, 13, 25, 44, 7];
        check(NativeArtworkTransitionGuard.Fingerprint(sameCover) ==
                NativeArtworkTransitionGuard.Fingerprint(sameCoverAgain) &&
              NativeArtworkTransitionGuard.Fingerprint(sameCover) !=
                NativeArtworkTransitionGuard.Fingerprint(otherCover),
            "Duplicate native artwork callbacks cannot reset the old-to-new fade");

        using var older = new Bitmap(12, 12);
        using var newer = new Bitmap(12, 12);
        using (var oldG = Graphics.FromImage(older)) oldG.Clear(Color.Red);
        using (var newG = Graphics.FromImage(newer)) newG.Clear(Color.Blue);

        var from = FluidLivingRenderer.PaletteFor(older)[0];
        var to = FluidLivingRenderer.PaletteFor(newer)[0];
        var mid = FluidLivingRenderer.PaletteBetween(from, to, 0.5f);
        check(from.R > mid.R && mid.R > to.R &&
              from.B < mid.B && mid.B < to.B,
            "Living palette interpolates old album colours toward new album colours");
        check(FluidLivingRenderer.PaletteBetween(from, to, 0f) == from &&
              FluidLivingRenderer.PaletteBetween(from, to, 1f) == to,
            "User's background fade starts at 100% old and ends at 100% new artwork");

        // Actual bitmap-level integration: the palette transition must be
        // DRAWN, not merely calculated while the view silently selects new
        // artwork as v0.6.22 did. Use the same renderer as both displays.
        using var clouds = new FluidLivingRenderer();
        using var canvas = new Bitmap(192, 108);
        Color PixelAt(float fraction)
        {
            using (var g = Graphics.FromImage(canvas))
                clouds.DrawTransition(g, new Rectangle(0, 0, 192, 108),
                    older, newer, fraction, 1f);
            return canvas.GetPixel(98, 49);
        }
        var oldFrame = PixelAt(0f);
        System.Threading.Thread.Sleep(120); // original capped 10fps paint budget
        var middleFrame = PixelAt(0.5f);
        System.Threading.Thread.Sleep(120);
        var newFrame = PixelAt(1f);
        check(oldFrame.R > middleFrame.R && middleFrame.R > newFrame.R &&
              oldFrame.B < middleFrame.B && middleFrame.B < newFrame.B,
            "Actual Living bitmap fades between two song covers instead of snapping to incoming palette");
    }
}
