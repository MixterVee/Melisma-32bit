#!/usr/bin/env python3
"""TV v2.6.2: remove language toggles from top toolbar; use persistent Language Settings.

Apply after patch_tv_v261.py against upstream Melisma v2.11.0. Both romanization
and translations already use SharedPreferences via SettingsStore. This patch
removes the per-song toolbar controls, leaving Settings as the sole switch.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
pp = ui / "PlayerScreen.kt"
ss = ui / "SettingsSheet.kt"

def once(s, old, new, label):
    n = s.count(old)
    if n != 1:
        raise RuntimeError(f"{label}: expected 1 anchor, found {n}")
    return s.replace(old, new, 1)

player = pp.read_text(encoding="utf-8")
player = once(player,
    """                if (romanizationAvailable) {
                    LabelChip(
                        "文A",
                        "Toggle romanization",
                        settings.showRomanization,
                        accent,
                        onToggleRomanization,
                    )
                }
""",
    """                // Romanization (文A) is a saved global Language preference.
                // Never show a per-song switch in the top toolbar.
""",
    "remove Chinese/Latin (文A) toolbar button")
player = once(player,
    """                // Hidden when neither source could produce one — an English song read in
                // English has nothing to translate, and a button that cannot work is worse
                // than no button.
                if (translationAvailable) {
                    ActionChip(
                        AppIcons.Translate,
                        // Which source is in use is a Settings decision; here it is on or off.
                        when (settings.translationSource) {
                            TranslationSource.OFF -> "Show translation"
                            TranslationSource.PROVIDER -> "Translation from the source — tap to hide"
                            TranslationSource.DEVICE -> "Translation on this device — tap to hide"
                        },
                        settings.translationSource != TranslationSource.OFF,
                        accent,
                        onToggleTranslation,
                    )
                }
""",
    """                // Translation is also a saved global Language preference, not
                // a control that reappears whenever a music service changes tracks.
""",
    "remove translation toolbar toggle")
pp.write_text(player, encoding="utf-8")

settings = ss.read_text(encoding="utf-8")
settings = once(settings,
    """                    title = "Romanization",
                    subtitle = "Japanese, Korean, Chinese, Cyrillic and Greek in Latin letters",
                    checked = settings.showRomanization,""",
    """                    title = "Romanization (文A)",
                    subtitle = "Global. Off keeps original characters for all songs, even after restart.",
                    checked = settings.showRomanization,""",
    "clarify saved romanization preference")
settings = once(settings,
    """                    label = "Translation",
                    options = TranslationSource.entries.map { it to it.label },""",
    """                    label = "Lyric translation (global)",
                    options = TranslationSource.entries.map { it to it.label },""",
    "clarify saved lyric translation preference")
settings = once(settings,
    """                        TranslationSource.OFF -> "No translation under the lyrics."
""",
    """                        TranslationSource.OFF ->
                            "Off means no translated line under any song, even if the music " +
                                "service supplies Chinese translations. The choice stays off " +
                                "across songs and restarts."
""",
    "explain permanent OFF preference")
ss.write_text(settings, encoding="utf-8")
print("TV v2.6.2: removed 文A/translation toolbar icons; persistent global Language preferences remain the only switches.")
