#!/usr/bin/env python3
"""Melisma Universal v2.7: persistent 1.0–10.0 s Background Transition slider.

Apply after patch_tv_v264.py against Melisma v2.11.0. Uses 200-ms
increments, including the previous 3.4 s default. All artwork/background
style fades use the same setting; BLACK stays immediate by design.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
settings_store = root / "app/src/main/java/com/melisma/app/settings/SettingsStore.kt"
settings_sheet = root / "app/src/main/java/com/melisma/app/ui/SettingsSheet.kt"
player_file = root / "app/src/main/java/com/melisma/app/ui/PlayerScreen.kt"
background_file = root / "app/src/main/java/com/melisma/app/ui/background/DynamicBackground.kt"

def replace_once(source: str, before: str, after: str, why: str) -> str:
    count = source.count(before)
    if count != 1:
        raise RuntimeError(f"{why}: expected exactly one anchor, found {count}")
    return source.replace(before, after, 1)

store = settings_store.read_text(encoding="utf-8")
store = replace_once(store,
    """    val backgroundBlur: Int = 24,
    /** Canvas video over [backgroundStyle], which shows whenever there is no video. */""",
    """    val backgroundBlur: Int = 24,
    /** Track-to-track background fade, in milliseconds (1–10 s). */
    val backgroundTransitionMs: Int = 3_400,
    /** Canvas video over [backgroundStyle], which shows whenever there is no video. */""",
    "persisted transition setting property")
store = replace_once(store,
    """        backgroundBlur = prefs.getInt(KEY_BACKGROUND_BLUR, 24),
        canvasMode = prefs.enum(KEY_CANVAS, CanvasMode.OFF),""",
    """        backgroundBlur = prefs.getInt(KEY_BACKGROUND_BLUR, 24),
        backgroundTransitionMs = prefs.getInt(KEY_BACKGROUND_TRANSITION, 3_400).coerceIn(1_000, 10_000),
        canvasMode = prefs.enum(KEY_CANVAS, CanvasMode.OFF),""",
    "restore saved transition on startup")
store = replace_once(store,
    """    fun setBackgroundBlur(px: Int) = edit { putInt(KEY_BACKGROUND_BLUR, px.coerceIn(0, 67)) }

    fun setCanvasMode""",
    """    fun setBackgroundBlur(px: Int) = edit { putInt(KEY_BACKGROUND_BLUR, px.coerceIn(0, 67)) }

    /** Keep the TV slider's 200-ms steps and preserve the chosen value across restarts. */
    fun setBackgroundTransitionMs(ms: Int) = edit {
        putInt(KEY_BACKGROUND_TRANSITION, ((ms.coerceIn(1_000, 10_000) + 100) / 200) * 200)
    }

    fun setCanvasMode""",
    "SettingsStore transition setter")
store = replace_once(store,
    """        const val KEY_BACKGROUND_BLUR = "background_blur"
        const val KEY_CANVAS = "canvas_mode" """.rstrip(),
    """        const val KEY_BACKGROUND_BLUR = "background_blur"
        const val KEY_BACKGROUND_TRANSITION = "background_transition_ms"
        const val KEY_CANVAS = "canvas_mode" """.rstrip(),
    "new SharedPreferences transition key")
settings_store.write_text(store, encoding="utf-8")

sheet = settings_sheet.read_text(encoding="utf-8")
sheet = replace_once(sheet,
    """                    onSelect = { store.setBackgroundStyle(it) },
                )
                if (settings.backgroundStyle == BackgroundStyle.ARTIST_HEADER &&""",
    """                    onSelect = { store.setBackgroundStyle(it) },
                )

                SliderRow(
                    label = "Background transition",
                    value = settings.backgroundTransitionMs.toFloat(),
                    range = 1_000f..10_000f,
                    display = "${settings.backgroundTransitionMs / 1000f} s",
                    accent = accent,
                    steps = 44, // 45 segments, 200 ms each: 1.0, 1.2, ... 10.0 s
                    onChange = { store.setBackgroundTransitionMs(it.toInt()) },
                )
                Hint("Fade duration when the artwork or background colours change between songs.")

                if (settings.backgroundStyle == BackgroundStyle.ARTIST_HEADER &&""",
    "Background section duration slider")
settings_sheet.write_text(sheet, encoding="utf-8")

player = player_file.read_text(encoding="utf-8")
player = replace_once(player,
    """            blurRadius = settings.backgroundBlur,
            preferStill = preferStill,""",
    """            blurRadius = settings.backgroundBlur,
            transitionDurationMs = settings.backgroundTransitionMs,
            preferStill = preferStill,""",
    "pass saved transition duration to renderer")
player_file.write_text(player, encoding="utf-8")

bg = background_file.read_text(encoding="utf-8")
bg = replace_once(bg,
    """    blurRadius: Int = 24,
    /**""",
    """    blurRadius: Int = 24,
    /** Track-to-track background transition, 1–10 seconds. */
    transitionDurationMs: Int = 3_400,
    /**""",
    "DynamicBackground transition parameter")
bg = replace_once(bg,
    """    val blendedBase by animateColorAsState(
        targetValue = colors.base,
        animationSpec = tween(durationMillis = 3_400, easing = FastOutSlowInEasing),""",
    """    val blendedBase by animateColorAsState(
        targetValue = colors.base,
        animationSpec = tween(durationMillis = transitionDurationMs, easing = FastOutSlowInEasing),""",
    "base palette duration")
bg = replace_once(bg,
    """    val blendedDark by animateColorAsState(
        targetValue = colors.darkVibrant,
        animationSpec = tween(durationMillis = 3_400, easing = FastOutSlowInEasing),""",
    """    val blendedDark by animateColorAsState(
        targetValue = colors.darkVibrant,
        animationSpec = tween(durationMillis = transitionDurationMs, easing = FastOutSlowInEasing),""",
    "upper palette duration")
bg = replace_once(bg,
    """        StillCoverBackground(source, softColors, blurRadius, staticModifier)""",
    """        StillCoverBackground(source, softColors, blurRadius, transitionDurationMs, staticModifier)""",
    "still cover transition parameter")
bg = replace_once(bg,
    """            moving = playing && !preferStill,
            tempoBpm = tempoBpm,
            modifier = modifier,""",
    """            moving = playing && !preferStill,
            tempoBpm = tempoBpm,
            transitionDurationMs = transitionDurationMs,
            modifier = modifier,""",
    "Living renderer transition parameter")
bg = replace_once(bg,
    """        fadeAnim.animateTo(1f, tween(durationMillis = 3_400, easing = FastOutSlowInEasing))""",
    """        fadeAnim.animateTo(1f, tween(durationMillis = transitionDurationMs, easing = FastOutSlowInEasing))""",
    "Living Classic transition duration")
bg = replace_once(bg,
    """private fun StillCoverBackground(
    artwork: Bitmap?,
    colors: ArtworkColors,
    blurRadius: Int,
    modifier: Modifier,""",
    """private fun StillCoverBackground(
    artwork: Bitmap?,
    colors: ArtworkColors,
    blurRadius: Int,
    transitionDurationMs: Int,
    modifier: Modifier,""",
    "Cover Art renderer transition argument")
bg = replace_once(bg,
    """                tween(durationMillis = 3_400, easing = FastOutSlowInEasing),""",
    """                tween(durationMillis = transitionDurationMs, easing = FastOutSlowInEasing),""",
    "Cover Art renderer animation duration")
bg = replace_once(bg,
    """private fun KawarpBackground(
    artwork: Bitmap?,
    colors: ArtworkColors,
    moving: Boolean,
    tempoBpm: Float?,
    modifier: Modifier,""",
    """private fun KawarpBackground(
    artwork: Bitmap?,
    colors: ArtworkColors,
    moving: Boolean,
    tempoBpm: Float?,
    transitionDurationMs: Int,
    modifier: Modifier,""",
    "Living/Kawarp duration parameter")
bg = replace_once(bg,
    """    LaunchedEffect(frame, current, moving, speed) {""",
    """    LaunchedEffect(frame, current, moving, speed, transitionDurationMs) {""",
    "re-evaluate Living frames if duration changes")
bg = replace_once(bg,
    """                Kawarp.TRANSITION_MS
            val blending = outgoing != null && progress < 1f""",
    """                transitionDurationMs.toFloat().coerceAtLeast(1f)
            val blending = outgoing != null && progress < 1f""",
    "Living/Kawarp fade fraction from adjustable duration")
background_file.write_text(bg, encoding="utf-8")

print("Melisma v2.7: 1–10s persistent 200ms-step Background transition slider controls Color, Living, Classic, Cover Art and artist image transitions.")
