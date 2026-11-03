#!/usr/bin/env python3
"""Direct TV D-pad Down to playback when top toolbar has auto-hidden.

Applies after patch_tv_sync_v25.py against Melisma v2.11.0.
Only intercepts Down when the toolbar itself or the blank TV root had focus;
never hijacks Down while seek, transport, settings, or quick sync has focus.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
player = root / "app/src/main/java/com/melisma/app/ui/PlayerScreen.kt"
s = player.read_text(encoding="utf-8")
old = """                } else if (event.key == Key.DirectionDown && toolbarVisible &&
                    toolbarFocused && !showSettings && !showWelcome &&
                    snapshot.transport.playPause && snapshot.hasTrack) {
                    // From any toolbar icon, Down jumps into the transport row.
                    runCatching { transportFocus.requestFocus() }.getOrDefault(false)
"""
new = """                } else if (event.key == Key.DirectionDown &&
                    // Down works from the toolbar OR the idle root after toolbar auto-hide.
                    // If focus is already on a playback widget, leave its own D-pad navigation alone.
                    (toolbarFocused || (!toolbarVisible && rootFocused)) &&
                    !showSettings && !showWelcome && !showSyncPanel &&
                    snapshot.transport.playPause && snapshot.hasTrack) {
                    // Do not reveal the top toolbar. Focus Play/Pause directly.
                    runCatching { transportFocus.requestFocus() }.getOrDefault(false)
"""
n = s.count(old)
if n != 1:
    raise RuntimeError(f"Hidden-menu Down routing: expected exactly one anchor, found {n}")
player.write_text(s.replace(old, new, 1), encoding="utf-8")
print("TV D-pad Down now directly focuses playback with toolbar shown or hidden.")
