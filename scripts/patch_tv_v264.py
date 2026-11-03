#!/usr/bin/env python3
"""TV v2.6.4: restore Language section remote Down navigation.

v2.6.2 renamed the first Language ToggleRow to "Romanization (文A)", but
firstTvSettingFocus still matched "Romanization". With a FocusRequester
attached to no node, the expanded Language header intercepted Down without
entering its controls. Match the renamed persistent setting explicitly.
Apply after patch_tv_v263.py against Melisma v2.11.0.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
path = root / "app/src/main/java/com/melisma/app/ui/SettingsSheet.kt"
source = path.read_text(encoding="utf-8")
old = '"Language" -> "toggle" to "Romanization"'
new = '"Language" -> "toggle" to "Romanization (文A)"'
if source.count(old) != 1:
    raise RuntimeError("Expected old Language first-control focus mapping exactly once")
if source.count('title = "Romanization (文A)"') != 1:
    raise RuntimeError("Expected the actual Language first ToggleRow title exactly once")
if 'Modifier.focusRequester(entry.requester)' not in source:
    raise RuntimeError("TV section first-control focus code is missing")
updated = source.replace(old, new, 1)
path.write_text(updated, encoding="utf-8")
print("TV v2.6.4: Language Down now focuses Romanization (文A) instead of a missing target.")
