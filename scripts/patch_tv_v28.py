#!/usr/bin/env python3
"""Melisma Universal TV v2.8: optional clock, full controls auto-hide, and safe Exit.

Applies after patch_tv_v275.py against Melisma v2.11.0.
- Device-format clock in top-right, independent of visibility of player chrome.
- Optional TV clean-screen mode: top toolbar AND bottom lyrics controls OR
  Cinema playback/seek/volume/Open player are hidden after the existing 5s
  timeout. D-pad Up/Back/OK/Down from the clean screen reveals controls.
- Explicit Settings Exit button and confirmation: stop session observing,
  unbind notification listener and finish/remove this app task. External
  music app continues playing.
- Defaults are OFF to preserve v2.7.5 behavior and fades.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
store_file = root / "app/src/main/java/com/melisma/app/settings/SettingsStore.kt"
sheet_file = ui / "SettingsSheet.kt"
player_file = ui / "PlayerScreen.kt"
panel_file = ui / "components/MediaPanel.kt"

def once(s, before, after, label):
    count = s.count(before)
    if count != 1:
        raise RuntimeError(f"{label}: expected one exact anchor, found {count}")
    return s.replace(before, after, 1)

store = store_file.read_text(encoding="utf-8")
store = once(store,
    """    val backgroundStyle: BackgroundStyle = BackgroundStyle.ANIMATED,""",
    """    /** Show the device time at the top right, even with the playback controls hidden. */
    val showTvClock: Boolean = false,
    /** Let the full playback chrome disappear after five seconds on Android TV. */
    val tvAutoHideAllControls: Boolean = false,
    val backgroundStyle: BackgroundStyle = BackgroundStyle.ANIMATED,""",
    "Settings clock + hide state")
store = once(store,
    """        backgroundStyle = prefs.enum(KEY_BACKGROUND, BackgroundStyle.ANIMATED),""",
    """        showTvClock = prefs.getBoolean(KEY_TV_CLOCK, false),
        tvAutoHideAllControls = prefs.getBoolean(KEY_TV_HIDE_CHROME, false),
        backgroundStyle = prefs.enum(KEY_BACKGROUND, BackgroundStyle.ANIMATED),""",
    "read persisted clock and clean-screen flags")
store = once(store,
    """    fun setBackgroundStyle(style: BackgroundStyle) = edit { putString(KEY_BACKGROUND, style.name) }""",
    """    fun setShowTvClock(value: Boolean) = edit { putBoolean(KEY_TV_CLOCK, value) }

    fun setTvAutoHideAllControls(value: Boolean) = edit { putBoolean(KEY_TV_HIDE_CHROME, value) }

    fun setBackgroundStyle(style: BackgroundStyle) = edit { putString(KEY_BACKGROUND, style.name) }""",
    "clock / clean-screen setters")
store = once(store,
    """        const val KEY_BACKGROUND = "background_style" """.rstrip(),
    """        const val KEY_TV_CLOCK = "tv_clock"
        const val KEY_TV_HIDE_CHROME = "tv_hide_all_controls"
        const val KEY_BACKGROUND = "background_style" """.rstrip(),
    "persisted clock and hide keys")
store_file.write_text(store, encoding="utf-8")

sheet = sheet_file.read_text(encoding="utf-8")
sheet = once(sheet,
    """import androidx.compose.material3.ExperimentalMaterial3Api
""",
    """import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.TextButton
""",
    "Settings confirmation imports")
sheet = once(sheet,
    """    onShowWelcome: () -> Unit,
    onDismiss: () -> Unit,
) {""",
    """    onShowWelcome: () -> Unit,
    onExitApp: () -> Unit,
    onDismiss: () -> Unit,
) {""",
    "Settings Exit callback")
sheet = once(sheet,
    """    var showCredits by remember { mutableStateOf(false) }""",
    """    var showCredits by remember { mutableStateOf(false) }
    var askExitConfirmation by remember { mutableStateOf(false) }""",
    "Exit confirmation state")
sheet = once(sheet,
    """                Box(
                    Modifier
                        .size(34.dp)
                        .tvFocusOutline()
                        .clip(CircleShape)""",
    """                Text(
                    text = "Exit",
                    color = Color.White,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier
                        .padding(end = 14.dp)
                        .tvFocusOutline()
                        .clip(RoundedCornerShape(8.dp))
                        .background(Color.White.copy(alpha = 0.11f))
                        .clickable { askExitConfirmation = true }
                        .padding(horizontal = 12.dp, vertical = 8.dp),
                )
                Box(
                    Modifier
                        .size(34.dp)
                        .tvFocusOutline()
                        .clip(CircleShape)""",
    "Settings header Exit action")
sheet = once(sheet,
    """                ChipGroup(
                    label = "Controls",
                    options = ControlsPosition.entries.map {
                        it to if (it == ControlsPosition.TOP) "Top" else "Bottom"
                    },
                    selected = settings.controlsPosition,
                    accent = accent,
                    onSelect = { store.setControlsPosition(it) },
                )""",
    """                ChipGroup(
                    label = "Controls",
                    options = ControlsPosition.entries.map {
                        it to if (it == ControlsPosition.TOP) "Top" else "Bottom"
                    },
                    selected = settings.controlsPosition,
                    accent = accent,
                    onSelect = { store.setControlsPosition(it) },
                )

                ToggleRow(
                    title = "Clock in top-right corner",
                    subtitle = "Show device time above the lyrics (12/24-hour device format)",
                    checked = settings.showTvClock,
                    accent = accent,
                    onCheckedChange = { store.setShowTvClock(it) },
                )
                ToggleRow(
                    title = "Auto-hide all playback controls",
                    subtitle = "On TV, hide top and bottom controls after 5s; press Up, OK or Back to reveal",
                    checked = settings.tvAutoHideAllControls,
                    accent = accent,
                    onCheckedChange = { store.setTvAutoHideAllControls(it) },
                )""",
    "View section clock and auto-hide settings")
# Place confirmation after sheet, so it uses its own Android Dialog window.
# The first top-level SettingsSheet composable ends immediately before CreditsPanel
# or nearby helper declarations; insert the dialog just before ModalBottomSheet call,
# so it shares the same composable scope and state.
sheet = once(sheet,
    """    ModalBottomSheet(
        onDismissRequest = onDismiss,""",
    """    if (askExitConfirmation) {
        AlertDialog(
            onDismissRequest = { askExitConfirmation = false },
            title = { Text("Exit Melisma?") },
            text = { Text("Melisma will close, but music in the source app will keep playing.") },
            confirmButton = {
                TextButton(onClick = {
                    askExitConfirmation = false
                    onExitApp()
                }) { Text("Exit", color = Color.White) }
            },
            dismissButton = {
                TextButton(onClick = { askExitConfirmation = false }) {
                    Text("Cancel", color = Color.White)
                }
            },
            containerColor = Color(0xFF20212B),
        )
    }

    ModalBottomSheet(
        onDismissRequest = onDismiss,""",
    "Exit confirmation dialog")
sheet_file.write_text(sheet, encoding="utf-8")

player = player_file.read_text(encoding="utf-8")
# Avoid clock on popup and when not requested, no forced US time format.
player = once(player,
    """import android.media.AudioManager
""",
    """import android.media.AudioManager
import java.util.Date
""",
    "time helper import")
player = once(player,
    """    var showSettings by remember { mutableStateOf(false) }""",
    """    var showSettings by remember { mutableStateOf(false) }
    var displayedClockMillis by remember { mutableStateOf(System.currentTimeMillis()) }
    LaunchedEffect(settings.showTvClock, popup) {
        if (!settings.showTvClock || popup) return@LaunchedEffect
        while (true) {
            displayedClockMillis = System.currentTimeMillis()
            delay(10_000)
        }
    }""",
    "optional clock refresh loop")
player = once(player,
    """    LaunchedEffect(tvMode, popup, showSettings, showWelcome, showSyncPanel,
        settings.controlsPosition, toolbarActivity) {""",
    """    LaunchedEffect(tvMode, popup, showSettings, showWelcome, showSyncPanel,
        settings.controlsPosition, settings.tvAutoHideAllControls, toolbarActivity) {""",
    "clean-screen setting restarts the chrome timer")
player = once(player,
    """            settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {
            delay(5_000)""",
    """            (settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP ||
                settings.tvAutoHideAllControls)) {
            delay(5_000)""",
    "allow timer also for bottom toolbar in clean-screen mode")
player = once(player,
    """            // Only transfer focus when it is still in the disappearing toolbar.
            // Never steal focus from play/pause, the seek slider, or Open Player.
            if (toolbarFocused || rootFocused) {""",
    """            // When all controls hide, focus must go somewhere that stays visible.
            // Otherwise it remains on an invisible seek slider / Open player button.
            if (settings.tvAutoHideAllControls) {
                runCatching { rootFocus.requestFocus() }
            } else if (toolbarFocused || rootFocused) {""",
    "safe focus when full controls disappear")
player = once(player,
    """        if (tvMode && !popup && !showSettings && !showWelcome &&
            settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {
            toolbarVisible = true""",
    """        if (tvMode && !popup && !showSettings && !showWelcome &&
            (settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP ||
                settings.tvAutoHideAllControls)) {
            toolbarVisible = true""",
    "restore clean-screen toolbar after Settings")
# In the root TV key handler, a hidden seek control must not be addressed via
# DirectionDown. Down, OK/Center and Enter instead reveal the chrome.
player = once(player,
    """                } else if (event.type != KeyEventType.KeyDown) {
                    false
                } else if (event.key == Key.DirectionUp && rootFocused && !toolbarVisible &&""",
    """                } else if (event.type != KeyEventType.KeyDown) {
                    false
                } else if (settings.tvAutoHideAllControls && rootFocused &&
                    !toolbarVisible && !showSettings && !showWelcome && !showSyncPanel &&
                    (event.key == Key.DirectionDown || event.key == Key.DirectionCenter ||
                        event.key == Key.Enter)) {
                    toolbarVisible = true
                    toolbarActivity++
                    scope.launch {
                        delay(80)
                        runCatching { toolbarFocus.requestFocus() }
                    }
                    true
                } else if (event.key == Key.DirectionUp && rootFocused && !toolbarVisible &&""",
    "D-pad Down / OK reveal all controls")
player = once(player,
    """                    settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {
                    tvUpHeld = true""",
    """                    (settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP ||
                        settings.tvAutoHideAllControls)) {
                    tvUpHeld = true""",
    "D-pad Up also reveals bottom toolbar in clean-screen mode")
# The user doesn't lose access to the settings panel; top toolbar still has
# its 5s auto-hide, and the existing Back=show toolbar path is unchanged.
player = once(player,
    """    Box(modifier.fillMaxSize().then(tvKeys)) {
        DynamicBackground(""",
    """    val playbackChromeVisible = !tvMode || !settings.tvAutoHideAllControls ||
        toolbarVisible || showSettings || showSyncPanel || showWelcome || selectionMode

    Box(modifier.fillMaxSize().then(tvKeys)) {
        DynamicBackground(""",
    "single source of truth for all playback chrome visibility")
# Clock overlay rendered above player and below modal windows.
player = once(player,
    """        if (tvMode && !popup && showSyncPanel && !showSettings && !showWelcome) {""",
    """        if (settings.showTvClock && !popup) {
            Box(
                Modifier
                    .align(Alignment.TopEnd)
                    .statusBarsPadding()
                    .padding(top = 8.dp, end = 18.dp)
                    .clip(RoundedCornerShape(9.dp))
                    .background(Color.Black.copy(alpha = 0.50f))
                    .padding(horizontal = 11.dp, vertical = 6.dp),
            ) {
                Text(
                    text = android.text.format.DateFormat.getTimeFormat(context)
                        .format(Date(displayedClockMillis)),
                    color = Color.White,
                    fontSize = 17.sp,
                    fontWeight = FontWeight.SemiBold,
                )
            }
        }

        if (tvMode && !popup && showSyncPanel && !showSettings && !showWelcome) {""",
    "always-visible top-right device clock")
# Prevent clock and rightmost top toolbar button from overlapping.
player = once(player,
    """                    Box(Modifier.statusBarsPadding().then(if (tvMode) Modifier.height(56.dp) else Modifier)) {""",
    """                    Box(Modifier.statusBarsPadding()
                        .padding(end = if (settings.showTvClock) 105.dp else 0.dp)
                        .then(if (tvMode) Modifier.height(56.dp) else Modifier)) {""",
    "clock safety margin for top toolbar")
# Optional hiding of the bottom Lyrics controls while retaining selection bar.
player = once(player,
    """                } else if (settings.viewMode == ViewMode.LYRICS) {
                    // One navigation-bar inset for the pair of them.""",
    """                } else if (settings.viewMode == ViewMode.LYRICS && playbackChromeVisible) {
                    // One navigation-bar inset for the pair of them.""",
    "hide Lyrics NowBar/volume when idle")
# Pass state through MainContent to Cinema's MediaPanel.
player = once(player,
    """                        settings = tvDisplaySettings,
                        snapshot = snapshot,""",
    """                        settings = tvDisplaySettings,
                        showPlaybackControls = playbackChromeVisible,
                        snapshot = snapshot,""",
    "pass chrome state to MainContent")
player = once(player,
    """    settings: Settings,
    snapshot: com.melisma.app.media.PlayerSnapshot,""",
    """    settings: Settings,
    showPlaybackControls: Boolean,
    snapshot: com.melisma.app.media.PlayerSnapshot,""",
    "MainContent showPlaybackControls signature")
player = once(player,
    """                            sourceLabel = snapshot.sourceLabel,
                            tvTransportFocusRequester = tvTransportFocusRequester,""",
    """                            sourceLabel = snapshot.sourceLabel,
                            showPlaybackControls = showPlaybackControls,
                            tvTransportFocusRequester = tvTransportFocusRequester,""",
    "pass chrome state to MediaPanel")
# Hide toolbar when positioned at bottom, but only when the clean-screen setting
# is on; the existing default behaviour never changes.
player = once(player,
    """                    Box(Modifier.navigationBarsPadding()) { controls() }
                } else if (settings.viewMode == ViewMode.CINEMA && !selectionMode) {""",
    """                    Box(Modifier.navigationBarsPadding()) {
                        if (tvMode && settings.tvAutoHideAllControls) {
                            AnimatedVisibility(
                                visible = toolbarVisible || showSettings || showSyncPanel,
                                enter = fadeIn(tween(250)),
                                exit = fadeOut(tween(250)),
                            ) { controls() }
                        } else controls()
                    }
                } else if (settings.viewMode == ViewMode.CINEMA && !selectionMode) {""",
    "bottom-position toolbar can also auto-hide")
# Make exiting deliberate, with a dialog in Settings, and release Android's
# notification-listener/media controller before dismissing only this app task.
player = once(player,
    """                onDismiss = { showSettings = false },
                onTvOffsetChange = if (tvMode)""",
    """                onDismiss = { showSettings = false },
                onExitApp = {
                    container.media.stop()
                    com.melisma.app.media.MediaNotificationListener.standDown()
                    (context as? android.app.Activity)?.finishAndRemoveTask()
                },
                onTvOffsetChange = if (tvMode)""",
    "Settings confirmed exit tears down media watching and closes task")
player_file.write_text(player, encoding="utf-8")

panel = panel_file.read_text(encoding="utf-8")
panel = once(panel,
    """    sourceLabel: String?,
    transport: Transport,""",
    """    sourceLabel: String?,
    showPlaybackControls: Boolean = true,
    transport: Transport,""",
    "Cinema player chrome flag")
panel = once(panel,
    """            ScrubBar(
                fraction = fraction,""",
    """            if (showPlaybackControls) ScrubBar(
                fraction = fraction,""",
    "Cinema seek bar respects clean-screen")
panel = once(panel,
    """            if (transport.any && roomForTransport) {""",
    """            if (showPlaybackControls && transport.any && roomForTransport) {""",
    "Cinema buttons respect clean-screen")
panel = once(panel,
    """            if (showVolume) {""",
    """            if (showPlaybackControls && showVolume) {""",
    "Cinema volume respects clean-screen")
panel = once(panel,
    """            if (roomForOpen) {""",
    """            if (showPlaybackControls && roomForOpen) {""",
    "Cinema Open app respects clean-screen")
panel_file.write_text(panel, encoding="utf-8")

print("Melisma v2.8: persisted clock and full TV auto-hide, safe remote reveal, and confirmed Exit with listener teardown; v2.7.5 fades untouched.")
