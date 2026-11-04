#!/usr/bin/env python3
"""Melisma Universal v2.7.2: repair Timing section's first TV D-pad focus.

The v2.7.1 attempt put the Timing FocusRequester on a custom focusable
SliderRow wrapper, which is not a reliably selectable TV action on Shield.
Attach the Section's first-entry requester instead to the actual clickable
'−50' StepButton, using the same mechanism as other working clickable controls.
That gives Down a stable target and direct timing adjustments immediately.
Apply after patch_tv_v271.py against Melisma v2.11.0.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
path = root / "app/src/main/java/com/melisma/app/ui/SettingsSheet.kt"
source = path.read_text(encoding="utf-8")

def replace_once(old: str, new: str, label: str):
    global source
    n = source.count(old)
    if n != 1:
        raise RuntimeError(f"{label}: expected 1 anchor, got {n}")
    source = source.replace(old, new, 1)

replace_once(
    '"Timing" -> "slider" to "Sync offset"',
    '"Timing" -> "step" to "−50"',
    "Timing section direct first focus maps to a clickable StepButton",
)

replace_once(
    """private fun StepButton(label: String, accent: Color, onClick: () -> Unit) {
    Box(
        Modifier
            .clip(RoundedCornerShape(16.dp))
            .background(accent.copy(alpha = 0.22f))
            .tvFocusOutline()
            .clickable(onClick = onClick)""",
    """private fun StepButton(label: String, accent: Color, onClick: () -> Unit) {
    Box(
        Modifier
            .clip(RoundedCornerShape(16.dp))
            .background(accent.copy(alpha = 0.22f))
            .then(firstTvSettingFocus("step", label))
            .tvFocusOutline()
            .clickable(onClick = onClick)""",
    "focus requester before StepButton clickable focus target",
)

# Ensure the target really exists in Timing and the v2.7.1 slider wrapper
# no longer claims the shared FocusRequester (mapping above makes it return
# Modifier for the slider).
if source.count('StepButton("−50", accent)') != 1:
    raise RuntimeError("Timing section's −50 step button missing or ambiguous")
if source.count('firstTvSettingFocus("step", label)') != 1:
    raise RuntimeError("Expected exactly one StepButton requester attachment")
path.write_text(source, encoding="utf-8")
print("TV v2.7.2: expanded Timing Down now enters the real −50ms clickable button; left/right navigate timing controls.")
