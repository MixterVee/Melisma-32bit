#!/usr/bin/env python3
"""Melisma Universal v2.8.2: optional 500ms Amazon auto-next on 32-bit Android TV only.

Apply after patch_tv_v281.py against Melisma upstream v2.11.0.
Safety: opt-in, Android TV only, 32-bit operating system only, Amazon Music
sessions only, only while playing with supported Next transport, one Next per
track. Does nothing on 64-bit OS even if running a 32-bit process.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
base = root / "app/src/main/java/com/melisma/app"
player_file = base / "ui/PlayerScreen.kt"
store_file = base / "settings/SettingsStore.kt"
sheet_file = base / "ui/SettingsSheet.kt"

def once(s, a, b, label):
    n = s.count(a)
    if n != 1:
        raise RuntimeError(f"{label}: wanted one exact anchor, found {n}")
    return s.replace(a, b, 1)

store = store_file.read_text(encoding="utf-8")
store = once(store,
    """    val tvAutoHideAllControls: Boolean = false,""",
    """    val tvAutoHideAllControls: Boolean = false,
    /** Experimental auto-next for Amazon Music on 32-bit Android TV; off by default. */
    val amazonAutoNext32Bit: Boolean = false,""",
    "32-bit auto-next setting state")
store = once(store,
    """        tvAutoHideAllControls = prefs.getBoolean(KEY_TV_HIDE_CHROME, false),""",
    """        tvAutoHideAllControls = prefs.getBoolean(KEY_TV_HIDE_CHROME, false),
        amazonAutoNext32Bit = prefs.getBoolean(KEY_AMAZON_AUTO_NEXT_32, false),""",
    "32-bit auto-next persisted setting")
store = once(store,
    """    fun setTvAutoHideAllControls(value: Boolean) = edit { putBoolean(KEY_TV_HIDE_CHROME, value) }""",
    """    fun setTvAutoHideAllControls(value: Boolean) = edit { putBoolean(KEY_TV_HIDE_CHROME, value) }

    fun setAmazonAutoNext32Bit(value: Boolean) =
        edit { putBoolean(KEY_AMAZON_AUTO_NEXT_32, value) }""",
    "32-bit auto-next setter")
store = once(store,
    """        const val KEY_TV_HIDE_CHROME = "tv_hide_all_controls" """.rstrip(),
    """        const val KEY_TV_HIDE_CHROME = "tv_hide_all_controls"
        const val KEY_AMAZON_AUTO_NEXT_32 = "amazon_auto_next_32bit" """.rstrip(),
    "32-bit auto-next preference key")
store_file.write_text(store, encoding="utf-8")

sheet = sheet_file.read_text(encoding="utf-8")
sheet = once(sheet,
    """                ToggleRow(
                    title = "Auto-hide all playback controls",
                    subtitle = "On TV, hide top and bottom controls after 5s; press Up, OK or Back to reveal",
                    checked = settings.tvAutoHideAllControls,
                    accent = accent,
                    onCheckedChange = { store.setTvAutoHideAllControls(it) },
                )""",
    """                ToggleRow(
                    title = "Auto-hide all playback controls",
                    subtitle = "On TV, hide top and bottom controls after 5s; press Up, OK or Back to reveal",
                    checked = settings.tvAutoHideAllControls,
                    accent = accent,
                    onCheckedChange = { store.setTvAutoHideAllControls(it) },
                )

                // SUPPORTED_64_BIT_ABIS describes OS/runtime capabilities,
                // not this universal APK's current process architecture.
                if (android.os.Build.SUPPORTED_32_BIT_ABIS.isNotEmpty() &&
                    android.os.Build.SUPPORTED_64_BIT_ABIS.isEmpty() &&
                    androidx.compose.ui.platform.LocalContext.current.packageManager.hasSystemFeature(
                        android.content.pm.PackageManager.FEATURE_LEANBACK
                    )
                ) {
                    ToggleRow(
                        title = "Amazon Music auto-next (32-bit TV test)",
                        subtitle = "Experimental: send Next at 0.5s remaining. Amazon Music only; Off by default.",
                        checked = settings.amazonAutoNext32Bit,
                        accent = accent,
                        onCheckedChange = { store.setAmazonAutoNext32Bit(it) },
                    )
                }""",
    "Auto-next switch shown only on 32-bit Android TV")
sheet_file.write_text(sheet, encoding="utf-8")

player = player_file.read_text(encoding="utf-8")
# TV mode is defined by earlier patches, and snapshot comes from the existing
# StateFlow. Read the latest value in the polling coroutine.
player = once(player,
    """    var askExitConfirmation by remember { mutableStateOf(false) }""",
    """    var askExitConfirmation by remember { mutableStateOf(false) }

    LaunchedEffect(settings.amazonAutoNext32Bit, tvMode, popup) {
        val only32BitAndroidTv =
            tvMode &&
            android.os.Build.SUPPORTED_32_BIT_ABIS.isNotEmpty() &&
            android.os.Build.SUPPORTED_64_BIT_ABIS.isEmpty()

        if (!only32BitAndroidTv || !settings.amazonAutoNext32Bit || popup) {
            return@LaunchedEffect
        }

        var lastTrackSignature: String? = null
        var lastProgressMs = -1L
        var nextAlreadySent = false
        while (true) {
            val playing = container.media.snapshot.value
            val track = playing.track
            val pkg = playing.sourcePackage
            val isAmazonMusic =
                pkg?.equals("com.amazon.mp3", ignoreCase = true) == true ||
                pkg?.equals("com.amazon.music", ignoreCase = true) == true ||
                playing.sourceLabel?.contains("Amazon Music", ignoreCase = true) == true

            if (track != null && isAmazonMusic && playing.playback.isPlaying &&
                playing.transport.skipNext) {
                val duration = if (playing.playback.durationMs > 0L) {
                    playing.playback.durationMs
                } else {
                    track.durationMs
                }
                // Do not auto-advance streams, unknown-duration material or
                // brief jingles. The playhead already uses elapsedRealtime.
                if (duration >= 15_000L) {
                    val current = playing.playback.currentMs()
                    val signature = (pkg ?: "") + ":" + (track.mediaId ?: track.cacheKey)

                    if (signature != lastTrackSignature ||
                        (lastProgressMs >= 0L &&
                            current + 2_000L < lastProgressMs &&
                            current + 3_000L < duration)) {
                        lastTrackSignature = signature
                        nextAlreadySent = false
                    }
                    lastProgressMs = current

                    // Trigger only before the end while Amazon's session
                    // is still controllable; never send after it disappears.
                    val remaining = duration - current
                    if (!nextAlreadySent && remaining in 1L..500L) {
                        nextAlreadySent = true
                        container.media.skipNext()
                    }
                }
            }
            delay(75L)
        }
    }""",
    "opt-in Android TV 32-bit Amazon near-end auto-next loop")
player_file.write_text(player, encoding="utf-8")

print("Melisma v2.8.2: opt-in Amazon Music next at <=500ms, 75ms checks; never on a 64-bit OS.")
