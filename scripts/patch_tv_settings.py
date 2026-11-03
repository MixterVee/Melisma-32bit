#!/usr/bin/env python3
"""Improve D-pad traversal of Settings Style choices in Melisma v2.11.0.

Run after patch_tv_ui.py, patch_tv_transport.py and patch_tv_seek_escape.py.
Stops if any expected upstream source anchor has changed.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
path = root / "app/src/main/java/com/melisma/app/ui/SettingsSheet.kt"
s = path.read_text(encoding="utf-8")


def rep(old: str, new: str, name: str):
    global s
    n = s.count(old)
    if n != 1:
        raise RuntimeError(f"{name}: expected 1 anchor, found {n}")
    s = s.replace(old, new, 1)


rep("import androidx.compose.foundation.background\n",
    "import androidx.compose.foundation.background\nimport androidx.compose.foundation.focusGroup\n",
    "focusGroup import")
rep("import androidx.compose.foundation.rememberScrollState\n",
    """import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
""", "bring-into-view imports")
rep("import androidx.compose.runtime.Composable\n",
    """import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.compositionLocalOf
""", "composition local imports")
rep("import androidx.compose.ui.focus.focusRequester\n",
    """import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.focusProperties
""", "focus properties import")
rep("import androidx.compose.ui.focus.focusProperties\n",
    """import androidx.compose.ui.focus.focusProperties
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.key.onPreviewKeyEvent
""", "settings D-pad key imports")

rep("""// ---- building blocks ----------------------------------------------------------
""",
    """/** Shared only by the expanded Background section's first Style chip. */
private val LocalBackgroundFirstChoice = compositionLocalOf<FocusRequester?> { null }

// ---- building blocks ----------------------------------------------------------
""", "first-choice composition local")

rep("""    Column(Modifier.fillMaxWidth()) {
        HorizontalDivider(color = Color.White.copy(alpha = 0.07f))
        Row(
            Modifier
                .fillMaxWidth()
                .tvFocusOutline()
                .clickable(onClick = onToggle)
                .padding(vertical = 14.dp),
""",
    """    val backgroundSection = title == "Background" || title == "View"
    val firstBackgroundChoice = remember { FocusRequester() }
    val tv = LocalContext.current.packageManager.hasSystemFeature(
        android.content.pm.PackageManager.FEATURE_LEANBACK
    )
    var headerFocused by remember { mutableStateOf(false) }
    Column(Modifier.fillMaxWidth().focusGroup()) {
        HorizontalDivider(color = Color.White.copy(alpha = 0.07f))
        Row(
            Modifier
                .fillMaxWidth()
                .then(
                    if (tv && backgroundSection && open)
                        Modifier.focusProperties { down = firstBackgroundChoice }
                    else Modifier
                )
                .onFocusChanged { headerFocused = it.hasFocus }
                .onPreviewKeyEvent { event ->
                    if (tv && headerFocused && backgroundSection && open &&
                        event.type == KeyEventType.KeyDown && event.key == Key.DirectionDown
                    ) {
                        runCatching { firstBackgroundChoice.requestFocus() }.getOrDefault(false)
                    } else false
                }
                .tvFocusOutline()
                .clickable(onClick = onToggle)
                .padding(vertical = 14.dp),
""", "Background section header redirect")

rep("""        AnimatedVisibility(visible = open) {
            Column(Modifier.fillMaxWidth().padding(bottom = 10.dp)) { content() }
        }
""",
    """        AnimatedVisibility(visible = open) {
            CompositionLocalProvider(
                LocalBackgroundFirstChoice provides if (backgroundSection && tv)
                    firstBackgroundChoice else null
            ) {
                Column(Modifier.fillMaxWidth().padding(bottom = 10.dp).focusGroup()) { content() }
            }
        }
""", "Expanded section focus group and focus target provider")

rep("""    Column(Modifier.fillMaxWidth().padding(top = 4.dp)) {
        Text(
            label,
""",
    """    val firstBackgroundChoice =
        if (label == "Style" || label == "Layout") LocalBackgroundFirstChoice.current else null
    Column(Modifier.fillMaxWidth().padding(top = 4.dp).focusGroup()) {
        Text(
            label,
""", "Focus group for choice sets")

rep("""        options.chunked(columns).forEach { row ->
            Row(
                Modifier.fillMaxWidth().padding(bottom = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                row.forEach { (value, text) ->
                    ChoiceChip(
                        text = text,
                        selected = value == selected,
                        accent = accent,
                        onClick = { onSelect(value) },
                        modifier = Modifier.weight(1f),
                    )
                }
""",
    """        options.chunked(columns).forEachIndexed { rowIndex, row ->
            Row(
                Modifier.fillMaxWidth().padding(bottom = 8.dp).focusGroup(),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                row.forEachIndexed { columnIndex, (value, text) ->
                    ChoiceChip(
                        text = text,
                        selected = value == selected,
                        accent = accent,
                        onClick = { onSelect(value) },
                        modifier = Modifier.weight(1f).then(
                            if (rowIndex == 0 && columnIndex == 0 &&
                                firstBackgroundChoice != null
                            ) Modifier.focusRequester(firstBackgroundChoice)
                            else Modifier
                        ),
                    )
                }
""", "Style choices marked as ordered focus targets")

rep("""private fun ChoiceChip(
    text: String,
    selected: Boolean,
    accent: Color,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Box(
        modifier
            .clip(RoundedCornerShape(10.dp))
            .background(if (selected) accent.copy(alpha = 0.3f) else Color.White.copy(alpha = 0.07f))
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 9.dp),
        contentAlignment = Alignment.Center,
    ) {
""",
    """private fun ChoiceChip(
    text: String,
    selected: Boolean,
    accent: Color,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val tv = LocalContext.current.packageManager.hasSystemFeature(
        android.content.pm.PackageManager.FEATURE_LEANBACK
    )
    var focused by remember { mutableStateOf(false) }
    val bringIntoView = remember { BringIntoViewRequester() }
    LaunchedEffect(focused) {
        if (focused && tv) runCatching { bringIntoView.bringIntoView() }
    }
    Box(
        modifier
            .bringIntoViewRequester(bringIntoView)
            .onFocusChanged { focused = it.hasFocus }
            .border(
                width = if (tv && focused) 3.dp else 0.dp,
                color = if (tv && focused) Color(0xFF00D5FF) else Color.Transparent,
                shape = RoundedCornerShape(10.dp),
            )
            .clip(RoundedCornerShape(10.dp))
            .background(
                if (tv && focused) Color(0x4400D5FF)
                else if (selected) accent.copy(alpha = 0.3f)
                else Color.White.copy(alpha = 0.07f)
            )
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp, vertical = 9.dp),
        contentAlignment = Alignment.Center,
    ) {
""", "TV focus and bring-into-view for every choice chip")

path.write_text(s, encoding="utf-8")
print("Patched Settings Background header and chip focus order, highlighting and scrolling.")
