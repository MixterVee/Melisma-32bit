#!/usr/bin/env python3
"""Melisma Universal v2.8.3: advance Amazon Music at 1 second instead of 0.5.

Runs after patch_tv_v282.py; changes only the timing threshold and its label.
The 32-bit Android TV guard, Amazon-only guard, opt-in preference and one-shot
protection remain unchanged.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
base = root / "app/src/main/java/com/melisma/app"
player_file = base / "ui/PlayerScreen.kt"
sheet_file = base / "ui/SettingsSheet.kt"

def replace_once(text, original, replacement, label):
    count = text.count(original)
    if count != 1:
        raise RuntimeError(f"{label}: expected one matching anchor, found {count}")
    return text.replace(original, replacement, 1)

player = player_file.read_text(encoding="utf-8")
player = replace_once(
    player,
    "if (!nextAlreadySent && remaining in 1L..500L) {",
    "if (!nextAlreadySent && remaining in 1L..1_000L) {",
    "change auto-next threshold to 1000ms",
)
player_file.write_text(player, encoding="utf-8")

sheet = sheet_file.read_text(encoding="utf-8")
sheet = replace_once(
    sheet,
    'subtitle = "Experimental: send Next at 0.5s remaining. Amazon Music only; Off by default.",',
    'subtitle = "Experimental: send Next at 1.0s remaining. Amazon Music only; Off by default.",',
    "update on-screen auto-next explanation",
)
sheet_file.write_text(sheet, encoding="utf-8")

print("Melisma v2.8.3: Amazon Music auto-next 1000ms threshold on 32-bit Android TV only.")
