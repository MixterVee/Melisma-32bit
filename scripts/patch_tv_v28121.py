#!/usr/bin/env python3
"""Melisma Universal v2.8.12.1: TV D-pad focus for Cloud Sync settings.

The audited TV Settings navigation routes Down from any expanded section
header into an attached FocusRequester. Cloud Sync v2.8.12 had no attached
first control, trapping remote focus on the heading. This fix registers the
first text input and adds existing TV focus/auto-scroll styling to all
Cloud Sync fields/buttons. No Firebase, phone or audio logic is changed.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
file = root / "app/src/main/java/com/melisma/app/ui/SettingsSheet.kt"
s = file.read_text(encoding="utf-8")

def replace_once(src, old, new, label):
    n = src.count(old)
    if n != 1:
        raise RuntimeError(f"{label}: expected one anchor, found {n}")
    return src.replace(old, new, 1)

s = replace_once(s,
    '''        "Storage and backup" ->
            "action" to if (label == "Cache cleared") "Cache cleared"
                        else "Clear the lyrics cache"
        else -> null''',
    '''        "Storage and backup" ->
            "action" to if (label == "Cache cleared") "Cache cleared"
                        else "Clear the lyrics cache"
        "Cloud sync" -> "cloud" to "Firebase Project ID"
        else -> null''',
    "Cloud section focus routing")

start = s.index('''            Section(
                title = "Cloud sync",''')
end = s.index('''            Section(
                title = "Storage and backup",''', start)
before, section, after = s[:start], s[start:end], s[end:]

section = replace_once(section,
    '''                    label = { Text("Firebase Project ID") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),''',
    '''                    label = { Text("Firebase Project ID") },
                    singleLine = true,
                    // The TV section header's Down action requests this focus node.
                    modifier = Modifier.fillMaxWidth()
                        .then(firstTvSettingFocus("cloud", "Firebase Project ID"))
                        .tvFocusOutline(),''',
    "Cloud Sync first focus target")

old = '''                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),'''
if section.count(old) != 4:
    raise RuntimeError(f"Other Cloud fields: expected four, got {section.count(old)}")
section = section.replace(old,
    '''                    singleLine = true,
                    modifier = Modifier.fillMaxWidth().tvFocusOutline(),''')

section = replace_once(section,
    '''                Button(onClick = { sync.configure(projectId, androidAppId, webApiKey) }) {''',
    '''                Button(
                    modifier = Modifier.tvFocusOutline(),
                    onClick = { sync.configure(projectId, androidAppId, webApiKey) },
                ) {''',
    "Save Firebase configuration TV button")

section = replace_once(section,
    '''                    Button(onClick = {
                        sync.signIn(loginEmail, loginPassword)
                        loginPassword = ""
                    }) { Text("Sign in") }
                    Button(onClick = { sync.syncNow() }) { Text("Sync now") }
                    Button(onClick = { sync.signOut() }) { Text("Sign out") }''',
    '''                    Button(
                        modifier = Modifier.tvFocusOutline(),
                        onClick = {
                            sync.signIn(loginEmail, loginPassword)
                            loginPassword = ""
                        },
                    ) { Text("Sign in") }
                    Button(
                        modifier = Modifier.tvFocusOutline(),
                        onClick = { sync.syncNow() },
                    ) { Text("Sync now") }
                    Button(
                        modifier = Modifier.tvFocusOutline(),
                        onClick = { sync.signOut() },
                    ) { Text("Sign out") }''',
    "Cloud action buttons TV focus and auto scroll")

s = before + section + after
file.write_text(s, encoding="utf-8")
print("Melisma v2.8.12.1 Cloud Sync TV remote focus route, focus outlines and scroll fixed.")
