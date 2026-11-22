#!/usr/bin/env python3
"""Melisma Universal v2.8.12.3: recording-aware corrections and clean Cloud Sync UI.

Apply after patch_tv_v28122.py. The old song-level cloud entries remain stored
and are never deleted or auto-applied to a possibly different recording.
New corrections use stable recording keys scoped to the playing music service.
The normal lyrics search and shared lookup caches retain their upstream keys.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
base = root / "app/src/main/java/com/melisma/app"

def replace_once(s, old, new, description):
    n = s.count(old)
    if n != 1:
        raise RuntimeError(f"{description}: expected one match, found {n}")
    return s.replace(old, new, 1)

# A content cache identity is not a safe identity for user corrections. Prefer
# the player's stable track ID; otherwise include the album and duration.
# Prefix + SHA-256 keeps long/odd song names within Firestore rules key limits.
now = base / "media/NowPlaying.kt"
s = now.read_text(encoding="utf-8")
s = replace_once(s, "import android.os.SystemClock\n",
    "import android.os.SystemClock\nimport java.security.MessageDigest\n",
    "Recording identity hashing import")
s = replace_once(s,
    """    val isEmpty: Boolean get() = title.isBlank() && artist.isBlank()""",
    """    /**
     * Identity for saved lyrics-provider choices and timing offsets, NOT for
     * provider searches. In particular Amazon/Deezer never share corrections
     * merely because their titles and 2-second duration buckets match.
     * When a reliable service track ID is present, it also distinguishes
     * different releases within a service. Without one, metadata is a
     * best-effort fallback; two identically tagged releases may still collide.
     */
    val correctionKey: String
        get() {
            val origin = packageName.trim().lowercase().ifBlank { "unknown-player" }
            val recording = playerId?.toString()
                ?: spotifyTrackId?.let { "spotify:" + it }
                ?: ("metadata:" + cacheKey + "|" + album.trim().lowercase())
            val bytes = (origin + "\u001f" + recording).toByteArray(Charsets.UTF_8)
            val hash = MessageDigest.getInstance("SHA-256")
                .digest(bytes).joinToString("") { "%02x".format(it) }
            return "rec2:" + hash
        }

    val isEmpty: Boolean get() = title.isBlank() && artist.isBlank()""",
    "New recording-specific correction identity")
now.write_text(s, encoding="utf-8")

# Save and replay chosen lyrics providers by recording, not by shared cache key.
lyrics = base / "lyrics/LyricsRepository.kt"
s = lyrics.read_text(encoding="utf-8")
s = replace_once(s,
    """        if (request?.cacheIdentity() == currentRequest?.cacheIdentity() &&
            request?.isrc == currentRequest?.isrc
        ) {""",
    """        if (request?.cacheIdentity() == currentRequest?.cacheIdentity() &&
            request?.isrc == currentRequest?.isrc &&
            track?.correctionKey == currentTrack?.correctionKey
        ) {""",
    "Reload chosen provider on same-title player/recording change")
s = replace_once(s,
    """    private fun chosenProvider(key: String): LyricsProvider? {
        val settings = settingsStore.current
        val id = settings.sourceChoices[key] ?: return null
        return providersFor(settings, providers, localStore.id).firstOrNull { it.id == id }
    }""",
    """    private fun chosenProvider(key: String): LyricsProvider? {
        val track = currentTrack?.takeIf { it.cacheKey == key } ?: return null
        val settings = settingsStore.current
        val id = settings.sourceChoices[track.correctionKey] ?: return null
        return providersFor(settings, providers, localStore.id).firstOrNull { it.id == id }
    }""",
    "Replay recording-scoped source selection")
s = replace_once(s,
    """    fun chooseSource(trackKey: String, providerId: String?) {
        settingsStore.setSourceChoice(trackKey, providerId)
        val request = currentRequest?.takeIf { it.cacheIdentity() == trackKey } ?: return""",
    """    fun chooseSource(trackKey: String, providerId: String?) {
        val track = currentTrack?.takeIf { it.cacheKey == trackKey } ?: return
        val request = currentRequest?.takeIf { it.cacheIdentity() == trackKey } ?: return
        settingsStore.setSourceChoice(track.correctionKey, providerId)""",
    "Save provider under recording instead of shared lyrics cache key")
lyrics.write_text(s, encoding="utf-8")

# TV timing panel and quick lyrics source cycling must use the same identity.
player = base / "ui/PlayerScreen.kt"
s = player.read_text(encoding="utf-8")
s = replace_once(s,
    """    val currentTrackKey = snapshot.track?.cacheKey
    val currentSourceChoice = currentTrackKey?.let { settings.sourceChoices[it] }
    LaunchedEffect(currentTrackKey, tvMode, currentSourceChoice) {
        // Do not interpret temporary null media sessions as a new song.
        if (tvMode && currentTrackKey != null) {
            if (lastNonNullTrackKey != currentTrackKey) {
                lastNonNullTrackKey = currentTrackKey
                showSyncPanel = false
            }
            tvSongOffsetMs = container.settings.songOffset(currentTrackKey, currentSourceChoice)
        }
    }""",
    """    val currentTrackKey = snapshot.track?.cacheKey
    val currentCorrectionKey = snapshot.track?.correctionKey
    val currentSourceChoice = currentCorrectionKey?.let { settings.sourceChoices[it] }
    LaunchedEffect(currentCorrectionKey, tvMode, currentSourceChoice) {
        // A change of recording or music service resets the displayed offset,
        // even if the lyrics lookup/cache key remains identical.
        if (tvMode && currentCorrectionKey != null) {
            if (lastNonNullTrackKey != currentCorrectionKey) {
                lastNonNullTrackKey = currentCorrectionKey
                showSyncPanel = false
            }
            tvSongOffsetMs = container.settings.songOffset(currentCorrectionKey, currentSourceChoice)
        }
    }""",
    "TV timing restore keyed to exact playing recording")
old = """                    currentTrackKey?.let { key ->
                        container.settings.setSongOffset(
                            key, container.settings.current.sourceChoices[key], value
                        )
                    }"""
new = """                    currentCorrectionKey?.let { key ->
                        container.settings.setSongOffset(
                            key, container.settings.current.sourceChoices[key], value
                        )
                    }"""
if s.count(old) != 2:
    raise RuntimeError("TV timing save must have two recording-key call sites")
s = s.replace(old, new)
s = replace_once(s,
    """val saved = container.settings.current.sourceChoices[key]""",
    """val saved = container.media.snapshot.value.track?.correctionKey
                                                ?.let { container.settings.current.sourceChoices[it] }""",
    "Use recorded music source in quick lyrics cycle")
s = replace_once(s,
    """LaunchedEffect(snapshot.track?.cacheKey) { quickLyricsWrapToFirst = false }""",
    """LaunchedEffect(snapshot.track?.correctionKey) { quickLyricsWrapToFirst = false }""",
    "Reset quick-source cycle on recording change")
s = replace_once(s,
    """tvSongOffsetMs = container.settings.songOffset(key, next)""",
    """tvSongOffsetMs = container.media.snapshot.value.track?.correctionKey
                                                    ?.let { container.settings.songOffset(it, next) } ?: 0""",
    "Restore chosen provider timing for recording")
s = replace_once(s,
    """tvSongOffsetMs = container.settings.songOffset(key, first)""",
    """tvSongOffsetMs = container.media.snapshot.value.track?.correctionKey
                                                    ?.let { container.settings.songOffset(it, first) } ?: 0""",
    "Restore wrap-around provider timing for recording")
player.write_text(s, encoding="utf-8")

# Expose Firebase's persisted user state for a clear, compact connected view.
cloud = base / "cloud/CloudSyncManager.kt"
s = cloud.read_text(encoding="utf-8")
s = replace_once(s,
    """    val apiKey get() = CloudSyncDefaults.WEB_API_KEY""",
    """    val apiKey get() = CloudSyncDefaults.WEB_API_KEY
    val isSignedIn get() = auth?.currentUser != null
    val signedInEmail get() = auth?.currentUser?.email""",
    "Expose signed-in account in Cloud Sync UI")
cloud.write_text(s, encoding="utf-8")

sheet = base / "ui/SettingsSheet.kt"
s = sheet.read_text(encoding="utf-8")
start = s.index('            Section(\n                title = "Cloud sync",')
end = s.index('            Section(\n                title = "Storage and backup",', start)
before, section, after = s[:start], s[start:end], s[end:]
section = replace_once(section,
    """                Text(
                    "Firebase is ready for Melisma Universal. Sign in to sync song sources and " +
                        "timing corrections across TVs. Changes always remain saved locally.",
                    fontSize = 12.sp,
                    color = Color.White.copy(alpha = 0.68f),
                )""",
    """                Text(
                    "Song sources and timing corrections sync across TVs when signed in. " +
                        "Corrections are kept separately for each music app and recording.",
                    fontSize = 12.sp,
                    color = Color.White.copy(alpha = 0.68f),
                )""",
    "Simplify Cloud intro text")

start_form = section.index('                OutlinedTextField(\n                    value = loginEmail,')
end_form = section.index('                Help(\n', start_form)
section = section[:start_form] + """                if (sync.isSignedIn) {
                    Text(
                        "Signed in as: " + (sync.signedInEmail ?: "Firebase account"),
                        fontSize = 14.sp,
                        color = Color.White,
                    )
                    Spacer(Modifier.height(8.dp))
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Button(
                            modifier = Modifier
                                .then(firstTvSettingFocus("cloud", "Account email"))
                                .tvFocusOutline(),
                            onClick = { sync.syncNow() },
                        ) { Text("Sync now") }
                        Button(
                            modifier = Modifier.tvFocusOutline(),
                            onClick = { sync.signOut() },
                        ) { Text("Sign out") }
                    }
                } else {
                    OutlinedTextField(
                        value = loginEmail,
                        onValueChange = { loginEmail = it },
                        label = { Text("Account email") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                            .then(firstTvSettingFocus("cloud", "Account email"))
                            .tvFocusOutline(),
                    )
                    OutlinedTextField(
                        value = loginPassword,
                        onValueChange = { loginPassword = it },
                        label = { Text("Account password") },
                        visualTransformation = PasswordVisualTransformation(),
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().tvFocusOutline(),
                    )
                    Button(
                        modifier = Modifier.tvFocusOutline(),
                        onClick = {
                            sync.signIn(loginEmail, loginPassword)
                            loginPassword = ""
                        },
                    ) { Text("Sign in") }
                }
""" + section[end_form:]
section = replace_once(section,
    """                    "Only preferred lyrics providers and timing corrections sync. Lyrics, music, " +
                        "API credentials and other settings never upload. Use the same Firebase " +
                        "account on each device; private Firestore rules protect corrections."
                )""",
    """                    "Only preferred lyrics providers and timing corrections sync. Lyrics, music, " +
                        "API credentials and other settings never upload. Use the same Firebase " +
                        "account on each TV. Older global corrections remain stored in your account, " +
                        "but are not automatically reused across music apps; reselect an older " +
                        "correction once to save it for the current recording."
                )""",
    "Explain private sync and safe legacy-record handling")
assert section.count(".tvFocusOutline()") == 5
assert section.count('firstTvSettingFocus("cloud", "Account email")') == 2
assert "Save Firebase configuration" not in section
s = before + section + after
sheet.write_text(s, encoding="utf-8")

test_dir = root / "app/src/test/java/com/melisma/app/media"
test = test_dir / "RecordingCorrectionKeyTest.kt"
if test.exists():
    raise RuntimeError("RecordingCorrectionKeyTest.kt already exists")
test.write_text("""package com.melisma.app.media

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

class RecordingCorrectionKeyTest {
    private fun track(app: String, id: String? = null, album: String = "Album") =
        TrackInfo(
            title = "Same Song",
            artist = "Same Artist",
            album = album,
            durationMs = 245_000L,
            mediaId = id,
            packageName = app,
        )

    @Test fun separateAppsDoNotShareCorrectionsEvenWithSameMetadata() {
        val amazon = track("com.amazon.mp3")
        val deezer = track("deezer.android.app")
        assertEquals(amazon.cacheKey, deezer.cacheKey)
        assertNotEquals(amazon.correctionKey, deezer.correctionKey)
    }

    @Test fun distinctReleaseIdsWithinOneServiceStaySeparate() {
        val amazonA = track("com.amazon.mp3", id = "B0ABCD1234")
        val amazonB = track("com.amazon.mp3", id = "B0EFGH5678")
        assertEquals(amazonA.cacheKey, amazonB.cacheKey)
        assertNotEquals(amazonA.correctionKey, amazonB.correctionKey)
    }

    @Test fun sameServiceAndTrackIdAreStableAcrossDevices() {
        val first = track("deezer.android.app", id = "0.2981739661")
        val second = track("deezer.android.app", id = "3.2981739661")
        assertEquals(first.correctionKey, second.correctionKey)
        assertEquals(69, first.correctionKey.length)
    }

    @Test fun albumDistinguishesMetadataOnlyReleases() {
        assertNotEquals(
            track("com.amazon.mp3", album = "Studio").correctionKey,
            track("com.amazon.mp3", album = "Live").correctionKey,
        )
    }

    @Test fun unrelatedFallbackMetadataCannotUseLegacyKey() {
        val newKey = track("com.amazon.mp3").correctionKey
        assertNotEquals("same song|same artist|122", newKey)
    }
}
""", encoding="utf-8")
print("v2.8.12.3: recording-aware source/timing keys, legacy cloud entries retained, connected account UI.")
