#!/usr/bin/env python3
"""TV seek-slider D-pad escape hatch for Melisma 2.11.0.

Runs after patch_tv_ui.py and patch_tv_transport.py.  Unlike a regular
Compose Slider, the TV slider should never trap Up/Down/Back focus.
"""
from pathlib import Path
import sys

ui = (Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")) / "app/src/main/java/com/melisma/app/ui"
pp = ui / "PlayerScreen.kt"
mp = ui / "components/MediaPanel.kt"
np = ui / "components/NowBar.kt"
hp = ui / "components/TvTransportFocus.kt"


def replace_one(s: str, old: str, new: str, name: str) -> str:
    n = s.count(old)
    if n != 1:
        raise RuntimeError(f"{name}: expected one match; found {n}")
    return s.replace(old, new, 1)


player = pp.read_text(encoding="utf-8")
# Parent preview callbacks run BEFORE a Slider can process Back. Intercept only
# if the root itself has focus, not if one of its children is active.
player = replace_one(player,
    """} else if (event.key == Key.Back && !toolbarVisible &&
                    !showSettings && !showWelcome) {""",
    """} else if (event.key == Key.Back && rootFocused && !toolbarVisible &&
                    !showSettings && !showWelcome) {""",
    "root Back must not intercept focused seek slider")

# Construct a single callback for when Up is pressed on either seek bar.
player = replace_one(player,
    """    val tvKeys = if (tvMode && !popup) {""",
    """    val tvSeekUp: (() -> Unit)? = if (tvMode && !popup &&
        settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {
        {
            toolbarVisible = true
            toolbarActivity++
            scope.launch {
                delay(80)
                runCatching { toolbarFocus.requestFocus() }
            }
        }
    } else null
    val tvKeys = if (tvMode && !popup) {""",
    "TV seek-bar Up callback")

player = replace_one(player,
    """                        tvTransportFocusRequester = if (tvMode) transportFocus else null,
                        permissionGranted = permissionGranted,""",
    """                        tvTransportFocusRequester = if (tvMode) transportFocus else null,
                        onTvSeekUp = tvSeekUp,
                        permissionGranted = permissionGranted,""",
    "MainContent seek Up callback")

player = replace_one(player,
    """                                tvTransportFocusRequester = if (tvMode) transportFocus else null,
                                transport = snapshot.transport,""",
    """                                tvTransportFocusRequester = if (tvMode) transportFocus else null,
                                onTvSeekUp = tvSeekUp,
                                transport = snapshot.transport,""",
    "NowBar seek Up callback")

player = replace_one(player,
    """    tvTransportFocusRequester: FocusRequester?,
    permissionGranted: Boolean,""",
    """    tvTransportFocusRequester: FocusRequester?,
    onTvSeekUp: (() -> Unit)?,
    permissionGranted: Boolean,""",
    "MainContent seek Up parameter")

player = replace_one(player,
    """                            tvTransportFocusRequester = tvTransportFocusRequester,
                            transport = snapshot.transport,""",
    """                            tvTransportFocusRequester = tvTransportFocusRequester,
                            onTvSeekUp = onTvSeekUp,
                            transport = snapshot.transport,""",
    "MediaPanel seek Up callback")
pp.write_text(player, encoding="utf-8")

panel = mp.read_text(encoding="utf-8")
panel = replace_one(panel,
    """    tvTransportFocusRequester: FocusRequester? = null,
) {""",
    """    tvTransportFocusRequester: FocusRequester? = null,
    onTvSeekUp: (() -> Unit)? = null,
) {""",
    "MediaPanel seek Up parameter")
panel = replace_one(panel,
    """                rightLabel = formatTime(playback.durationMs),
            )""",
    """                rightLabel = formatTime(playback.durationMs),
                onTvSeekUp = onTvSeekUp,
                tvTransportFocusRequester = tvTransportFocusRequester,
            )""",
    "MediaPanel ScrubBar call")
panel = replace_one(panel,
    """    rightLabel: String,
) {
    Slider(""",
    """    rightLabel: String,
    onTvSeekUp: (() -> Unit)?,
    tvTransportFocusRequester: FocusRequester?,
) {
    Slider(""",
    "MediaPanel ScrubBar parameters")
panel = replace_one(panel,
    """            .height(24.dp)
            .tvTransportFocus(RoundedCornerShape(8.dp)),""",
    """            .height(24.dp)
            .tvSeekEscape(tvTransportFocusRequester, onTvSeekUp)
            .tvTransportFocus(RoundedCornerShape(8.dp)),""",
    "MediaPanel slider keyboard escape")
mp.write_text(panel, encoding="utf-8")

now = np.read_text(encoding="utf-8")
now = replace_one(now,
    """    tvTransportFocusRequester: FocusRequester? = null,
) {""",
    """    tvTransportFocusRequester: FocusRequester? = null,
    onTvSeekUp: (() -> Unit)? = null,
) {""",
    "NowBar seek Up parameter")
now = replace_one(now,
    """                .height(24.dp)
                .tvTransportFocus(RoundedCornerShape(8.dp)),""",
    """                .height(24.dp)
                .tvSeekEscape(tvTransportFocusRequester, onTvSeekUp)
                .tvTransportFocus(RoundedCornerShape(8.dp)),""",
    "NowBar slider keyboard escape")
np.write_text(now, encoding="utf-8")

helper = hp.read_text(encoding="utf-8")
helper = replace_one(helper,
    """import androidx.compose.ui.focus.onFocusChanged
""",
    """import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.key.onPreviewKeyEvent
""",
    "focus helper key imports")
helper += """

/**
 * Explicit remote navigation for TV seek sliders, whose internal arrow handling may
 * otherwise trap D-pad focus. Left/Right are intentionally NOT intercepted: they
 * still seek as normal. Up reveals the top menu, Down and Back return to Play/Pause.
 */
@Composable
internal fun Modifier.tvSeekEscape(
    playRequester: FocusRequester?,
    onUp: (() -> Unit)?,
): Modifier {
    val context = LocalContext.current
    if (!context.packageManager.hasSystemFeature(PackageManager.FEATURE_LEANBACK)) return this
    return this.onPreviewKeyEvent { event ->
        if (event.type != KeyEventType.KeyDown) return@onPreviewKeyEvent false
        when (event.key) {
            Key.DirectionUp -> {
                if (onUp != null) {
                    onUp()
                    true
                } else {
                    runCatching { playRequester?.requestFocus() ?: false }.getOrDefault(false)
                }
            }
            Key.DirectionDown, Key.Back ->
                runCatching { playRequester?.requestFocus() ?: false }.getOrDefault(false)
            else -> false
        }
    }
}
"""
hp.write_text(helper, encoding="utf-8")
print("Patched TV seek slider: Up=toolbar; Down/Back=Play; Left/Right=seek.")
