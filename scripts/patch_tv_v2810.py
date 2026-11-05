#!/usr/bin/env python3
"""Melisma Universal v2.8.10: reclaim TV screen height when toolbar auto-hides.

Applies after v2.8.9 against Melisma upstream v2.11.0.
- TV TOP toolbar is now a temporary overlay of the content (including help
  captions); no permanent 94dp spacer when it hides.
- The main content, Cinema cover, and lyrics can occupy the full screen.
- TV D-pad focus controls, 5-second auto-hide, show/hide animation, clock
  clearance and source/help captions remain. Bottom toolbar and non-TV layouts
  retain their previous structure.
- No changes to artwork fades, Amazon Music auto-next, provider selection, or
  TV-only Settings filtering.
"""
from pathlib import Path
import sys

root=Path(sys.argv[1]) if len(sys.argv)>1 else Path("melisma")
f=root/"app/src/main/java/com/melisma/app/ui/PlayerScreen.kt"
s=f.read_text(encoding="utf-8")
def once(a,b,why):
    global s
    count=s.count(a)
    if count!=1:
        raise RuntimeError(f"{why}: expected 1 matching anchor, got {count}")
    s=s.replace(a,b,1)

# Hoist ViewControls into the else branch of the root Box so that it can be
# drawn in a full-screen overlay instead of consuming Column height.
once(
    """        } else {
            Column(Modifier.fillMaxSize()) {
                val controls: @Composable () -> Unit = {""",
    """        } else {
            val controls: @Composable () -> Unit = {""",
    "hoist reusable toolbar composable")

slot_start=s.index("                if (settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP) {",s.index("val controls: @Composable () -> Unit"))
content_start=s.index("                Box(\n                    Modifier.weight(1f).fillMaxWidth().then(",slot_start)
old_slot=s[slot_start:content_start]
assert "Modifier.height(94.dp)" in old_slot and "TvFadingTopControls" in old_slot, "unexpected top slot structure"
# For non-TV views the original toolbar remains in the normal layout.
# On Android TV we use only the system status bar inset (normally 0dp),
# eliminating the old permanently reserved 94dp toolbar band.
new_slot="""            Column(Modifier.fillMaxSize()) {
                if (!tvMode &&
                    settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP
                ) {
                    Box(Modifier.statusBarsPadding()) { controls() }
                } else {
                    Spacer(Modifier.statusBarsPadding())
                }

"""
s=s[:slot_start]+new_slot+s[content_start:]

# Insert the TOP TV toolbar as an overlay above the full-height content.
# This remains in the same "not popup" scope as controls(), and before
# the clock and dialog overlays in the root Box. Its own measured height
# includes the caption row, and disappears entirely when auto-hidden.
once(
    """            }
        }

        if (settings.showTvClock && !popup) {""",
    """            }

            if (tvMode &&
                settings.controlsPosition == com.melisma.app.settings.ControlsPosition.TOP
            ) {
                Box(
                    Modifier
                        .align(Alignment.TopStart)
                        .fillMaxWidth()
                        .statusBarsPadding()
                        .padding(end = if (settings.showTvClock) 105.dp else 0.dp),
                ) {
                    TvFadingTopControls(visible = toolbarVisible || showSettings) {
                        controls()
                    }
                }
            }
        }

        if (settings.showTvClock && !popup) {""",
    "floating TV toolbar overlay")

if ".height(94.dp)" in s:
    raise RuntimeError("old fixed top toolbar height still present")
f.write_text(s,encoding="utf-8")
print("v2.8.10: floating Android TV top toolbar reclaims 94dp during auto-hide; captions, clock and transitions preserved.")
