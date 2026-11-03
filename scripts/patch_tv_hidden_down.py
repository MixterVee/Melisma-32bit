#!/usr/bin/env python3
"""Direct TV Down to playback with the toolbar auto-hidden, without hijacking player keys.

Applies after patch_tv_sync_v25.py against upstream Melisma 2.11.0.
Tracks which panel owns focus: remote Down from idle lyrics or hidden controls
jumps into playback; remote Down already inside playback remains normal.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
pp = ui / "PlayerScreen.kt"
np = ui / "components/NowBar.kt"
mp = ui / "components/MediaPanel.kt"


def once(source: str, old: str, new: str, purpose: str) -> str:
    n = source.count(old)
    if n != 1:
        raise RuntimeError(f"{purpose}: expected one anchor, found {n}")
    return source.replace(old, new, 1)


player = pp.read_text(encoding="utf-8")
player = once(player,
    """    var toolbarFocused by remember { mutableStateOf(false) }
    var toolbarVisible by remember { mutableStateOf(true) }""",
    """    var toolbarFocused by remember { mutableStateOf(false) }
    var tvPlaybackFocused by remember { mutableStateOf(false) }
    var toolbarVisible by remember { mutableStateOf(true) }""",
    "track focused playback panel")

player = once(player,
    """                } else if (event.key == Key.DirectionDown && toolbarVisible &&
                    toolbarFocused && !showSettings && !showWelcome &&
                    snapshot.transport.playPause && snapshot.hasTrack) {
                    // From any toolbar icon, Down jumps into the transport row.
                    runCatching { transportFocus.requestFocus() }.getOrDefault(false)
""",
    """                } else if (event.key == Key.DirectionDown &&
                    !showSettings && !showWelcome && !showSyncPanel && !selectionMode &&
                    snapshot.transport.playPause && snapshot.hasTrack &&
                    (toolbarFocused || (!toolbarVisible && !tvPlaybackFocused))) {
                    // From hidden toolbar OR idle lyrics, go straight to Play/Pause.
                    // Never hijack Down inside the player's own seek/transport controls.
                    runCatching { transportFocus.requestFocus() }.getOrDefault(false)
""",
    "top-toolbar/hidden-screen D-pad Down handler")

player = once(player,
    """                        tvTransportFocusRequester = if (tvMode) transportFocus else null,
                        onTvSeekUp = tvSeekUp,
                        permissionGranted = permissionGranted,""",
    """                        tvTransportFocusRequester = if (tvMode) transportFocus else null,
                        onTvPlaybackFocusChanged = { tvPlaybackFocused = it },
                        onTvSeekUp = tvSeekUp,
                        permissionGranted = permissionGranted,""",
    "MainContent playback focus callback")

player = once(player,
    """                                tvTransportFocusRequester = if (tvMode) transportFocus else null,
                                onTvSeekUp = tvSeekUp,
                                transport = snapshot.transport,""",
    """                                tvTransportFocusRequester = if (tvMode) transportFocus else null,
                                onTvPlaybackFocusChanged = { tvPlaybackFocused = it },
                                onTvSeekUp = tvSeekUp,
                                transport = snapshot.transport,""",
    "NowBar playback focus callback")

player = once(player,
    """    tvTransportFocusRequester: FocusRequester?,
    onTvSeekUp: (() -> Unit)?,
    permissionGranted: Boolean,""",
    """    tvTransportFocusRequester: FocusRequester?,
    onTvPlaybackFocusChanged: (Boolean) -> Unit,
    onTvSeekUp: (() -> Unit)?,
    permissionGranted: Boolean,""",
    "MainContent callback parameter")

player = once(player,
    """                            tvTransportFocusRequester = tvTransportFocusRequester,
                            onTvSeekUp = onTvSeekUp,
                            transport = snapshot.transport,""",
    """                            tvTransportFocusRequester = tvTransportFocusRequester,
                            onTvPlaybackFocusChanged = onTvPlaybackFocusChanged,
                            onTvSeekUp = onTvSeekUp,
                            transport = snapshot.transport,""",
    "MediaPanel callback parameter")
pp.write_text(player,encoding="utf-8")

now = np.read_text(encoding="utf-8")
now = once(now,
    """import androidx.compose.ui.focus.focusRequester
""",
    """import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
""",
    "NowBar focus state import")
now = once(now,
    """    tvTransportFocusRequester: FocusRequester? = null,
    onTvSeekUp: (() -> Unit)? = null,
) {""",
    """    tvTransportFocusRequester: FocusRequester? = null,
    onTvPlaybackFocusChanged: ((Boolean) -> Unit)? = null,
    onTvSeekUp: (() -> Unit)? = null,
) {""",
    "NowBar callback signature")
now = once(now,
    """        modifier = modifier
            .fillMaxWidth()
            .padding(horizontal = 18.dp, vertical = 12.dp),""",
    """        modifier = modifier
            .onFocusChanged { onTvPlaybackFocusChanged?.invoke(it.hasFocus) }
            .fillMaxWidth()
            .padding(horizontal = 18.dp, vertical = 12.dp),""",
    "NowBar focus group")
np.write_text(now,encoding="utf-8")

media = mp.read_text(encoding="utf-8")
media = once(media,
    """import androidx.compose.ui.focus.focusRequester
""",
    """import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
""",
    "MediaPanel focus import")
media = once(media,
    """    tvTransportFocusRequester: FocusRequester? = null,
    onTvSeekUp: (() -> Unit)? = null,
) {""",
    """    tvTransportFocusRequester: FocusRequester? = null,
    onTvPlaybackFocusChanged: ((Boolean) -> Unit)? = null,
    onTvSeekUp: (() -> Unit)? = null,
) {""",
    "MediaPanel callback signature")
media = once(media,
    """    BoxWithConstraints(
        modifier
            // The panel shares the screen with the lyrics; it must never bleed into them,""",
    """    BoxWithConstraints(
        modifier
            .onFocusChanged { onTvPlaybackFocusChanged?.invoke(it.hasFocus) }
            // The panel shares the screen with the lyrics; it must never bleed into them,""",
    "MediaPanel focus group")
mp.write_text(media,encoding="utf-8")
print("Hidden-toolbar Down routes to Play/Pause, while playback controls retain native D-pad movement.")
