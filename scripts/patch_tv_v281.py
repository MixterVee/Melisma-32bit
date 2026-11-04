#!/usr/bin/env python3
"""Melisma Universal v2.8.1: transparent clock, toolbar Exit, languages Off.

Apply after patch_tv_v28.py against upstream Melisma v2.11.0.
Existing saved language preferences are retained; only missing-key defaults change.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
base = root / "app/src/main/java/com/melisma/app"
player_file = base / "ui/PlayerScreen.kt"
sheet_file = base / "ui/SettingsSheet.kt"
store_file = base / "settings/SettingsStore.kt"
icons_file = base / "ui/components/AppIcons.kt"

def once(source, before, after, label):
    count = source.count(before)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count}")
    return source.replace(before, after, 1)

# Clock: retain the right-aligned placement and readable white time, but let
# the underlying artwork show through without any black card or tint.
player = player_file.read_text(encoding="utf-8")
player = once(player,
    """                    .padding(top = 8.dp, end = 18.dp)
                    .clip(RoundedCornerShape(9.dp))
                    .background(Color.Black.copy(alpha = 0.50f))
                    .padding(horizontal = 11.dp, vertical = 6.dp),""",
    """                    .padding(top = 8.dp, end = 18.dp)
                    .padding(horizontal = 11.dp, vertical = 6.dp),""",
    "remove clock background and rounded card")

# Move the existing confirmed Exit action from Settings to the main toolbar.
# Keep cleanup scoped to Melisma: do not stop the external music player.
player = once(player,
    """import androidx.compose.material3.Icon
import androidx.compose.material3.Text
""",
    """import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.TextButton
""",
    "player Exit confirmation imports")
player = once(player,
    """    var showSettings by remember { mutableStateOf(false) }""",
    """    var showSettings by remember { mutableStateOf(false) }
    var askExitConfirmation by remember { mutableStateOf(false) }""",
    "player exit confirmation state")
player = once(player,
    """                        onSettings = { showSettings = true },
                    )""",
    """                        onSettings = { showSettings = true },
                        onExit = {
                            toolbarVisible = true
                            askExitConfirmation = true
                        },
                    )""",
    "main toolbar Exit callback")
player = once(player,
    """                onDismiss = { showSettings = false },
                onExitApp = {
                    container.media.stop()
                    com.melisma.app.media.MediaNotificationListener.standDown()
                    (context as? android.app.Activity)?.finishAndRemoveTask()
                },
                onTvOffsetChange = if (tvMode)""",
    """                onDismiss = { showSettings = false },
                onTvOffsetChange = if (tvMode)""",
    "remove Settings Exit callback")
player = once(player,
    """        if (showSettings) {
            SettingsSheet(""",
    """        if (askExitConfirmation) {
            AlertDialog(
                onDismissRequest = { askExitConfirmation = false },
                title = { Text("Exit Melisma?") },
                text = { Text("Melisma will close, but music in the source app will keep playing.") },
                confirmButton = {
                    TextButton(onClick = {
                        askExitConfirmation = false
                        container.media.stop()
                        com.melisma.app.media.MediaNotificationListener.standDown()
                        (context as? android.app.Activity)?.finishAndRemoveTask()
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

        if (showSettings) {
            SettingsSheet(""",
    "confirmed Exit modal moved to main player")
player = once(player,
    """    onRetry: () -> Unit,
    onSettings: () -> Unit,
) {""",
    """    onRetry: () -> Unit,
    onSettings: () -> Unit,
    onExit: () -> Unit,
) {""",
    "main ViewControls Exit parameter")
player = once(player,
    """                ActionChip(AppIcons.Tune, "Settings", false, accent, onSettings)
            }
        }""",
    """                ActionChip(AppIcons.Tune, "Settings", false, accent, onSettings)
                ActionChip(AppIcons.Exit, "Exit Melisma", false, accent, onExit)
            }
        }""",
    "Exit icon in main toolbar")
player_file.write_text(player, encoding="utf-8")

sheet = sheet_file.read_text(encoding="utf-8")
sheet = once(sheet,
    """import androidx.compose.material3.AlertDialog
import androidx.compose.material3.TextButton
""",
    "",
    "remove unused Settings modal imports")
sheet = once(sheet,
    """    onShowWelcome: () -> Unit,
    onExitApp: () -> Unit,
    onDismiss: () -> Unit,""",
    """    onShowWelcome: () -> Unit,
    onDismiss: () -> Unit,""",
    "remove Settings Exit parameter")
sheet = once(sheet,
    """    var showCredits by remember { mutableStateOf(false) }
    var askExitConfirmation by remember { mutableStateOf(false) }""",
    """    var showCredits by remember { mutableStateOf(false) }""",
    "remove Settings exit state")
sheet = once(sheet,
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
""",
    "",
    "remove Exit from Settings header")
sheet = once(sheet,
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

""",
    "",
    "remove dialog from Settings")
sheet_file.write_text(sheet, encoding="utf-8")

icons = icons_file.read_text(encoding="utf-8")
icons = once(icons,
    """    val Close by lazy {""",
    """    /** Exit this application without stopping music in the source player. */
    val Exit by lazy {
        icon(
            "Exit",
            "M10.09,15.59L11.5,17l5,-5 -5,-5 -1.41,1.41L12.67,11H3v2h9.67l-2.58,2.59z" +
                "M19,3H5C3.9,3 3,3.9 3,5v4h2V5h14v14H5v-4H3v4c0,1.1 0.9,2 2,2h14c1.1,0 2,-0.9 2,-2V5c0,-1.1 -0.9,-2 -2,-2z",
        )
    }

    val Close by lazy {""",
    "dedicated Exit toolbar icon")
icons_file.write_text(icons, encoding="utf-8")

# Only default values change: existing choices saved in SharedPreferences win.
store = store_file.read_text(encoding="utf-8")
store = once(store,
    """    val showRomanization: Boolean = true,""",
    """    val showRomanization: Boolean = false,""",
    "romanization default Off")
store = once(store,
    """    val detectHokkien: Boolean = true,""",
    """    val detectHokkien: Boolean = false,""",
    "Hokkien recognition default Off")
store = once(store,
    """    val translationSource: TranslationSource = TranslationSource.PROVIDER,""",
    """    val translationSource: TranslationSource = TranslationSource.OFF,""",
    "translation default Off")
store = once(store,
    """        showRomanization = prefs.getBoolean(KEY_ROMANIZE, true),""",
    """        showRomanization = prefs.getBoolean(KEY_ROMANIZE, false),""",
    "romanization persisted-key fallback Off")
store = once(store,
    """        detectHokkien = prefs.getBoolean(KEY_DETECT_HOKKIEN, true),""",
    """        detectHokkien = prefs.getBoolean(KEY_DETECT_HOKKIEN, false),""",
    "Hokkien recognition persisted-key fallback Off")
store = once(store,
    """        return if (getBoolean(KEY_TRANSLATE_LEGACY, false)) {
            TranslationSource.DEVICE
        } else {
            TranslationSource.PROVIDER
        }""",
    """        return if (getBoolean(KEY_TRANSLATE_LEGACY, false)) {
            TranslationSource.DEVICE
        } else {
            TranslationSource.OFF
        }""",
    "translation persisted-key fallback Off, preserving explicit legacy On")
store_file.write_text(store, encoding="utf-8")

print("Melisma v2.8.1: unobstructed clock, confirmed Exit on main toolbar, all alternate-language overlays Off by default; saved user choices retained.")
