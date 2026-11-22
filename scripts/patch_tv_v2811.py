#!/usr/bin/env python3
"""Melisma Universal v2.8.11: persistent TV per-song, per-source timing.
Apply after patch_tv_v2810.py against Melisma 2.11.0.
Per-track source choice was already saved by upstream. The new per-track/source
offsets save automatically; Reset to 0 deletes the correction for that source.
Changes are TV-only; phone global offset, Amazon Auto-Next and TV artwork unaffected.
"""
from pathlib import Path
import sys
root=Path(sys.argv[1]) if len(sys.argv)>1 else Path("melisma")
base=root/"app/src/main/java/com/melisma/app"
sp=base/"settings/SettingsStore.kt"
pp=base/"ui/PlayerScreen.kt"
test=root/"app/src/test/java/com/melisma/app/settings/SongOffsetSettingsTest.kt"

def once(src,old,new,why):
    n=src.count(old)
    if n!=1: raise RuntimeError(f"{why}: expected one anchor, got {n}")
    return src.replace(old,new,1)

s=sp.read_text(encoding="utf-8")
s=once(s,
    """    val sourceChoices: Map<String, String> = emptyMap(),
    val translationSource: TranslationSource = TranslationSource.OFF,""",
    """    val sourceChoices: Map<String, String> = emptyMap(),
    /** Local-only per-song/per-selected-source timing (also in settings backup). */
    val songOffsets: Map<String, Int> = emptyMap(),
    val translationSource: TranslationSource = TranslationSource.OFF,""",
    "Settings.songOffsets")
s=once(s,
    """        sourceChoices = prefs.getStringSet(KEY_SOURCE_CHOICES, null).orEmpty().mapNotNull { entry ->
            val split = entry.lastIndexOf(READING_SEPARATOR)
            if (split <= 0 || split == entry.lastIndex) null else entry.substring(0, split) to entry.substring(split + 1)
        }.toMap(),
        chineseReadings =""",
    """        sourceChoices = prefs.getStringSet(KEY_SOURCE_CHOICES, null).orEmpty().mapNotNull { entry ->
            val split = entry.lastIndexOf(READING_SEPARATOR)
            if (split <= 0 || split == entry.lastIndex) null else entry.substring(0, split) to entry.substring(split + 1)
        }.toMap(),
        songOffsets = prefs.getStringSet(KEY_SONG_OFFSETS, null).orEmpty().mapNotNull { entry ->
            val split = entry.lastIndexOf(READING_SEPARATOR)
            if (split <= 0) return@mapNotNull null
            val key = entry.substring(0, split)
            val value = entry.substring(split + 1).toIntOrNull()
            if (key.isBlank() || value == null || value !in SYNC_OFFSET_RANGE) null else key to value
        }.toMap(),
        chineseReadings =""",
    "Read persistent song offsets")
s=once(s,
    """    fun setSourceChoice(key: String, providerId: String?) = edit {
        val next = current.sourceChoices.toMutableMap()
        if (providerId == null) next.remove(key) else next[key] = providerId
        putStringSet(KEY_SOURCE_CHOICES, next.map { (k, v) -> "$k$READING_SEPARATOR$v" }.toSet())
    }

    fun setPrefetchNextTrack""",
    """    fun setSourceChoice(key: String, providerId: String?) = edit {
        val next = current.sourceChoices.toMutableMap()
        if (providerId == null) next.remove(key) else next[key] = providerId
        putStringSet(KEY_SOURCE_CHOICES, next.map { (k, v) -> "$k$READING_SEPARATOR$v" }.toSet())
    }

    /** Auto-selected lyrics and each explicitly chosen provider have independent corrections. */
    private fun songOffsetKey(trackKey: String, providerId: String?): String =
        "$trackKey$READING_SEPARATOR" + (providerId ?: "#automatic")

    fun songOffset(trackKey: String, providerId: String?): Int =
        current.songOffsets[songOffsetKey(trackKey, providerId)] ?: 0

    /** Zero forgets only this recording/provider correction; others remain. */
    fun setSongOffset(trackKey: String, providerId: String?, offsetMs: Int) {
        if (trackKey.isBlank()) return
        val ms = offsetMs.coerceIn(SYNC_OFFSET_RANGE)
        val key = songOffsetKey(trackKey, providerId)
        if (songOffset(trackKey, providerId) == ms) return
        edit {
            val next = current.songOffsets.toMutableMap()
            if (ms == 0) next.remove(key) else next[key] = ms
            putStringSet(KEY_SONG_OFFSETS,
                next.map { (k, v) -> "$k$READING_SEPARATOR$v" }.toSet())
        }
    }

    fun setPrefetchNextTrack""",
    "Per-source persistent read/write methods")
s=once(s,
    """        const val KEY_SOURCE_CHOICES = "source_choices"

        /**""",
    """        const val KEY_SOURCE_CHOICES = "source_choices"
        const val KEY_SONG_OFFSETS = "song_sync_offsets_v1"

        /**""",
    "New persistent preferences key")
sp.write_text(s,encoding="utf-8")

p=pp.read_text(encoding="utf-8")
p=once(p,
    """    val currentTrackKey = snapshot.track?.cacheKey
    LaunchedEffect(currentTrackKey, tvMode) {
        // A different song starts at zero; replay or seeking within the same one keeps it.
        // A temporary null media session is not interpreted as a new song.
        if (tvMode && currentTrackKey != null && lastNonNullTrackKey != currentTrackKey) {
            lastNonNullTrackKey = currentTrackKey
            tvSongOffsetMs = 0
            showSyncPanel = false
        }
    }""",
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
    "Load corrected per-song offset on song or source change")
p=once(p,
    """                onOffsetChange = { tvSongOffsetMs = it },
                onClose = {""",
    """                onOffsetChange = { value ->
                    tvSongOffsetMs = value
                    currentTrackKey?.let { key ->
                        container.settings.setSongOffset(
                            key, container.settings.current.sourceChoices[key], value
                        )
                    }
                },
                onClose = {""",
    "Save quick timing changes")
p=once(p,
    """                onTvOffsetChange = if (tvMode) { value: Int -> tvSongOffsetMs = value } else null,""",
    """                onTvOffsetChange = if (tvMode) { value: Int ->
                    tvSongOffsetMs = value
                    currentTrackKey?.let { key ->
                        container.settings.setSongOffset(
                            key, container.settings.current.sourceChoices[key], value
                        )
                    }
                } else null,""",
    "Save full TV Settings timing changes")
p=once(p,
    """                                                tvSongOffsetMs = 0
                                                container.lyrics.chooseSource(key, next)""",
    """                                                tvSongOffsetMs = container.settings.songOffset(key, next)
                                                container.lyrics.chooseSource(key, next)""",
    "Remember chosen lyrics provider offset")
p=once(p,
    """                                                tvSongOffsetMs = 0
                                                container.lyrics.chooseSource(key, first)""",
    """                                                tvSongOffsetMs = container.settings.songOffset(key, first)
                                                container.lyrics.chooseSource(key, first)""",
    "Remember initial lyrics provider offset")
pp.write_text(p,encoding="utf-8")

if test.exists(): raise RuntimeError("SongOffsetSettingsTest already exists")
test.write_text("""package com.melisma.app.settings

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class SongOffsetSettingsTest {
    private val context = ApplicationProvider.getApplicationContext<Context>()
    private fun fresh(): SettingsStore {
        context.getSharedPreferences("melisma", Context.MODE_PRIVATE).edit().clear().commit()
        return SettingsStore(context)
    }

    @Test fun repeatedSongRestoresProviderAndTimingAfterRestart() {
        val store = fresh()
        val track = "let's dance|david bowie|137"
        store.setSourceChoice(track, "lrclib")
        store.setSongOffset(track, "lrclib", 350)
        val restart = SettingsStore(context)
        assertEquals("lrclib", restart.current.sourceChoices[track])
        assertEquals(350, restart.songOffset(track, "lrclib"))
        assertEquals(0, restart.songOffset("other song|artist|100", "lrclib"))
    }

    @Test fun separateProvidersResetIndependently() {
        val store = fresh()
        val key = "track|artist|100"
        store.setSongOffset(key, "lrclib", 350)
        store.setSongOffset(key, "musixmatch", -250)
        store.setSongOffset(key, null, 100)
        assertEquals(350, store.songOffset(key, "lrclib"))
        assertEquals(-250, store.songOffset(key, "musixmatch"))
        assertEquals(100, store.songOffset(key, null))
        store.setSongOffset(key, "lrclib", 0)
        val reload = SettingsStore(context)
        assertEquals(0, reload.songOffset(key, "lrclib"))
        assertEquals(-250, reload.songOffset(key, "musixmatch"))
        assertEquals(100, reload.songOffset(key, null))
        assertEquals(2, reload.current.songOffsets.size)
    }

    @Test fun savedCorrectionsAreIncludedInSettingsBackup() {
        val store = fresh()
        val track = "song|artist|75"
        store.setSourceChoice(track, "lrclib")
        store.setSongOffset(track, "lrclib", 500)
        val exported = SettingsBackup.write(store.exportableSettings())
        val decoded = SettingsBackup.read(exported) as SettingsBackup.Restore.Ready
        val restored = fresh()
        restored.restore(decoded.settings, decoded.credentials)
        assertEquals("lrclib", restored.current.sourceChoices[track])
        assertEquals(500, restored.songOffset(track, "lrclib"))
    }
}
""",encoding="utf-8")
print("v2.8.11: local per-track/per-provider offsets persist, Reset forgets just one, backup carries corrections.")
