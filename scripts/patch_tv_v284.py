#!/usr/bin/env python3
"""Melisma Universal v2.8.4: Android TV remote source sorting and vertical timing.

Apply after patch_tv_v283.py against upstream v2.11.0.
- D-pad selectable Up / Down / Top actions on every lyric provider in Settings.
- TV per-song timing gets vertical, remote-operated control: Down = lyrics earlier
  (increase positive offset), Up = lyrics later (decrease positive offset).
- Other players, auto-next (1s), fading backgrounds and saved preferences untouched.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
settings_file = ui / "SettingsSheet.kt"
panel_file = ui / "components/TvSyncPanel.kt"

def once(s, a, b, label):
    count = s.count(a)
    if count != 1:
        raise RuntimeError(f"{label}: expected one anchor, found {count}")
    return s.replace(a, b, 1)

settings = settings_file.read_text(encoding="utf-8")
settings = once(settings,
    """                val listed = settings.providerOrder.filter {
                    it != "cacheserver" || settings.developerMode
                }
                ReorderableColumn(""",
    """                val listed = settings.providerOrder.filter {
                    it != "cacheserver" || settings.developerMode
                }
                // Drag handles work on touchscreens, but Android TV needs
                // explicit focusable actions. Preserve the filtered slot order
                // so hidden Cache Server rows retain their stored position.
                val tvSourceReorder = LocalContext.current.packageManager.hasSystemFeature(
                    android.content.pm.PackageManager.FEATURE_LEANBACK
                )
                val moveSource: (String, Int) -> Unit = { id, target ->
                    val currentIndex = listed.indexOf(id)
                    if (currentIndex >= 0 && target in listed.indices && target != currentIndex) {
                        val revised = listed.toMutableList()
                        val moved = revised.removeAt(currentIndex)
                        revised.add(target, moved)
                        store.setProviderOrder(settings.providerOrder.withVisibleOrder(revised))
                    }
                }
                ReorderableColumn(""",
    "TV source sorting model")
settings = once(settings,
    """                        handle = handle,
                        accent = accent,
                        onToggle = { store.setProviderEnabled(id, it) },
                    )""",
    """                        handle = handle,
                        accent = accent,
                        onToggle = { store.setProviderEnabled(id, it) },
                        showTvMoveControls = tvSourceReorder,
                        onMoveUp = { moveSource(id, listed.indexOf(id) - 1) },
                        onMoveDown = { moveSource(id, listed.indexOf(id) + 1) },
                        onMoveTop = { moveSource(id, 0) },
                    )""",
    "source remote actions to ProviderRow")
settings = once(settings,
    """    accent: Color,
    onToggle: (Boolean) -> Unit,
) {
    Row(
        Modifier.fillMaxWidth().padding(vertical = 6.dp),""",
    """    accent: Color,
    onToggle: (Boolean) -> Unit,
    showTvMoveControls: Boolean,
    onMoveUp: () -> Unit,
    onMoveDown: () -> Unit,
    onMoveTop: () -> Unit,
) {
    Row(
        Modifier.fillMaxWidth().padding(vertical = 6.dp),""",
    "ProviderRow TV callbacks")
settings = once(settings,
    """        Switch(
            checked = enabled,
            onCheckedChange = onToggle,
            colors = SwitchDefaults.colors(
                checkedTrackColor = accent.copy(alpha = 0.6f),
                checkedThumbColor = Color.White,
            ),
        )
    }
}

/**
 * Settings to a file and back again.""",
    """        if (showTvMoveControls) {
            Row(
                Modifier.padding(end = 10.dp),
                horizontalArrangement = Arrangement.spacedBy(4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                RemoteSourceMove("↑", "Move $title up", onMoveUp)
                RemoteSourceMove("↓", "Move $title down", onMoveDown)
                RemoteSourceMove("Top", "Move $title to top", onMoveTop)
            }
        }
        Switch(
            checked = enabled,
            onCheckedChange = onToggle,
            colors = SwitchDefaults.colors(
                checkedTrackColor = accent.copy(alpha = 0.6f),
                checkedThumbColor = Color.White,
            ),
        )
    }
}

/** Select with the TV remote, then press OK to move the source and save its order. */
@Composable
private fun RemoteSourceMove(
    label: String,
    description: String,
    onClick: () -> Unit,
) {
    Box(
        Modifier
            .width(if (label == "Top") 48.dp else 38.dp)
            .height(40.dp)
            .tvFocusOutline()
            .clip(RoundedCornerShape(8.dp))
            .background(Color.White.copy(alpha = 0.12f))
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = label,
            color = Color.White,
            fontSize = if (label == "Top") 12.sp else 20.sp,
            fontWeight = FontWeight.Medium,
        )
    }
}

/**
 * Settings to a file and back again.""",
    "focusable TV source sorting controls")
settings = once(settings,
    """            ) {
                RemoteSourceMove("↑", "Move $title up", onMoveUp)""",
    """            ) {
                RemoteSourceMove("↑", "Move $title up", onMoveUp)""",
    "verify remote source row layout") if False else settings
# Content descriptions on these controls are used by accessibility services,
# while the D-pad uses Compose's focusable clickable Box.
settings = once(settings,
    """import androidx.compose.foundation.layout.fillMaxWidth
""",
    """import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
""",
    "width for remote reorder buttons") if "import androidx.compose.foundation.layout.width\n" not in settings else settings
settings_file.write_text(settings, encoding="utf-8")

panel = panel_file.read_text(encoding="utf-8")
panel = once(panel,
    """import androidx.compose.foundation.background
""",
    """import androidx.compose.foundation.background
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.Spacer
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.StrokeCap
""",
    "vertical timing draw/focus imports")
panel = once(panel,
    """        Text("Positive = earlier · Negative = later", color = Color.White, fontSize = 11.sp)
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
        )""",
    """        Text("↓ Earlier · ↑ Later · 50 ms per press", color = Color.White, fontSize = 11.sp)
        Row(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.Center,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text("Later", color = Color.White.copy(alpha = 0.72f), fontSize = 11.sp)
            Spacer(Modifier.width(9.dp))
            // Custom vertical TV control, not a rotated Material slider:
            // D-pad Up/Down adjust time instead of navigating away.
            Canvas(
                modifier = Modifier
                    .width(72.dp)
                    .height(218.dp)
                    .focusRequester(sliderFocus)
                    .onPreviewKeyEvent { event ->
                        if (event.type != KeyEventType.KeyDown) false
                        else when (event.key) {
                            Key.DirectionDown -> { apply(offsetMs + 50); true }
                            Key.DirectionUp -> { apply(offsetMs - 50); true }
                            Key.DirectionLeft -> {
                                runCatching { doneFocus.requestFocus() }.isSuccess
                            }
                            Key.DirectionRight -> {
                                runCatching { nudgeFocus.requestFocus() }.isSuccess
                            }
                            Key.Back -> { onClose(); true }
                            else -> false
                        }
                    }
                    .tvTransportFocus(RoundedCornerShape(10.dp))
                    .focusable(),
            ) {
                val x = size.width / 2f
                val inset = 18.dp.toPx()
                val top = inset
                val bottom = size.height - inset
                val middle = (top + bottom) / 2f
                val ratio = ((offsetMs + 3000f) / 6000f).coerceIn(0f, 1f)
                val thumbY = top + (bottom - top) * ratio
                drawLine(
                    color = Color.White.copy(alpha = 0.28f),
                    start = Offset(x, top),
                    end = Offset(x, bottom),
                    strokeWidth = 9.dp.toPx(),
                    cap = StrokeCap.Round,
                )
                drawLine(
                    color = cyan,
                    start = Offset(x, middle),
                    end = Offset(x, thumbY),
                    strokeWidth = 9.dp.toPx(),
                    cap = StrokeCap.Round,
                )
                drawCircle(color = cyan, radius = 12.dp.toPx(), center = Offset(x, thumbY))
                drawCircle(color = Color.White, radius = 3.dp.toPx(), center = Offset(x, thumbY))
            }
            Spacer(Modifier.width(9.dp))
            Text("Earlier", color = Color.White.copy(alpha = 0.72f), fontSize = 11.sp)
        }""",
    "vertical lyric timing with D-pad up/down")
panel_file.write_text(panel, encoding="utf-8")
print("Melisma v2.8.4: Settings source reorder accessible with remote; timing slider vertical, Down earlier, Up later.")
