#!/usr/bin/env python3
"""Melisma Universal v2.7.1: Android TV Timing/Players focus, deterministic Cover Art crossfade.

Apply after patch_tv_v27.py against Melisma v2.11.0.
- Timing: first focus on a TV-friendly Sync offset row; Left/Right = 50 ms.
- Players to follow: first dynamic player gets the section's FocusRequester.
- Cover Art/Artist Header: Compose Crossfade keyed by the displayed bitmap replaces
  the manual mutation of currentCover before the animation was reset. This avoids
  an unfaded first frame and handles repeated Amazon Music artwork updates.
Other background renderers remain completely unchanged.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
sheet_path = root / "app/src/main/java/com/melisma/app/ui/SettingsSheet.kt"
bg_path = root / "app/src/main/java/com/melisma/app/ui/background/DynamicBackground.kt"

def once(source, old, new, why):
    n = source.count(old)
    if n != 1:
        raise RuntimeError(f"{why}: expected exactly one anchor; found {n}")
    return source.replace(old, new, 1)

s = sheet_path.read_text(encoding="utf-8")
s = once(s,
    "import androidx.compose.foundation.focusGroup\n",
    "import androidx.compose.foundation.focusGroup\nimport androidx.compose.foundation.focusable\n",
    "TV focusable import")

s = once(s,
    """                } else {
                    for ((packageName, label) in players) {
                        ToggleRow(
                            title = label,
                            subtitle = packageName,""",
    """                } else {
                    players.forEachIndexed { index, (packageName, label) ->
                        ToggleRow(
                            title = label,
                            subtitle = packageName,
                            firstInTvSection = index == 0,""",
    "dynamic first player gains section Down focus")

# Anchor to the exact final patched ToggleRow.
s = once(s,
    """private fun ToggleRow(
    title: String,
    subtitle: String?,
    checked: Boolean,
    accent: Color,
    onCheckedChange: (Boolean) -> Unit,
) {
    Row(
        Modifier
            .fillMaxWidth()
            .then(firstTvSettingFocus("toggle", title))
            .tvFocusOutline()""",
    """private fun ToggleRow(
    title: String,
    subtitle: String?,
    checked: Boolean,
    accent: Color,
    onCheckedChange: (Boolean) -> Unit,
    firstInTvSection: Boolean = false,
) {
    val firstPlayer = if (firstInTvSection) LocalTvSectionEntry.current?.requester else null
    Row(
        Modifier
            .fillMaxWidth()
            .then(if (firstPlayer != null) Modifier.focusRequester(firstPlayer)
                else firstTvSettingFocus("toggle", title))
            .tvFocusOutline()""",
    "first player is directly focusable from expanded header")

# Timing's initial Material slider isn't a reliable first D-pad focus target on
# some Android TVs. Make the row itself focusable, and let its Left/Right update
# the setting explicitly. Other sliders retain their existing behaviour.
section_start = s.find("@Composable\nprivate fun SliderRow(")
section_end = s.find("\n/**", section_start + 12)
if section_start < 0 or section_end < 0:
    raise RuntimeError("Could not isolate SliderRow")
slider = s[section_start:section_end]
slider = once(slider,
    """    Column(Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {""",
    """    val tv = LocalContext.current.packageManager.hasSystemFeature(
        android.content.pm.PackageManager.FEATURE_LEANBACK
    )
    val timingTv = tv && label == "Sync offset"
    val focusManager = LocalFocusManager.current
    val timingFocus = if (timingTv) {
        Modifier
            .then(firstTvSettingFocus("slider", label))
            .onPreviewKeyEvent { event ->
                if (event.type != KeyEventType.KeyDown) {
                    false
                } else when (event.key) {
                    Key.DirectionLeft -> {
                        onChange((value - 50f).coerceIn(range.start, range.endInclusive))
                        true
                    }
                    Key.DirectionRight -> {
                        onChange((value + 50f).coerceIn(range.start, range.endInclusive))
                        true
                    }
                    Key.DirectionUp -> focusManager.moveFocus(FocusDirection.Up)
                    Key.DirectionDown -> focusManager.moveFocus(FocusDirection.Down)
                    else -> false
                }
            }
            .tvFocusOutline()
            .focusable()
    } else Modifier

    Column(Modifier.fillMaxWidth().padding(vertical = 6.dp).then(timingFocus)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {""",
    "D-pad focusable Sync offset row with 50ms arrow controls")
slider = once(slider,
    """            modifier = Modifier
                .then(firstTvSettingFocus("slider", label))
                .tvSettingsSliderKeys()
                .tvFocusOutline(),
            valueRange = range,""",
    """            modifier = Modifier
                .then(if (timingTv) Modifier.focusProperties { canFocus = false }
                    else firstTvSettingFocus("slider", label))
                .then(if (timingTv) Modifier else Modifier.tvSettingsSliderKeys().tvFocusOutline()),
            valueRange = range,""",
    "prevent TV Timing row and child slider from fighting over focus")
s = s[:section_start] + slider + s[section_end:]
sheet_path.write_text(s, encoding="utf-8")

bg = bg_path.read_text(encoding="utf-8")
bg = once(bg,
    "import androidx.compose.animation.animateColorAsState\n",
    "import androidx.compose.animation.animateColorAsState\nimport androidx.compose.animation.Crossfade\n",
    "Compose Crossfade import")
bg = once(bg,
    """        val source = if (resolved == BackgroundStyle.ARTIST_HEADER) artistImage else artwork
        StillCoverBackground(source, softColors, blurRadius, transitionDurationMs, staticModifier)
        return""",
    """        val source = if (resolved == BackgroundStyle.ARTIST_HEADER) artistImage else artwork
        // The old renderer assigned the next cover before resetting fadeAnim, so a
        // full-opacity incoming frame could leak through. Crossfade owns the transition
        // atomically, even when Amazon Music sends multiple artwork updates per song.
        Crossfade(
            targetState = source,
            animationSpec = tween(
                durationMillis = transitionDurationMs,
                easing = FastOutSlowInEasing,
            ),
            label = "Background artwork crossfade",
        ) { displayedArtwork ->
            StillCoverBackground(displayedArtwork, softColors, blurRadius, staticModifier)
        }
        return""",
    "Use Compose animation directly for Cover Art/Artist Header")

start = bg.find("@Composable\nprivate fun StillCoverBackground(")
end = bg.find("/** Downscale to the size a given blur radius implies.", start)
if start < 0 or end < start:
    raise RuntimeError("Could not isolate StillCoverBackground")
old_cover = bg[start:end]
if "LaunchedEffect(reduced)" not in old_cover or "transitionDurationMs" not in old_cover:
    raise RuntimeError("Old manual Cover Art fade not present: refusing to overwrite")
new_cover = """@Composable
private fun StillCoverBackground(
    artwork: Bitmap?,
    colors: ArtworkColors,
    blurRadius: Int,
    modifier: Modifier,
) {
    // Each Crossfade layer keeps its own bitmap and is drawn by the compositor
    // at a gradually changing alpha. No manual bitmap swap or fade state here.
    val reduced = remember(artwork, blurRadius) { artwork?.reduced(blurRadius) }
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
        if (reduced != null) {
            drawIntoCanvas { canvas ->
                val native = canvas.nativeCanvas
                val scale = 1.15f * maxOf(
                    native.width.toFloat() / reduced.width,
                    native.height.toFloat() / reduced.height,
                )
                matrix.reset()
                matrix.postScale(scale, scale)
                matrix.postTranslate(
                    native.width / 2f - reduced.width * scale / 2f,
                    native.height / 2f - reduced.height * scale / 2f,
                )
                native.drawBitmap(reduced, matrix, paint)
            }
        }
        drawFloorShade()
    }
}

"""
bg = bg[:start] + new_cover + bg[end:]
if "LaunchedEffect(reduced)" in bg:
    raise RuntimeError("Old manual Cover Art fade still present")
bg_path.write_text(bg, encoding="utf-8")
print("TV v2.7.1: Timing/Players Down focus restored; Cover Art/Artist Header use interruption-safe Compose artwork crossfade.")
