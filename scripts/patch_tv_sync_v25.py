#!/usr/bin/env python3
"""Patch TV v2.5 quick, per-song lyrics sync and TV settings sheet in Melisma 2.11.0.

Applies AFTER the v2.4 patch scripts; fails closed on upstream source changes.
Each track has a temporary offset: resets only for a DIFFERENT non-null track
cacheKey, not on pause, rewind, seek, replay or temporary no-track snapshots.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
pp = ui / "PlayerScreen.kt"
sp = ui / "SettingsSheet.kt"
np = ui / "components/TvSyncPanel.kt"

def once(s, old, new, why):
    n = s.count(old)
    if n != 1: raise RuntimeError(f"{why}: expected 1 match, found {n}")
    return s.replace(old, new, 1)

settings = sp.read_text(encoding="utf-8")
settings = once(settings, "import androidx.compose.material3.ModalBottomSheet\n",
    "import androidx.compose.material3.ModalBottomSheet\nimport androidx.compose.material3.BottomSheetDefaults\n",
    "settings handle import")
settings = once(settings,
    """    onShowWelcome: () -> Unit,
    onDismiss: () -> Unit,
) {""",
    """    onShowWelcome: () -> Unit,
    onDismiss: () -> Unit,
    onTvOffsetChange: ((Int) -> Unit)? = null,
) {""", "TV Settings per-song offset callback")
settings = once(settings,
    """    val context = LocalContext.current
    var showCredits by remember { mutableStateOf(false) }""",
    """    val context = LocalContext.current
    val tvSheet = remember(context) {
        context.packageManager.hasSystemFeature(android.content.pm.PackageManager.FEATURE_LEANBACK)
    }
    var showCredits by remember { mutableStateOf(false) }""", "TV sheet detection")
settings = once(settings,
    """        containerColor = Color(0xFF14141A),
        contentColor = Color.White,
    ) {""",
    """        containerColor = Color(0xFF14141A),
        contentColor = Color.White,
        // Android TV: prevent a focused/pressed drag handle from moving the modal
        // sheet off-screen. Phone touch and swipe behaviour remains unchanged.
        dragHandle = if (tvSheet) null else @Composable { BottomSheetDefaults.DragHandle() },
        sheetGesturesEnabled = !tvSheet,
    ) {""", "disable TV sheet dragging")
settings = once(settings,
    """    val firstBackgroundChoice = remember { FocusRequester() }
    val tv = LocalContext.current.packageManager.hasSystemFeature(""",
    """    val firstBackgroundChoice = remember { FocusRequester() }
    val tvHeaderFocus = remember { FocusRequester() }
    val tv = LocalContext.current.packageManager.hasSystemFeature(""",
    "initial Settings focus requester")
settings = once(settings,
    """    var headerFocused by remember { mutableStateOf(false) }
    Column(Modifier.fillMaxWidth().focusGroup()) {""",
    """    var headerFocused by remember { mutableStateOf(false) }
    LaunchedEffect(tv, title) {
        if (tv && title == "View") {
            delay(330)
            runCatching { tvHeaderFocus.requestFocus() }
        }
    }
    Column(Modifier.fillMaxWidth().focusGroup()) {""",
    "focus View header after sheet entry")
settings = once(settings,
    """        Row(
            Modifier
                .fillMaxWidth()
                .then(
                    if (tv && open)""",
    """        Row(
            Modifier
                .fillMaxWidth()
                .then(if (tv && title == "View") Modifier.focusRequester(tvHeaderFocus) else Modifier)
                .then(
                    if (tv && open)""",
    "attach TV View header focus target")
settings = once(settings,
    """                    label = "Sync offset",
                    value = settings.syncOffsetMs.toFloat(),""",
    """                    label = if (onTvOffsetChange != null) "Sync offset (this song)" else "Sync offset",
                    value = settings.syncOffsetMs.toFloat(),""",
    "TV timing label")
settings = once(settings,
    """                    onChange = { store.setSyncOffset((it / 10).toInt() * 10) },
                    typed = TypedEntry(settings.syncOffsetMs, "ms", SettingsStore.SYNC_OFFSET_RANGE) {
                        store.setSyncOffset(it)
                    },""",
    """                    onChange = {
                        val ms = (it / 10).toInt() * 10
                        if (onTvOffsetChange != null) onTvOffsetChange(ms)
                        else store.setSyncOffset(ms)
                    },
                    typed = TypedEntry(settings.syncOffsetMs, "ms", SettingsStore.SYNC_OFFSET_RANGE) {
                        if (onTvOffsetChange != null) onTvOffsetChange(it)
                        else store.setSyncOffset(it)
                    },""",
    "timing Settings callback for currently playing track")
sp.write_text(settings, encoding="utf-8")

player = pp.read_text(encoding="utf-8")
player = once(player, "import com.melisma.app.ui.components.VolumeRow\n",
    "import com.melisma.app.ui.components.VolumeRow\nimport com.melisma.app.ui.components.TvSyncPanel\n",
    "TvSyncPanel import")
player = once(player,
    """    var toolbarActivity by remember { mutableIntStateOf(0) }
    var showSettings by remember { mutableStateOf(false) }""",
    """    var toolbarActivity by remember { mutableIntStateOf(0) }
    var showSyncPanel by remember { mutableStateOf(false) }
    var tvSongOffsetMs by remember { mutableIntStateOf(0) }
    var lastNonNullTrackKey by remember { mutableStateOf<String?>(null) }
    val currentTrackKey = snapshot.track?.cacheKey
    LaunchedEffect(currentTrackKey, tvMode) {
        // A different song starts at zero; replay or seeking within the same one keeps it.
        // A temporary null media session is not interpreted as a new song.
        if (tvMode && currentTrackKey != null && lastNonNullTrackKey != currentTrackKey) {
            lastNonNullTrackKey = currentTrackKey
            tvSongOffsetMs = 0
            showSyncPanel = false
        }
    }
    val tvDisplaySettings =
        if (tvMode) settings.copy(syncOffsetMs = tvSongOffsetMs) else settings
    var showSettings by remember { mutableStateOf(false) }
    // Hold D-pad Up in the empty lyrics area for 700 ms to open quick sync.
    // A short tap keeps the existing reveal-toolbar action.
    var tvUpHeld by remember { mutableStateOf(false) }
    LaunchedEffect(tvUpHeld) {
        if (tvUpHeld) {
            delay(700)
            if (tvUpHeld && rootFocused && !showSettings && !showSyncPanel) {
                tvUpHeld = false
                showSyncPanel = true
                toolbarVisible = true
                toolbarActivity++
            }
        }
    }""",
    "track-specific offset and shortcut states")
player = once(player,
    """    LaunchedEffect(tvMode, popup, showSettings, showWelcome, settings.controlsPosition, toolbarActivity) {
        if (tvMode && !popup && !showSettings && !showWelcome &&""",
    """    LaunchedEffect(tvMode, popup, showSettings, showWelcome, showSyncPanel,
        settings.controlsPosition, toolbarActivity) {
        if (tvMode && !popup && !showSettings && !showWelcome && !showSyncPanel &&""",
    "disable toolbar autohide during quick timing")
player = once(player,
    """        if (popup) {
            showSettings = false
            showWelcome = false
            selectionMode = false""",
    """        if (popup) {
            showSettings = false
            showWelcome = false
            showSyncPanel = false
            selectionMode = false""",
    "close timing editor in popup")
player = once(player,
    """                if (event.type != KeyEventType.KeyDown) {
                    false
                } else if (event.key == Key.DirectionDown && toolbarVisible &&""",
    """                if (event.key == Key.DirectionUp &&
                    event.type == KeyEventType.KeyUp && tvUpHeld) {
                    // Short tap: show toolbar. The long-press coroutine is cancelled.
                    tvUpHeld = false
                    toolbarVisible = true
                    toolbarActivity++
                    scope.launch {
                        delay(70)
                        runCatching { toolbarFocus.requestFocus() }
                    }
                    true
                } else if (event.type != KeyEventType.KeyDown) {
                    false
                } else if (event.key == Key.DirectionUp && rootFocused && !toolbarVisible &&
                    !showSettings && !showWelcome && !showSyncPanel &&
                    settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {
                    tvUpHeld = true
                    true
                } else if (event.key == Key.DirectionDown && toolbarVisible &&""",
    "Up short/long press while root focused")
player = once(player,
    """        if (popup) {
            PopupContent(
                document = document,
                settings = settings,""",
    """        if (popup) {
            PopupContent(
                document = document,
                settings = tvDisplaySettings,""",
    "respect TV timing offset in popup")
player = once(player,
    """                        onTvToolbarFocusChanged = { toolbarFocused = it },
                        romanizationAvailable = demoMode ||""",
    """                        onTvToolbarFocusChanged = { toolbarFocused = it },
                        onOpenTvSync = if (tvMode) {
                            {
                                showSyncPanel = true
                                toolbarVisible = true
                                toolbarActivity++
                            }
                        } else null,
                        romanizationAvailable = demoMode ||""",
    "toolbar quick timing button callback")
player = once(player,
    """                Box(Modifier.weight(1f).fillMaxWidth()) {
                    MainContent(
                        container = container,
                        settings = settings,""",
    """                Box(
                    Modifier.weight(1f).fillMaxWidth().then(
                        if (tvMode && showSyncPanel && settings.viewMode == ViewMode.LYRICS)
                            Modifier.padding(start = 290.dp)
                        else Modifier
                    )
                ) {
                    MainContent(
                        container = container,
                        settings = tvDisplaySettings,""",
    "side lane for timing control leaves lyrics visible")
player = once(player,
    """        if (showWelcome) {
            WelcomeSheet(""",
    """        if (tvMode && !popup && showSyncPanel && !showSettings && !showWelcome) {
            TvSyncPanel(
                offsetMs = tvSongOffsetMs,
                onOffsetChange = { tvSongOffsetMs = it },
                onClose = {
                    showSyncPanel = false
                    toolbarVisible = true
                    toolbarActivity++
                    scope.launch {
                        delay(70)
                        runCatching { toolbarFocus.requestFocus() }
                    }
                },
                modifier = Modifier.align(
                    if (settings.viewMode == ViewMode.CINEMA &&
                        settings.mediaPanelSide == MediaPanelSide.END)
                        Alignment.CenterEnd
                    else Alignment.CenterStart
                ).padding(horizontal = 12.dp),
            )
        }

        if (showWelcome) {
            WelcomeSheet(""",
    "side mounted live timing panel")
player = once(player,
    """                settings = settings,
                accent = colors.accent,
                songWriters = (lyricsState as? LyricsState.Loaded)?.document?.songWriters""",
    """                settings = tvDisplaySettings,
                accent = colors.accent,
                songWriters = (lyricsState as? LyricsState.Loaded)?.document?.songWriters""",
    "Settings shows same song-specific offset")
player = once(player,
    """                onDismiss = { showSettings = false },
            )""",
    """                onDismiss = { showSettings = false },
                onTvOffsetChange = if (tvMode) { value: Int -> tvSongOffsetMs = value } else null,
            )""",
    "timing changes from full Settings stay song-specific")
player = once(player,
    """    onTvToolbarFocusChanged: (Boolean) -> Unit,
    romanizationAvailable: Boolean,""",
    """    onTvToolbarFocusChanged: (Boolean) -> Unit,
    onOpenTvSync: (() -> Unit)?,
    romanizationAvailable: Boolean,""",
    "quick timing callback in ViewControls")
player = once(player,
    """                ActionChip(AppIcons.Refresh, "Look up again", false, accent, onRetry)
                ActionChip(AppIcons.Tune, "Settings", false, accent, onSettings)""",
    """                ActionChip(AppIcons.Refresh, "Look up again", false, accent, onRetry)
                if (onOpenTvSync != null) {
                    LabelChip(
                        "±ms",
                        "Quick timing adjustment for this song",
                        false,
                        Color(0xFF00D5FF),
                        onOpenTvSync,
                    )
                }
                ActionChip(AppIcons.Tune, "Settings", false, accent, onSettings)""",
    "TV toolbar ±ms button")
pp.write_text(player, encoding="utf-8")
if np.exists(): raise RuntimeError("Unexpected upstream TvSyncPanel.kt")
np.write_text("""package com.melisma.app.ui.components

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlin.math.roundToInt

private val cyan = Color(0xFF00D5FF)

/**
 * Live TV timing controls. No media seek, audio or other app interaction.
 * A side panel rather than the fullscreen/modal Settings sheet.
 */
@Composable
fun TvSyncPanel(
    offsetMs: Int,
    onOffsetChange: (Int) -> Unit,
    onClose: () -> Unit,
    modifier: Modifier = Modifier,
) {
    BackHandler(onBack = onClose)
    val sliderFocus = remember { FocusRequester() }
    val nudgeFocus = remember { FocusRequester() }
    val doneFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) {
        delay(120)
        runCatching { sliderFocus.requestFocus() }
    }
    fun apply(value: Int) { onOffsetChange(value.coerceIn(-3000, 3000)) }

    Column(
        modifier.width(266.dp)
            .border(2.dp, cyan, RoundedCornerShape(18.dp))
            .background(Color(0xF71C2636), RoundedCornerShape(18.dp))
            .padding(13.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Lyric timing", color = Color.White, fontSize = 17.sp,
                modifier = Modifier.weight(1f))
            SyncButton("Done", Modifier.focusRequester(doneFocus), onClose)
        }
        val display = if (offsetMs > 0) "+$offsetMs" else "$offsetMs"
        Text("$display ms", color = cyan, fontSize = 26.sp)
        Text("Positive = earlier · Negative = later", color = Color.White, fontSize = 11.sp)
        Slider(
            value = offsetMs.toFloat(),
            onValueChange = { apply((it / 50f).roundToInt() * 50) },
            valueRange = -3000f..3000f,
            steps = 119,
            colors = SliderDefaults.colors(
                thumbColor = cyan,
                activeTrackColor = cyan,
                inactiveTrackColor = Color.White.copy(alpha = 0.28f),
            ),
            modifier = Modifier.fillMaxWidth()
                .focusRequester(sliderFocus)
                .onPreviewKeyEvent { event ->
                    if (event.type != KeyEventType.KeyDown) false
                    else when (event.key) {
                        Key.DirectionUp -> runCatching { doneFocus.requestFocus() }.getOrDefault(false)
                        Key.DirectionDown -> runCatching { nudgeFocus.requestFocus() }.getOrDefault(false)
                        Key.Back -> { onClose(); true }
                        else -> false
                    }
                }
                .tvTransportFocus(RoundedCornerShape(8.dp)),
        )
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            SyncButton("−50", Modifier.weight(1f).focusRequester(nudgeFocus)) {
                apply(offsetMs - 50)
            }
            SyncButton("+50", Modifier.weight(1f)) {
                apply(offsetMs + 50)
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            SyncButton("Reset", Modifier.weight(1f)) { apply(0) }
            Text("This song only", color = Color.White.copy(alpha = 0.65f),
                fontSize = 11.sp, modifier = Modifier.align(Alignment.CenterVertically))
        }
    }
}

@Composable
private fun SyncButton(text: String, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Row(
        modifier
            .tvTransportFocus(RoundedCornerShape(9.dp))
            .background(Color.White.copy(alpha = 0.12f), RoundedCornerShape(9.dp))
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 10.dp),
        horizontalArrangement = Arrangement.Center,
    ) {
        Text(text, color = Color.White, fontSize = 13.sp)
    }
}
""",encoding="utf-8")
print("Applied TV v2.5 settings handle fix and per-track live timing panel")
