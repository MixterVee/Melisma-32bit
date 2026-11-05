#!/usr/bin/env python3
"""Melisma Universal v2.8.9 - conservative Android TV-only UI cleanup.

Apply after patch_tv_v288.py. Hide (never delete) phone/touch-specific UI on
Android TV while preserving all settings/data and non-TV layouts.
Do not touch the v2.8.7 independent cover/background fade, TV focus/scroll
fixes, providers, player, or Amazon auto-next.
"""
from pathlib import Path
import sys

root=Path(sys.argv[1]) if len(sys.argv)>1 else Path("melisma")
ui=root/"app/src/main/java/com/melisma/app/ui"
player_path=ui/"PlayerScreen.kt"
sheet_path=ui/"SettingsSheet.kt"

def once(s, before, after, label):
    n=s.count(before)
    if n != 1:
        raise RuntimeError(f"{label}: expected 1 match; got {n}")
    return s.replace(before,after,1)

player=player_path.read_text(encoding="utf-8")
view_start=player.index("private fun ViewControls(")
view_end=player.index("@Composable\nprivate fun ActionChip(",view_start)
view=player[view_start:view_end]
view=once(view,
    """                if (popupAvailable) {
                    ActionChip(AppIcons.PopupWindow, "Popup lyrics", false, accent, onEnterPopup)
                }""",
    """                // Floating-window lyrics are for phone/tablet use, not Android TV.
                if (popupAvailable && !tv) {
                    ActionChip(AppIcons.PopupWindow, "Popup lyrics", false, accent, onEnterPopup)
                }""",
    "hide TV popup toolbar button")
view=once(view,
    """                if (hasDocument) {
                    ActionChip(AppIcons.Copy, "Select lines to copy", false, accent, onStartSelection)
                }""",
    """                // TV remotes have no useful line-selection/clipboard gesture.
                if (hasDocument && !tv) {
                    ActionChip(AppIcons.Copy, "Select lines to copy", false, accent, onStartSelection)
                }""",
    "hide TV select-to-copy toolbar button")
player=player[:view_start]+view+player[view_end:]
player_path.write_text(player,encoding="utf-8")

s=sheet_path.read_text(encoding="utf-8")
s=once(s,
    """    var showCredits by remember { mutableStateOf(false) }""",
    """    val tvOnlyCleanup = LocalContext.current.packageManager.hasSystemFeature(
        android.content.pm.PackageManager.FEATURE_LEANBACK
    )
    var showCredits by remember { mutableStateOf(false) }""",
    "TV-only setting visibility")

# Keep the underlying values unchanged: only omit the control rows on TVs.
s=once(s,
    """                ToggleRow(
                    title = "Compact mode",
                    subtitle = "Smaller type and tighter spacing, for split screen",
                    checked = settings.compactMode,
                    accent = accent,
                    onCheckedChange = { store.setCompactMode(it) },
                )
                Help(
                    "Meant for split screen or a small window. The floating window switches to it on its own, so you do not need this for that.",
                )""",
    """                if (!tvOnlyCleanup) {
                    ToggleRow(
                        title = "Compact mode",
                        subtitle = "Smaller type and tighter spacing, for split screen",
                        checked = settings.compactMode,
                        accent = accent,
                        onCheckedChange = { store.setCompactMode(it) },
                    )
                    Help(
                        "Meant for split screen or a small window. The floating window switches to it on its own, so you do not need this for that.",
                    )
                }""",
    "View compact-mode TV cleanup")

s=once(s,
    """                ToggleRow(
                    title = "Highlight the line you hold",
                    subtitle = "A tinted box appears under your finger; holding also copies the line",
                    checked = settings.lineTapHighlight,
                    accent = accent,
                    onCheckedChange = { store.setLineTapHighlight(it) },
                )
                Help(
                    "A desktop shows this when the mouse is over a line; a finger has no hover, so it appears while you hold — which is also the gesture that copies the line.",
                )""",
    """                if (!tvOnlyCleanup) {
                    ToggleRow(
                        title = "Highlight the line you hold",
                        subtitle = "A tinted box appears under your finger; holding also copies the line",
                        checked = settings.lineTapHighlight,
                        accent = accent,
                        onCheckedChange = { store.setLineTapHighlight(it) },
                    )
                    Help(
                        "A desktop shows this when the mouse is over a line; a finger has no hover, so it appears while you hold — which is also the gesture that copies the line.",
                    )
                }""",
    "Lyrics display touch highlight TV cleanup")

s=once(s,
    """                ToggleRow(
                    title = "Tap a line to jump there",
                    subtitle = "Seeks the player to that line",
                    checked = settings.tapLineToSeek,
                    accent = accent,
                    onCheckedChange = { store.setTapLineToSeek(it) },
                )
                Help(
                    "Only works on lyrics that have timings. Holding a line still copies it either way.",
                )""",
    """                if (!tvOnlyCleanup) {
                    ToggleRow(
                        title = "Tap a line to jump there",
                        subtitle = "Seeks the player to that line",
                        checked = settings.tapLineToSeek,
                        accent = accent,
                        onCheckedChange = { store.setTapLineToSeek(it) },
                    )
                    Help(
                        "Only works on lyrics that have timings. Holding a line still copies it either way.",
                    )
                }""",
    "Timing tap-to-seek TV cleanup")

s=once(s,
    """                ToggleRow(
                    title = "Hold the background still in the floating window",
                    subtitle = "Living stops on a frame instead of moving",
                    checked = settings.popupStillBackground,
                    accent = accent,
                    onCheckedChange = { store.setPopupStillBackground(it) },
                )
                Help(
                    "The window is what stays on screen over whatever else you are doing, " +
                        "sometimes for an hour, and the drifting background is the most expensive " +
                        "thing this app draws. Off keeps it moving in there too.",
                )""",
    """                if (!tvOnlyCleanup) {
                    ToggleRow(
                        title = "Hold the background still in the floating window",
                        subtitle = "Living stops on a frame instead of moving",
                        checked = settings.popupStillBackground,
                        accent = accent,
                        onCheckedChange = { store.setPopupStillBackground(it) },
                    )
                    Help(
                        "The window is what stays on screen over whatever else you are doing, " +
                            "sometimes for an hour, and the drifting background is the most expensive " +
                            "thing this app draws. Off keeps it moving in there too.",
                    )
                }""",
    "Battery floating-window TV cleanup")

s=once(s,
    """                ActionRow(
                    title = "Copy all the lyrics",
                    subtitle = "Puts the whole song on the clipboard",
                    accent = accent,
                    onClick = onCopyAll,
                )""",
    """                if (!tvOnlyCleanup) {
                    ActionRow(
                        title = "Copy all the lyrics",
                        subtitle = "Puts the whole song on the clipboard",
                        accent = accent,
                        onClick = onCopyAll,
                    )
                }""",
    "This track clipboard action TV cleanup")

# Filtering at the Section composable keeps the entire Popup and Android Auto
# groups invisible on TV, while all phone layouts and saved settings remain.
section_start=s.index("private fun Section(")
section_end=s.index("\n@Composable\nprivate fun Hint(",section_start)
section=s[section_start:section_end]
section=once(section,
    """    Column(Modifier.fillMaxWidth().focusGroup()) {""",
    """    val tvSectionCleanup = LocalContext.current.packageManager.hasSystemFeature(
        android.content.pm.PackageManager.FEATURE_LEANBACK
    )
    if (tvSectionCleanup && (title == "Popup lyrics" || title == "Android Auto")) return

    Column(Modifier.fillMaxWidth().focusGroup()) {""",
    "hide phone-only Settings sections for TV")
s=s[:section_start]+section+s[section_end:]
sheet_path.write_text(s,encoding="utf-8")

print("Melisma v2.8.9: hide Popup/Android Auto, compact, touch-only, clipboard and floating-window UI on TV; all features retained for non-TV.")
