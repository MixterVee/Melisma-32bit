#!/usr/bin/env python3
"""Audit-wide Android TV D-pad focus support for Melisma SettingsSheet 2.11.0.

Apply after patch_tv_settings.py. Provides explicit first-control focus for
expanded sections, visible focus for all common control types, automatic
scroll-to-focus, and avoids slider Up/Down and switch focus traps.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
path = root / "app/src/main/java/com/melisma/app/ui/SettingsSheet.kt"
s = path.read_text(encoding="utf-8")


def patch(old, new, why):
    global s
    n = s.count(old)
    if n != 1:
        raise RuntimeError(f"{why}: expected exactly one match, got {n}")
    s = s.replace(old, new, 1)


patch("import androidx.compose.ui.focus.focusProperties\n",
      "import androidx.compose.ui.focus.focusProperties\nimport androidx.compose.ui.focus.FocusDirection\n",
      "focus direction import")

patch("""/** Shared only by the expanded Background section's first Style chip. */
private val LocalBackgroundFirstChoice = compositionLocalOf<FocusRequester?> { null }
""",
      """/** Direct first-choice target for View and Background grouped options. */
private val LocalBackgroundFirstChoice = compositionLocalOf<FocusRequester?> { null }

/** Every section can route Down directly to its first interactive control. */
private data class TvSectionEntry(val title: String, val requester: FocusRequester)
private val LocalTvSectionEntry = compositionLocalOf<TvSectionEntry?> { null }

/** Maps first focusable setting in each fixed-section layout of Melisma 2.11.0. */
@Composable
private fun firstTvSettingFocus(type: String, label: String): Modifier {
    val entry = LocalTvSectionEntry.current ?: return Modifier
    val expected = when (entry.title) {
        "Popup lyrics" -> "toggle" to "Popup lyrics"
        "Android Auto" -> "toggle" to "Plain screen in the car"
        "Lyrics display" -> "slider" to "Text size"
        "Timing" -> "slider" to "Sync offset"
        "Language" -> "toggle" to "Romanization"
        "Where lyrics come from" -> "toggle" to "Identify the recording"
        "Tokens and endpoints" -> "secret" to "Spotify sp_dc cookie"
        "This track" -> "action" to "Copy all the lyrics"
        "Battery" -> "toggle" to "Keep the screen on while playing"
        "About" -> "toggle" to "Check for updates automatically"
        "Developer" -> "toggle" to "Developer options"
        "Storage and backup" ->
            "action" to if (label == "Cache cleared") "Cache cleared"
                        else "Clear the lyrics cache"
        else -> null
    }
    return if (expected?.first == type && expected.second == label) {
        Modifier.focusRequester(entry.requester)
    } else Modifier
}

/** Up/Down should exit a settings slider; Left/Right retain normal value adjustments. */
@Composable
private fun Modifier.tvSettingsSliderKeys(): Modifier {
    val tv = LocalContext.current.packageManager.hasSystemFeature(
        android.content.pm.PackageManager.FEATURE_LEANBACK
    )
    val focusManager = LocalFocusManager.current
    if (!tv) return this
    return this.onPreviewKeyEvent { event ->
        if (event.type != KeyEventType.KeyDown) return@onPreviewKeyEvent false
        when (event.key) {
            Key.DirectionUp -> focusManager.moveFocus(FocusDirection.Up)
            Key.DirectionDown -> focusManager.moveFocus(FocusDirection.Down)
            else -> false
        }
    }
}
""", "all-section entry focus support")

# Both focus group and explicit first child selection apply to ALL expanded sections.
patch("if (tv && backgroundSection && open)\n                        Modifier.focusProperties { down = firstBackgroundChoice }",
      "if (tv && open)\n                        Modifier.focusProperties { down = firstBackgroundChoice }",
      "all-section header down target")
patch("if (tv && headerFocused && backgroundSection && open &&",
      "if (tv && headerFocused && open &&",
      "all-section header down navigation")
patch("""                LocalBackgroundFirstChoice provides if (backgroundSection && tv)
                    firstBackgroundChoice else null
            ) {""",
      """                LocalBackgroundFirstChoice provides if (backgroundSection && tv)
                    firstBackgroundChoice else null,
                LocalTvSectionEntry provides if (tv) TvSectionEntry(title, firstBackgroundChoice)
                    else null
            ) {""", "expose first focus to section controls")

# Most sections start with a ToggleRow.
patch("""    Row(
        Modifier
            .fillMaxWidth()
            .tvFocusOutline()
            .clickable { onCheckedChange(!checked) }
            .padding(vertical = 8.dp),
""",
      """    Row(
        Modifier
            .fillMaxWidth()
            .then(firstTvSettingFocus("toggle", title))
            .tvFocusOutline()
            .clickable { onCheckedChange(!checked) }
            .padding(vertical = 8.dp),
""", "toggle first focus target")

patch("""        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            colors = SwitchDefaults.colors(""",
      """        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            // Only the parent ToggleRow should be a separate TV remote stop.
            modifier = Modifier.focusProperties { canFocus = false },
            colors = SwitchDefaults.colors(""", "avoid two focus stops for one toggle")

# SliderRow first focus target and consistent Up/Down navigation.
patch("""        Slider(
            value = value.coerceIn(range.start, range.endInclusive),
            onValueChange = onChange,
            valueRange = range,""",
      """        Slider(
            value = value.coerceIn(range.start, range.endInclusive),
            onValueChange = onChange,
            modifier = Modifier
                .then(firstTvSettingFocus("slider", label))
                .tvSettingsSliderKeys()
                .tvFocusOutline(),
            valueRange = range,""", "slider focus and arrow exit")

# ActionRow first focus target.
patch("""    Row(
        Modifier
            .fillMaxWidth()
            .tvFocusOutline()
            .clickable(onClick = onClick)
            .padding(vertical = 10.dp),
""",
      """    Row(
        Modifier
            .fillMaxWidth()
            .then(firstTvSettingFocus("action", title))
            .tvFocusOutline()
            .clickable(onClick = onClick)
            .padding(vertical = 10.dp),
""", "action row focus target")

# Tokens/endpoints are input fields rather than buttons.
patch("""                readOnly = readOnly,
                modifier = Modifier.fillMaxWidth(),
""",
      """                readOnly = readOnly,
                modifier = Modifier
                    .fillMaxWidth()
                    .then(firstTvSettingFocus("secret", label))
                    .tvFocusOutline(),
""", "secret field focus target")

# High-contrast focus for less-common interactive widgets too.
patch("""            handle.size(38.dp).clip(CircleShape),""",
      """            handle.size(38.dp).clip(CircleShape).tvFocusOutline(),""",
      "player drag handle focus")
patch("""    Row(
        Modifier.fillMaxWidth().padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        // Left of the name rather than right of the switch:""",
      """    Row(
        Modifier.fillMaxWidth().padding(vertical = 6.dp).tvFocusOutline(),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        // Left of the name rather than right of the switch:""",
      "player toggle row focus")

patch("""            .clip(RoundedCornerShape(16.dp))
            .background(accent.copy(alpha = 0.22f))
            .clickable(onClick = onClick)""",
      """            .clip(RoundedCornerShape(16.dp))
            .background(accent.copy(alpha = 0.22f))
            .tvFocusOutline()
            .clickable(onClick = onClick)""", "step button focus")

# The display of currently focused settings should scroll into view.
patch("""    var focused by remember { mutableStateOf(false) }
    return this
        .onFocusChanged { focused = it.hasFocus }
        .border(
            if (focused) 2.dp else 0.dp,""",
      """    var focused by remember { mutableStateOf(false) }
    val bringIntoView = remember { BringIntoViewRequester() }
    LaunchedEffect(focused) {
        if (focused) runCatching { bringIntoView.bringIntoView() }
    }
    return this
        .bringIntoViewRequester(bringIntoView)
        .onFocusChanged { focused = it.hasFocus }
        .border(
            if (focused) 2.dp else 0.dp,""",
      "all settings focused elements auto scroll")

# Provide visual focus for global settings help button.
patch("""                        .size(34.dp)
                        .clip(CircleShape)
                        .background(
                            if (showHelp) accent.copy(alpha = 0.3f)""",
      """                        .size(34.dp)
                        .tvFocusOutline()
                        .clip(CircleShape)
                        .background(
                            if (showHelp) accent.copy(alpha = 0.3f)""",
      "settings help button focus")

path.write_text(s, encoding="utf-8")
print("Completed all-settings TV focus audit: sections, toggles, sliders, choices, actions, inputs.")
