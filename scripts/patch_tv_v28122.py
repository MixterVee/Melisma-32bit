#!/usr/bin/env python3
"""Melisma Universal v2.8.12.2: preconfigure Firebase for all Android TV devices.

Runs after patch_tv_v28121.py. The Firebase client API key is provided only
through a GitHub Actions repository secret (MELISMA_FIREBASE_API_KEY). The
generated Kotlin config is created in the disposable upstream build checkout;
nothing containing the key is committed to the public automation repository.
Firebase API keys are client identifiers, not authorization credentials.
Firestore security rules and Firebase Authentication secure user data.
"""
from pathlib import Path
import os
import re
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
key = os.environ.get("MELISMA_FIREBASE_API_KEY", "").strip()
if not re.fullmatch(r"AIza[A-Za-z0-9_-]{20,}", key):
    raise RuntimeError(
        "MELISMA_FIREBASE_API_KEY is missing or invalid. "
        "Add the Firebase current_key as a GitHub Actions repository secret."
    )

base = root / "app/src/main/java/com/melisma/app"
cloud = base / "cloud"
manager = cloud / "CloudSyncManager.kt"
settings = base / "ui/SettingsSheet.kt"

def replace_once(s, old, new, label):
    count = s.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, got {count}")
    return s.replace(old, new, 1)

# The generated source file lives in the temporary GitHub Actions build tree.
defaults = cloud / "CloudSyncDefaults.kt"
if defaults.exists():
    raise RuntimeError("CloudSyncDefaults already exists; aborting")
defaults.write_text(
    "package com.melisma.app.cloud\n\n"
    "/** Generated at build time from repository configuration. */\n"
    "internal object CloudSyncDefaults {\n"
    '    const val PROJECT_ID = "melisma-universal"\n'
    '    const val APP_ID = "1:802566187243:android:fb6e7704d8a64d0670f6ae"\n'
    f'    const val WEB_API_KEY = "{key}"\n'
    "}\n",
    encoding="utf-8",
)

# Use built-in app identity even if older installs retained manual values.
s = manager.read_text(encoding="utf-8")
s = replace_once(s,
    '    val projectId get() = prefs.getString("project_id", "").orEmpty()\n'
    '    val appId get() = prefs.getString("app_id", "").orEmpty()\n'
    '    val apiKey get() = prefs.getString("api_key", "").orEmpty()',
    '    val projectId get() = CloudSyncDefaults.PROJECT_ID\n'
    '    val appId get() = CloudSyncDefaults.APP_ID\n'
    '    val apiKey get() = CloudSyncDefaults.WEB_API_KEY',
    "Use built-in Firebase config")
start = s.index("    /** Firebase configuration identifiers are public, not passwords. */")
end = s.index("    private fun initializeFirebase()", start)
s = s[:start] + s[end:]  # Remove obsolete manual configure API.
s = replace_once(s,
    '"Save Firebase project settings first"',
    '"Firebase configuration is unavailable"',
    "Sign-in error text")
manager.write_text(s, encoding="utf-8")

s = settings.read_text(encoding="utf-8")
s = replace_once(s,
    '"Cloud sync" -> "cloud" to "Firebase Project ID"',
    '"Cloud sync" -> "cloud" to "Account email"',
    "Cloud first D-pad target")
start = s.index('            Section(\n                title = "Cloud sync",')
end = s.index('            Section(\n                title = "Storage and backup",', start)
before, section, after = s[:start], s[start:end], s[end:]
section = replace_once(section,
    '                var projectId by remember { mutableStateOf(sync.projectId) }\n'
    '                var androidAppId by remember { mutableStateOf(sync.appId) }\n'
    '                var webApiKey by remember { mutableStateOf(sync.apiKey) }\n',
    '',
    "Remove manual Firebase configuration state")
section = replace_once(section,
    '"Optional. Melisma always saves corrections locally, even when cloud sync is off. " +\n'
    '                        "Configure your own Firebase project and sign in to the SAME account on each TV.",',
    '"Firebase is ready for Melisma Universal. Sign in to sync song sources and " +\n'
    '                        "timing corrections across TVs. Changes always remain saved locally.",',
    "Explain automatic configuration")
start_form = section.index('                OutlinedTextField(\n                    value = projectId,')
end_form = section.index('                OutlinedTextField(\n                    value = loginEmail,', start_form)
section = section[:start_form] + section[end_form:]
section = replace_once(section,
    '''                OutlinedTextField(
                    value = loginEmail,
                    onValueChange = { loginEmail = it },
                    label = { Text("Account email") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth().tvFocusOutline(),
                )''',
    '''                OutlinedTextField(
                    value = loginEmail,
                    onValueChange = { loginEmail = it },
                    label = { Text("Account email") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                        .then(firstTvSettingFocus("cloud", "Account email"))
                        .tvFocusOutline(),
                )''',
    "Restore first focus on account email")
section = replace_once(section,
    '"API credentials and other settings never upload. Firebase email/password " +\n'
    '                        "authentication and private Firestore access rules must be configured " +\n'
    '                        "first. Use the same account on each device."',
    '"API credentials and other settings never upload. Use the same Firebase " +\n'
    '                        "account on each device; private Firestore rules protect corrections."',
    "Refresh Cloud help text")
assert "Save Firebase configuration" not in section
assert "Firebase Project ID" not in section
assert section.count(".tvFocusOutline()") == 5
s = before + section + after
settings.write_text(s, encoding="utf-8")
print("v2.8.12.2: Firebase identity preconfigured and TV Cloud Sync sign-in simplified.")
