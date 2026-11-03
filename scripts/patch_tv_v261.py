#!/usr/bin/env python3
"""TV v2.6.1: eliminate abrupt artwork jumps in static backgrounds and album-art gaps.

Apply after patch_tv_v26.py against upstream Melisma v2.11.0.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
player_path = ui / "PlayerScreen.kt"
bg_path = ui / "background/DynamicBackground.kt"

def once(s, old, new, label):
    n = s.count(old)
    if n != 1:
        raise RuntimeError(f"{label}: expected 1 anchor, found {n}")
    return s.replace(old, new, 1)

player = player_path.read_text(encoding="utf-8")
player = once(player,
    """    val fallbackArtwork = remember { tvFallbackArtwork() }
    val backgroundArtwork = artwork ?: fallbackArtwork
    val backgroundColors = if (artwork == null) {
        ArtworkColors(""",
    """    val fallbackArtwork = remember { tvFallbackArtwork() }
    // A media session can temporarily publish no cover while switching songs.
    // Keep the last real album art for a grace period instead of abruptly inserting
    // the synthetic fallback between Amazon Music/Deezer/Spotify tracks.
    var heldArtwork by remember { mutableStateOf<android.graphics.Bitmap?>(null) }
    LaunchedEffect(artwork) {
        if (artwork != null) {
            heldArtwork = artwork
        } else {
            delay(2_500)
            heldArtwork = null
        }
    }
    val backgroundArtwork = artwork ?: heldArtwork ?: fallbackArtwork
    val backgroundPalette = remember(backgroundArtwork) { ArtworkColors.from(backgroundArtwork) }
    val backgroundColors = if (backgroundArtwork === fallbackArtwork) {
        ArtworkColors(""",
    "retain the outgoing cover during media-session artwork gaps")
player = once(player,
    """            accent = Color(0xFF00D5FF),
        )
    } else colors
    val tvMode = remember(context) {""",
    """            accent = Color(0xFF00D5FF),
        )
    } else backgroundPalette
    val tvMode = remember(context) {""",
    "keep the held artwork's matching palette")
player_path.write_text(player, encoding="utf-8")

bg = bg_path.read_text(encoding="utf-8")
start = bg.find("@Composable\nprivate fun StillCoverBackground(")
end = bg.find("/** Downscale to the size a given blur radius implies.", start)
if start == -1 or end == -1 or end <= start:
    raise RuntimeError("Could not isolate StillCoverBackground.")
old_section = bg[start:end]
if old_section.count("val reduced = remember(artwork, blurRadius)") != 1:
    raise RuntimeError("StillCoverBackground upstream anchor mismatch.")
replacement = """@Composable
private fun StillCoverBackground(
    artwork: Bitmap?,
    colors: ArtworkColors,
    blurRadius: Int,
    modifier: Modifier,
) {
    val reduced = remember(artwork, blurRadius) { artwork?.reduced(blurRadius) }
    // Unlike the original still renderer, keep both the outgoing and the incoming
    // covers. v2.6 only animated the base wash; the actual large artwork jumped
    // immediately on every song, especially visible when still mode is selected.
    var currentCover by remember { mutableStateOf<Bitmap?>(null) }
    var outgoingCover by remember { mutableStateOf<Bitmap?>(null) }
    val fadeAnim = remember { Animatable(1f) }
    LaunchedEffect(reduced) {
        if (currentCover === reduced) return@LaunchedEffect
        outgoingCover = currentCover
        currentCover = reduced
        if (outgoingCover == null || reduced == null) {
            // First artwork on launch: show it promptly instead of fading from black.
            fadeAnim.snapTo(1f)
            outgoingCover = null
        } else {
            fadeAnim.snapTo(0f)
            fadeAnim.animateTo(
                1f,
                tween(durationMillis = 3_400, easing = FastOutSlowInEasing),
            )
            outgoingCover = null
        }
    }
    val fade = fadeAnim.value

    // Already promoted by the caller; modifier arrives with fillMaxSize applied.
    val paint = remember {
        Paint().apply {
            isFilterBitmap = true
            isAntiAlias = true
            colorFilter = ColorMatrixColorFilter(
                ColorMatrix().apply {
                    setSaturation(1.7f)
                    postConcat(
                        ColorMatrix(
                            floatArrayOf(
                                0.55f, 0f, 0f, 0f, 0f,
                                0f, 0.55f, 0f, 0f, 0f,
                                0f, 0f, 0.55f, 0f, 0f,
                                0f, 0f, 0f, 1f, 0f,
                            ),
                        ),
                    )
                },
            )
        }
    }
    val matrix = remember { Matrix() }

    Canvas(modifier) {
        drawRect(colors.base)
        val incoming = currentCover
        if (incoming != null) {
            drawIntoCanvas { canvas ->
                val native = canvas.nativeCanvas
                fun drawCover(bitmap: Bitmap, opacity: Int) {
                    val scale = 1.15f * maxOf(
                        native.width.toFloat() / bitmap.width,
                        native.height.toFloat() / bitmap.height,
                    )
                    matrix.reset()
                    matrix.postScale(scale, scale)
                    matrix.postTranslate(
                        native.width / 2f - bitmap.width * scale / 2f,
                        native.height / 2f - bitmap.height * scale / 2f,
                    )
                    paint.alpha = opacity.coerceIn(0, 255)
                    native.drawBitmap(bitmap, matrix, paint)
                }
                val previous = outgoingCover
                if (previous != null && fade < 1f) {
                    // Opaque outgoing layer underneath; fade the next song over it.
                    // This avoids the brief dark dip of two half-transparent layers.
                    drawCover(previous, 255)
                    drawCover(incoming, (fade * 255f).toInt())
                } else {
                    drawCover(incoming, 255)
                }
                paint.alpha = 255
            }
        }
        drawFloorShade()
    }
}

"""
bg = bg[:start] + replacement + bg[end:]
bg_path.write_text(bg, encoding="utf-8")
print("TV v2.6.1: static album/artist backgrounds now crossfade for 3.4 seconds; transient artwork gaps retain the previous cover.")
