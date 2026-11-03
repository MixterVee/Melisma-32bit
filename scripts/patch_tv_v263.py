#!/usr/bin/env python3
"""TV v2.6.3: fix one-way Cover Blur D-pad slider adjustment.

Compose's continuous Slider moves by fractional values over range 0..67.
Truncating with toInt() means a right press (e.g. 24 -> 24.67)
sticks at 24 while a left press (24 -> 23.33) drops to 23.
Give Cover Blur exact integer steps and round before persisting.
Run after patch_tv_v262.py against Melisma v2.11.0.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
p = root / "app/src/main/java/com/melisma/app/ui/SettingsSheet.kt"
s = p.read_text(encoding="utf-8")

def once(old, new, label):
    global s
    count = s.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count}")
    s = s.replace(old, new, 1)

once(
    "import androidx.compose.ui.unit.sp\n",
    "import androidx.compose.ui.unit.sp\nimport kotlin.math.roundToInt\n",
    "integer rounding import",
)

once(
    """                        label = "Cover blur",
                        value = settings.backgroundBlur.toFloat(),
                        range = 0f..67f,
                        display = "$\{settings.backgroundBlur}px",
                        accent = accent,
                        onChange = { store.setBackgroundBlur(it.toInt()) },""".replace("\\",""),
    """                        label = "Cover blur",
                        value = settings.backgroundBlur.toFloat(),
                        range = 0f..67f,
                        display = "$\{settings.backgroundBlur}px",
                        accent = accent,
                        steps = 66, // 67 equal segments = exact integer values 0 through 67.
                        onChange = { store.setBackgroundBlur(it.roundToInt()) },""".replace("\\",""),
    "Cover Blur exact steps and symmetric rounding",
)

once(
    """    onChange: (Float) -> Unit,
    /** When set, tapping the value types one in, for a slider too coarse to land on it. */
    typed: TypedEntry? = null,""",
    """    onChange: (Float) -> Unit,
    steps: Int = 0,
    /** When set, tapping the value types one in, for a slider too coarse to land on it. */
    typed: TypedEntry? = null,""",
    "SliderRow optional steps parameter",
)

once(
    """            modifier = Modifier
                .then(firstTvSettingFocus("slider", label))
                .tvSettingsSliderKeys()
                .tvFocusOutline(),
            valueRange = range,
            colors = SliderDefaults.colors(""",
    """            modifier = Modifier
                .then(firstTvSettingFocus("slider", label))
                .tvSettingsSliderKeys()
                .tvFocusOutline(),
            valueRange = range,
            steps = steps,
            colors = SliderDefaults.colors(""",
    "forward exact-step count to Material3 Slider",
)
p.write_text(s, encoding="utf-8")
print("TV v2.6.3: Cover blur now increments/decrements by exactly 1 via D-pad without one-way truncation.")
