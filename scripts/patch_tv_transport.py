#!/usr/bin/env python3
"""Patch TV remote focus movement into the Cinema and Lyrics playback controls.

Runs after patch_tv_ui.py against pinned Melisma 2.11.0. Fails on unexpected
source changes instead of producing an APK with silently broken controls.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
player_path = ui / "PlayerScreen.kt"
panel_path = ui / "components/MediaPanel.kt"
nowbar_path = ui / "components/NowBar.kt"
helper_path = ui / "components/TvTransportFocus.kt"


def replace_once(source: str, old: str, new: str, name: str) -> str:
    n = source.count(old)
    if n != 1:
        raise RuntimeError(f"{name}: wanted exactly 1 anchor, found {n}")
    return source.replace(old, new, 1)


player = player_path.read_text(encoding="utf-8")
player = replace_once(player,
    """    val toolbarFocus = remember { FocusRequester() }
    val rootFocus = remember { FocusRequester() }
    var toolbarVisible by remember { mutableStateOf(true) }""",
    """    val toolbarFocus = remember { FocusRequester() }
    val transportFocus = remember { FocusRequester() }
    val rootFocus = remember { FocusRequester() }
    var rootFocused by remember { mutableStateOf(false) }
    var toolbarFocused by remember { mutableStateOf(false) }
    var toolbarVisible by remember { mutableStateOf(true) }""",
    "TV focus requesters")

player = replace_once(player,
    """            delay(5_000)
            rootFocus.requestFocus()
            toolbarVisible = false""",
    """            delay(5_000)
            // Only transfer focus when it is still in the disappearing toolbar.
            // Never steal focus from play/pause, the seek slider, or Open Player.
            if (toolbarFocused || rootFocused) {
                if (snapshot.track != null && snapshot.transport.playPause &&
                    (document != null || settings.viewMode == ViewMode.LYRICS)) {
                    runCatching { transportFocus.requestFocus() }
                        .onFailure { rootFocus.requestFocus() }
                } else {
                    rootFocus.requestFocus()
                }
            }
            toolbarVisible = false""",
    "TV auto-hide focus not stolen from player")

# document is defined later in PlayerScreen.kt; do not reference it in this early LaunchedEffect.
player = replace_once(player,
    "(document != null || settings.viewMode == ViewMode.LYRICS)",
    "settings.viewMode == ViewMode.LYRICS || snapshot.hasTrack",
    "resolve effect scope variables")

player = replace_once(player,
    """                } else if (event.key == Key.DirectionUp && !toolbarVisible &&
                    !showSettings && !showWelcome &&
                    settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {""",
    """                } else if (event.key == Key.DirectionDown && toolbarVisible &&
                    toolbarFocused && !showSettings && !showWelcome &&
                    snapshot.transport.playPause && snapshot.hasTrack) {
                    // From any toolbar icon, Down jumps into the transport row.
                    runCatching { transportFocus.requestFocus() }.getOrDefault(false)
                } else if (event.key == Key.Back && !toolbarVisible &&
                    !showSettings && !showWelcome) {
                    // Back is also a reliable way to reveal the toolbar from any TV control.
                    toolbarVisible = true
                    toolbarActivity++
                    scope.launch {
                        delay(80)
                        runCatching { toolbarFocus.requestFocus() }
                    }
                    true
                } else if (event.key == Key.DirectionUp && rootFocused && !toolbarVisible &&
                    !showSettings && !showWelcome &&
                    settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {""",
    "TV Up/Down/Back controls")

player = replace_once(player,
    """            }
            .focusable()
    } else Modifier
    Box(modifier.fillMaxSize().then(tvKeys)) {""",
    """            }
            .onFocusChanged { rootFocused = it.isFocused }
            .focusable()
    } else Modifier
    Box(modifier.fillMaxSize().then(tvKeys)) {""",
    "root focus tracking")

player = replace_once(player,
    """                        tvFocusRequester = if (tvMode) toolbarFocus else null,
                        romanizationAvailable = demoMode ||""",
    """                        tvFocusRequester = if (tvMode) toolbarFocus else null,
                        onTvToolbarFocusChanged = { toolbarFocused = it },
                        romanizationAvailable = demoMode ||""",
    "toolbar focus change callback")

player = replace_once(player,
    """                        snapshot = snapshot,
                        artwork = artwork,
                        colors = colors,
                        permissionGranted = permissionGranted,""",
    """                        snapshot = snapshot,
                        artwork = artwork,
                        colors = colors,
                        tvTransportFocusRequester = if (tvMode) transportFocus else null,
                        permissionGranted = permissionGranted,""",
    "MainContent focus argument")

player = replace_once(player,
    """                                artwork = artwork,
                                accent = colors.accent,
                                transport = snapshot.transport,""",
    """                                artwork = artwork,
                                accent = colors.accent,
                                tvTransportFocusRequester = if (tvMode) transportFocus else null,
                                transport = snapshot.transport,""",
    "NowBar focus argument")

player = replace_once(player,
    """    snapshot: com.melisma.app.media.PlayerSnapshot,
    artwork: android.graphics.Bitmap?,
    colors: ArtworkColors,
    permissionGranted: Boolean,""",
    """    snapshot: com.melisma.app.media.PlayerSnapshot,
    artwork: android.graphics.Bitmap?,
    colors: ArtworkColors,
    tvTransportFocusRequester: FocusRequester?,
    permissionGranted: Boolean,""",
    "MainContent parameter")

player = replace_once(player,
    """                            playback = snapshot.playback,
                            artwork = artwork,
                            accent = colors.accent,
                            transport = snapshot.transport,""",
    """                            playback = snapshot.playback,
                            artwork = artwork,
                            accent = colors.accent,
                            tvTransportFocusRequester = tvTransportFocusRequester,
                            transport = snapshot.transport,""",
    "MediaPanel focus argument")

player = replace_once(player,
    """    accent: Color,
    tvFocusRequester: FocusRequester?,
    romanizationAvailable: Boolean,""",
    """    accent: Color,
    tvFocusRequester: FocusRequester?,
    onTvToolbarFocusChanged: (Boolean) -> Unit,
    romanizationAvailable: Boolean,""",
    "ViewControls toolbar focused callback parameter")

player = replace_once(player,
    """    BoxWithConstraints(Modifier.fillMaxWidth()) {
        val sideBySide = maxWidth >= 560.dp""",
    """    BoxWithConstraints(
        Modifier.fillMaxWidth().onFocusChanged { onTvToolbarFocusChanged(it.hasFocus) }
    ) {
        val sideBySide = maxWidth >= 560.dp""",
    "ViewControls focus tracking")

# Avoid keeping a hidden active focus target. Moving from toolbar to player should always
# work, and the timeout no longer force-focuses the root when the user is using the player.
player_path.write_text(player, encoding="utf-8")


panel = panel_path.read_text(encoding="utf-8")
panel = replace_once(panel,
    "import androidx.compose.ui.draw.clipToBounds\n",
    """import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
""",
    "MediaPanel imports")
panel = replace_once(panel,
    """    hideCover: Boolean = false,
) {""",
    """    hideCover: Boolean = false,
    tvTransportFocusRequester: FocusRequester? = null,
) {""",
    "MediaPanel requester parameter")
panel = replace_once(panel,
    """                        onClick = onTogglePlay,
                        enabled = transport.playPause,
                        modifier = Modifier.size(54.dp),""",
    """                        onClick = onTogglePlay,
                        enabled = transport.playPause,
                        modifier = Modifier.size(54.dp)
                            .then(
                                if (tvTransportFocusRequester != null)
                                    Modifier.focusRequester(tvTransportFocusRequester)
                                else Modifier
                            )
                            .tvTransportFocus(),""",
    "MediaPanel play/pause focus")
panel = replace_once(panel,
    "IconButton(onClick = onPrevious, enabled = transport.skipPrevious) {",
    "IconButton(onClick = onPrevious, enabled = transport.skipPrevious, modifier = Modifier.tvTransportFocus()) {",
    "MediaPanel previous focus")
panel = replace_once(panel,
    "IconButton(onClick = onNext, enabled = transport.skipNext) {",
    "IconButton(onClick = onNext, enabled = transport.skipNext, modifier = Modifier.tvTransportFocus()) {",
    "MediaPanel next focus")
panel = replace_once(panel,
    """                        .clip(RoundedCornerShape(12.dp))
                        .clickable(onClick = onOpenSource)""",
    """                        .tvTransportFocus(RoundedCornerShape(12.dp))
                        .clip(RoundedCornerShape(12.dp))
                        .clickable(onClick = onOpenSource)""",
    "Open Player TV focus")
panel = replace_once(panel,
    """        modifier = Modifier
            .fillMaxWidth()
            .height(24.dp),
    )
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {""",
    """        modifier = Modifier
            .fillMaxWidth()
            .height(24.dp)
            .tvTransportFocus(RoundedCornerShape(8.dp)),
    )
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {""",
    "MediaPanel seek slider focus")
panel_path.write_text(panel, encoding="utf-8")


nowbar = nowbar_path.read_text(encoding="utf-8")
nowbar = replace_once(nowbar,
    "import androidx.compose.ui.draw.clip\n",
    """import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
""", "NowBar imports")
nowbar = replace_once(nowbar,
    """    modifier: Modifier = Modifier,
) {""",
    """    modifier: Modifier = Modifier,
    tvTransportFocusRequester: FocusRequester? = null,
) {""",
    "NowBar requester parameter")
nowbar = replace_once(nowbar,
    """                IconButton(onClick = onTogglePlay, enabled = transport.playPause) {""",
    """                IconButton(
                    onClick = onTogglePlay,
                    enabled = transport.playPause,
                    modifier = Modifier
                        .then(
                            if (tvTransportFocusRequester != null)
                                Modifier.focusRequester(tvTransportFocusRequester)
                            else Modifier
                        )
                        .tvTransportFocus(),
                ) {""", "NowBar play focus")
nowbar = replace_once(nowbar,
    "IconButton(onClick = onPrevious, enabled = transport.skipPrevious) {",
    "IconButton(onClick = onPrevious, enabled = transport.skipPrevious, modifier = Modifier.tvTransportFocus()) {",
    "NowBar previous focus")
nowbar = replace_once(nowbar,
    "IconButton(onClick = onNext, enabled = transport.skipNext) {",
    "IconButton(onClick = onNext, enabled = transport.skipNext, modifier = Modifier.tvTransportFocus()) {",
    "NowBar next focus")
nowbar = replace_once(nowbar,
    """            modifier = Modifier
                .fillMaxWidth()
                .height(24.dp),
        )
    """,
    """            modifier = Modifier
                .fillMaxWidth()
                .height(24.dp)
                .tvTransportFocus(RoundedCornerShape(8.dp)),
        )
    """, "NowBar seek slider focus")
nowbar = replace_once(nowbar,
    """                    .background(Color.White.copy(alpha = 0.08f))
                    .pointerInput(Unit) { detectTapGestures { onOpenSource() } },""",
    """                    .background(Color.White.copy(alpha = 0.08f))
                    .tvTransportFocus(RoundedCornerShape(10.dp))
                    .clickable(onClick = onOpenSource),""",
    "NowBar open source focus")
nowbar = replace_once(nowbar,
    "import androidx.compose.foundation.background\n",
    "import androidx.compose.foundation.background\nimport androidx.compose.foundation.clickable\n",
    "NowBar clickable import")
nowbar_path.write_text(nowbar, encoding="utf-8")

if helper_path.exists():
    raise RuntimeError("Focus helper file already exists in this upstream version")
helper_path.write_text("""package com.melisma.app.ui.components

import android.content.pm.PackageManager
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp

/** TV remote focus is cyan and clearly separate from active/pressed state. */
@Composable
internal fun Modifier.tvTransportFocus(shape: Shape = CircleShape): Modifier {
    val context = LocalContext.current
    val tv = context.packageManager.hasSystemFeature(PackageManager.FEATURE_LEANBACK)
    if (!tv) return this
    var focused by remember { mutableStateOf(false) }
    return this
        .onFocusChanged { focused = it.hasFocus }
        .border(
            width = if (focused) 3.dp else 0.dp,
            color = if (focused) Color(0xFF00D5FF) else Color.Transparent,
            shape = shape,
        )
        .background(
            color = if (focused) Color(0x3300D5FF) else Color.Transparent,
            shape = shape,
        )
}
""", encoding="utf-8")
print("Applied TV playback/transport focus fixes for Cinema and Lyrics views.")
