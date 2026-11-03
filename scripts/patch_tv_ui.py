#!/usr/bin/env python3
"""Apply narrowly scoped TV usability changes to Melisma v2.11.0.

Fail closed when expected upstream source anchors change. This keeps the
unofficial Android TV patches reviewable and protects upstream upgrades.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
player_path = root / "app/src/main/java/com/melisma/app/ui/PlayerScreen.kt"
settings_path = root / "app/src/main/java/com/melisma/app/ui/SettingsSheet.kt"


def change(source: str, old: str, new: str, purpose: str) -> str:
    count = source.count(old)
    if count != 1:
        raise RuntimeError(f"{purpose}: expected 1 source match, found {count}")
    return source.replace(old, new, 1)


player = player_path.read_text(encoding="utf-8")
player = change(player,
    "import androidx.compose.foundation.background\n",
    """import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.focusable
""", "PlayerScreen imports foundation")
player = change(player,
    "import androidx.compose.ui.draw.clip\n",
    """import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.key.onPreviewKeyEvent
""", "PlayerScreen imports input focus")

player = change(player,
    """    val colors = remember(artwork) { ArtworkColors.from(artwork) }
    var showSettings by remember { mutableStateOf(false) }
""",
    """    val colors = remember(artwork) { ArtworkColors.from(artwork) }
    // Never fabricate album artwork in the song metadata or mini-player. Only the animated
    // background receives a synthetic colour field when a song has no actual cover.
    val fallbackArtwork = remember { tvFallbackArtwork() }
    val backgroundArtwork = artwork ?: fallbackArtwork
    val backgroundColors = if (artwork == null) {
        ArtworkColors(
            base = Color(0xFF142B50),
            vibrant = Color(0xFF438EE8),
            darkVibrant = Color(0xFF2576A8),
            muted = Color(0xFF384A81),
            accent = Color(0xFF00D5FF),
        )
    } else colors
    val tvMode = remember(context) {
        context.packageManager.hasSystemFeature(android.content.pm.PackageManager.FEATURE_LEANBACK)
    }
    val toolbarFocus = remember { FocusRequester() }
    val rootFocus = remember { FocusRequester() }
    var toolbarVisible by remember { mutableStateOf(true) }
    var toolbarActivity by remember { mutableIntStateOf(0) }
    var showSettings by remember { mutableStateOf(false) }
""", "fallback palette and TV mode")

player = change(player,
    """    var showWelcome by remember { mutableStateOf(false) }
    // First run only.""",
    """    var showWelcome by remember { mutableStateOf(false) }

    // Television-only chrome. Keep an always-sized toolbar slot so fading the buttons out
    // does not shift the lyrics. After idle, move focus to the screen before hiding controls.
    LaunchedEffect(tvMode, popup, showSettings, showWelcome, settings.controlsPosition, toolbarActivity) {
        if (tvMode && !popup && !showSettings && !showWelcome &&
            settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {
            delay(5_000)
            rootFocus.requestFocus()
            toolbarVisible = false
        }
    }
    LaunchedEffect(showSettings, showWelcome, popup) {
        if (tvMode && !popup && !showSettings && !showWelcome &&
            settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {
            toolbarVisible = true
            toolbarActivity++
            delay(80)
            toolbarFocus.requestFocus()
        }
    }

    // First run only.""", "5-second auto-hide and menu focus lifecycle")

player = change(player,
    """    Box(modifier.fillMaxSize()) {
        DynamicBackground(
            artwork = artwork,
            colors = colors,
""",
    """    val tvKeys = if (tvMode && !popup) {
        Modifier
            .focusRequester(rootFocus)
            .onPreviewKeyEvent { event ->
                if (event.type != KeyEventType.KeyDown) {
                    false
                } else if (event.key == Key.DirectionUp && !toolbarVisible &&
                    !showSettings && !showWelcome &&
                    settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {
                    toolbarVisible = true
                    toolbarActivity++
                    scope.launch {
                        delay(80)
                        toolbarFocus.requestFocus()
                    }
                    true
                } else {
                    // Any D-pad or OK press extends the five-second interval.
                    if (toolbarVisible) toolbarActivity++
                    false
                }
            }
            .focusable()
    } else Modifier
    Box(modifier.fillMaxSize().then(tvKeys)) {
        DynamicBackground(
            artwork = backgroundArtwork,
            colors = backgroundColors,
""", "TV root keys and synthetic background")

player = change(player,
    """                         accent = colors.accent,
                         romanizationAvailable = demoMode ||""",
    """                         accent = colors.accent,
                         tvFocusRequester = if (tvMode) toolbarFocus else null,
                         romanizationAvailable = demoMode ||""", "toolbar focus argument") if False else player

# Account for exact indentation of this call (24 spaces).
player = change(player,
    """                        accent = colors.accent,
                        romanizationAvailable = demoMode ||""",
    """                        accent = colors.accent,
                        tvFocusRequester = if (tvMode) toolbarFocus else null,
                        romanizationAvailable = demoMode ||""", "toolbar focus argument")

player = change(player,
    """                if (settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {
                    Box(Modifier.statusBarsPadding()) { controls() }
                } else {
                    Spacer(Modifier.statusBarsPadding())
                }""",
    """                if (settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {
                    // Fixed-size TV slot remains while controls fade: lyrics never jump.
                    Box(Modifier.statusBarsPadding().then(if (tvMode) Modifier.height(56.dp) else Modifier)) {
                        if (tvMode) {
                            TvFadingTopControls(visible = toolbarVisible || showSettings) { controls() }
                        } else controls()
                    }
                } else {
                    Spacer(Modifier.statusBarsPadding())
                }""", "constant-height top chrome")

player = change(player,
    """    accent: Color,
    romanizationAvailable: Boolean,
""",
    """    accent: Color,
    tvFocusRequester: FocusRequester?,
    romanizationAvailable: Boolean,
""", "toolbar function argument")

player = change(player,
    """                    "Cinema view",
                    settings.viewMode == ViewMode.CINEMA,
                    accent,
                    onToggleCinema,
                )""",
    """                    "Cinema view",
                    settings.viewMode == ViewMode.CINEMA,
                    accent,
                    onToggleCinema,
                    focusRequester = tvFocusRequester,
                )""", "Cinema focus target")

player = change(player,
    """private fun ActionChip(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    description: String,
    active: Boolean,
    accent: Color,
    onClick: () -> Unit,
) {
    Box(
        Modifier
            .size(38.dp)
            .clip(CircleShape)
            .background(if (active) accent.copy(alpha = 0.28f) else Color.White.copy(alpha = 0.08f))
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            imageVector = icon,
            contentDescription = description,
            tint = if (active) Color.White else Color.White.copy(alpha = 0.72f),
            modifier = Modifier.size(19.dp),
        )
    }
}""",
    """private fun ActionChip(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    description: String,
    active: Boolean,
    accent: Color,
    onClick: () -> Unit,
    focusRequester: FocusRequester? = null,
) {
    var focused by remember { mutableStateOf(false) }
    Box(
        Modifier
            .size(42.dp)
            .then(if (focusRequester != null) Modifier.focusRequester(focusRequester) else Modifier)
            .onFocusChanged { focused = it.isFocused }
            .border(
                if (focused) 3.dp else 0.dp,
                if (focused) Color(0xFF00D5FF) else Color.Transparent,
                CircleShape,
            )
            .clip(CircleShape)
            .background(
                if (focused) Color(0xFF00D5FF).copy(alpha = 0.25f)
                else if (active) accent.copy(alpha = 0.28f)
                else Color.White.copy(alpha = 0.08f)
            )
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            imageVector = icon,
            contentDescription = description,
            tint = if (focused) Color(0xFF00D5FF)
                else if (active) Color.White else Color.White.copy(alpha = 0.72f),
            modifier = Modifier.size(19.dp),
        )
    }
}""", "high contrast toolbar focus")

player = change(player,
    """private fun LabelChip(
    label: String,
    description: String,
    active: Boolean,
    accent: Color,
    onClick: () -> Unit,
) {
    Box(
        Modifier
            .size(38.dp)
            .clip(CircleShape)
            .background(if (active) accent.copy(alpha = 0.28f) else Color.White.copy(alpha = 0.08f))
            .semantics { contentDescription = description }
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = label,
            color = if (active) Color.White else Color.White.copy(alpha = 0.72f),
            fontSize = 13.sp,
            fontWeight = FontWeight.SemiBold,
        )
    }
}""",
    """private fun LabelChip(
    label: String,
    description: String,
    active: Boolean,
    accent: Color,
    onClick: () -> Unit,
) {
    var focused by remember { mutableStateOf(false) }
    Box(
        Modifier
            .size(42.dp)
            .onFocusChanged { focused = it.isFocused }
            .border(
                if (focused) 3.dp else 0.dp,
                if (focused) Color(0xFF00D5FF) else Color.Transparent,
                CircleShape,
            )
            .clip(CircleShape)
            .background(
                if (focused) Color(0xFF00D5FF).copy(alpha = 0.25f)
                else if (active) accent.copy(alpha = 0.28f)
                else Color.White.copy(alpha = 0.08f)
            )
            .semantics { contentDescription = description }
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = label,
            color = if (focused) Color(0xFF00D5FF)
                else if (active) Color.White else Color.White.copy(alpha = 0.72f),
            fontSize = 13.sp,
            fontWeight = FontWeight.SemiBold,
        )
    }
}""", "high contrast text chip focus")

player += """

/** Fade top TV controls outside of the enclosing ColumnScope, using the unscoped overload. */
@Composable
private fun TvFadingTopControls(visible: Boolean, content: @Composable () -> Unit) {
    AnimatedVisibility(
        visible = visible,
        enter = fadeIn(animationSpec = tween(220)),
        exit = fadeOut(animationSpec = tween(260)),
    ) { content() }
}

/**
 * Unbranded colour field used only by background renderers when media has no cover art.
 * Artwork in the player, media metadata, and other applications remains unchanged.
 */
private fun tvFallbackArtwork(): android.graphics.Bitmap {
    val width = 96
    val height = 96
    val pixels = IntArray(width * height)
    for (y in 0 until height) {
        for (x in 0 until width) {
            val u = x.toFloat() / (width - 1)
            val v = y.toFloat() / (height - 1)
            val wave = (kotlin.math.sin((u * 3f + v * 2f) * 3.14159f) + 1f) * 0.5f
            val red = (30f + 120f * u + 55f * wave).toInt().coerceIn(0, 255)
            val green = (60f + 85f * (1f - u) + 45f * v).toInt().coerceIn(0, 255)
            val blue = (130f + 110f * v + 15f * wave).toInt().coerceIn(0, 255)
            pixels[y * width + x] = android.graphics.Color.rgb(red, green, blue)
        }
    }
    return android.graphics.Bitmap.createBitmap(width, height, android.graphics.Bitmap.Config.ARGB_8888)
        .apply { setPixels(pixels, 0, width, 0, 0, width, height) }
}
"""
player = change(player, "import androidx.compose.animation.core.tween\n", "import androidx.compose.animation.core.tween\n", "ensure tween import")
player_path.write_text(player, encoding="utf-8")

settings = settings_path.read_text(encoding="utf-8")
settings = change(settings,
    "import androidx.compose.foundation.background\n",
    "import androidx.compose.foundation.background\nimport androidx.compose.foundation.border\n",
    "SettingsSheet border import")
settings = change(settings,
    "import androidx.compose.ui.platform.LocalFocusManager\n",
    "import androidx.compose.ui.platform.LocalFocusManager\nimport androidx.compose.ui.platform.LocalContext\n",
    "SettingsSheet context import")
settings = change(settings,
    """                .fillMaxWidth()
                .clickable(onClick = onToggle)
                .padding(vertical = 14.dp),""",
    """                .fillMaxWidth()
                .tvFocusOutline()
                .clickable(onClick = onToggle)
                .padding(vertical = 14.dp),""", "focusable setting section")
settings = change(settings,
    """            .fillMaxWidth()
            .clickable { onCheckedChange(!checked) }
            .padding(vertical = 8.dp),""",
    """            .fillMaxWidth()
            .tvFocusOutline()
            .clickable { onCheckedChange(!checked) }
            .padding(vertical = 8.dp),""", "focusable settings toggle")
settings = change(settings,
    """            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(vertical = 10.dp),""",
    """            .fillMaxWidth()
            .tvFocusOutline()
            .clickable(onClick = onClick)
            .padding(vertical = 10.dp),""", "focusable settings action")
settings += """

/** The active TV remote target stays obvious, independently of whether a setting is enabled. */
@Composable
private fun Modifier.tvFocusOutline(): Modifier {
    val context = LocalContext.current
    if (!context.packageManager.hasSystemFeature(android.content.pm.PackageManager.FEATURE_LEANBACK)) {
        return this
    }
    var focused by remember { mutableStateOf(false) }
    return this
        .onFocusChanged { focused = it.isFocused }
        .border(
            if (focused) 2.dp else 0.dp,
            if (focused) Color(0xFF00D5FF) else Color.Transparent,
            RoundedCornerShape(8.dp),
        )
}
"""
settings_path.write_text(settings, encoding="utf-8")
print("Applied Melisma TV v2 UI and fallback-artwork patches")
