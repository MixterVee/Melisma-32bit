#!/usr/bin/env python3
"""Melisma Universal v2.8.8: show remote toolbar captions and open Background visibly.

Apply after patch_tv_v287.py on upstream v2.11.0.
- Expand the fixed TV toolbar slot from 56dp to 94dp so the existing 26dp
  focus description isn't covered by the following main-content layer.
- Ignore late blur events from a previous toolbar chip; clear help when focus
  leaves the toolbar as a whole.
- When expanding the Background section with a TV remote, focus the first
  Style choice after its opening animation. The existing ChoiceChip
  BringIntoViewRequester scrolls it into the visible Settings viewport.
Do not touch the working independent cover art/background transitions or
the experimental Amazon Music Auto-Next logic.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
player_file = ui / "PlayerScreen.kt"
settings_file = ui / "SettingsSheet.kt"

def once(s, old, new, why):
    n = s.count(old)
    if n != 1:
        raise RuntimeError(f"{why}: expected exactly one match, got {n}")
    return s.replace(old, new, 1)

player = player_file.read_text(encoding="utf-8")
player = once(player,
    ".then(if (tvMode) Modifier.height(56.dp) else Modifier)",
    ".then(if (tvMode) Modifier.height(94.dp) else Modifier)",
    "TV top chrome slot for caption")

player = once(player,
    """        LocalToolbarFocusHelp provides { label: String? ->
            if (focusedToolbarHelp != label) focusedToolbarHelp = label
        },""",
    """        LocalToolbarFocusHelp provides { label: String? ->
            // A previous button may send a blur *after* the next button gains
            // focus. Do not let a late null erase its new description.
            if (label != null && focusedToolbarHelp != label) focusedToolbarHelp = label
        },""",
    "ignore late toolbar chip blur")

player = once(player,
    """.onFocusChanged { onTvToolbarFocusChanged(it.hasFocus) }""",
    """.onFocusChanged {
            onTvToolbarFocusChanged(it.hasFocus)
            if (!it.hasFocus) focusedToolbarHelp = null
        }""",
    "clear caption only when toolbar loses focus")

player_file.write_text(player, encoding="utf-8")

sheet = settings_file.read_text(encoding="utf-8")
sheet = once(sheet,
    """    var headerFocused by remember { mutableStateOf(false) }""",
    """    var headerFocused by remember { mutableStateOf(false) }

    // Opening Background while scrolled near the end of View can leave Style
    // above the viewport. Move TV focus to the first Style chip after expansion;
    // its existing BringIntoViewRequester scrolls it into view.
    LaunchedEffect(open, title, tv) {
        if (tv && title == "Background" && open) {
            kotlinx.coroutines.delay(320)
            runCatching { firstBackgroundChoice.requestFocus() }
        }
    }""",
    "Background auto-focus first visible Style chip")

settings_file.write_text(sheet, encoding="utf-8")
print("Melisma v2.8.8: TV toolbar help has visible caption space; opening Background focuses and scrolls to Style.")
