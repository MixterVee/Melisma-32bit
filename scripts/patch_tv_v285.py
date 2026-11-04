#!/usr/bin/env python3
"""Melisma Universal v2.8.5: TV Next Lyrics source shortcut and Experimental label.

Apply after patch_tv_v284.py on upstream v2.11.0.
- Remote-focusable "L↻" chip beside ±ms cycles this song's available lyrics
  providers; skips known NONE/FAILED responses, leaves global order untouched.
- Uses existing chooseSource() and its per-song stored override. Returning to
  Automatic (after the last available provider) restores normal ranking.
- Keep already shown lyrics on screen while fetching alternatives; track
  changes still use their original Loading state.
- Rename 32-bit Amazon auto-next as Experimental with Prime skip-limit warning.
The 1.0-second threshold, 32-bit guard, playback and background art unchanged.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
base = root / "app/src/main/java/com/melisma/app"
player_file = base / "ui/PlayerScreen.kt"
repo_file = base / "lyrics/LyricsRepository.kt"
settings_file = base / "ui/SettingsSheet.kt"

def once(s, old, new, label):
    count = s.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected 1 anchor, got {count}")
    return s.replace(old, new, 1)

player = player_file.read_text(encoding="utf-8")
player = once(player,
    """                        onOpenTvSync = if (tvMode) {
                            {
                                showSyncPanel = true
                                toolbarVisible = true
                                toolbarActivity++
                            }
                        } else null,
                        romanizationAvailable = demoMode ||""",
    """                        onOpenTvSync = if (tvMode) {
                            {
                                showSyncPanel = true
                                toolbarVisible = true
                                toolbarActivity++
                            }
                        } else null,
                        onNextLyrics = if (tvMode && !demoMode && snapshot.track != null) {
                            {
                                scope.launch {
                                    val key = container.media.snapshot.value.track?.cacheKey
                                    if (key != null) {
                                        val options = container.lyrics.sourceOptions()
                                            .filter { option ->
                                                // Local-file lyrics outrank providers; do
                                                // not offer an empty local lookup as an alternate.
                                                option.id != "local" &&
                                                    option.outcome != com.melisma.app.lyrics.LyricsCache.Outcome.NONE &&
                                                    option.outcome != com.melisma.app.lyrics.LyricsCache.Outcome.FAILED
                                            }
                                        // A track may end while the cache is being read.
                                        if (container.media.snapshot.value.track?.cacheKey == key) {
                                            val showing = (container.lyrics.state.value
                                                as? LyricsState.Loaded)?.document?.providerId
                                            val saved = container.settings.current.sourceChoices[key]
                                            val current = saved ?: showing
                                            val index = options.indexOfFirst { it.id == current }
                                            val next = when {
                                                options.isEmpty() -> null
                                                index < 0 -> options.first().id
                                                index == options.lastIndex -> null // Automatic
                                                else -> options[index + 1].id
                                            }
                                            if (options.isEmpty()) {
                                                android.widget.Toast.makeText(
                                                    context,
                                                    "No alternative lyrics sources available",
                                                    android.widget.Toast.LENGTH_SHORT,
                                                ).show()
                                            } else if (saved != next) {
                                                // A manually adjusted offset for the previous
                                                // provider would distort the next one's sync.
                                                tvSongOffsetMs = 0
                                                container.lyrics.chooseSource(key, next)
                                            } else {
                                                // Automatic may display the last source;
                                                // wrap around rather than do nothing.
                                                val first = options.first().id
                                                tvSongOffsetMs = 0
                                                container.lyrics.chooseSource(key, first)
                                            }
                                        }
                                    }
                                }
                            }
                        } else null,
                        romanizationAvailable = demoMode ||""",
    "TV-only quick source cycling callback")

player = once(player,
    """    onOpenTvSync: (() -> Unit)?,
    romanizationAvailable: Boolean,""",
    """    onOpenTvSync: (() -> Unit)?,
    onNextLyrics: (() -> Unit)?,
    romanizationAvailable: Boolean,""",
    "Next Lyrics toolbar parameter")

player = once(player,
    """                // Romanization (文A) is a saved global Language preference.
                // Never show a per-song switch in the top toolbar.
""",
    """                // Romanization (文A) is a saved global Language preference.
                // Never show a per-song switch in the top toolbar.
                if (onNextLyrics != null) {
                    LabelChip(
                        "L↻",
                        "Next Lyrics — switch to another source for this song",
                        false,
                        Color(0xFF00D5FF),
                        onNextLyrics,
                    )
                }
""",
    "Next Lyrics remote-focusable chip beside existing ±ms")

player_file.write_text(player, encoding="utf-8")

# The per-song source switch already uses a cancellable fetchJob and emits a
# new Base.Ready or a fallback. Removing the intermediate Base.Loading state
# keeps the *same song's* current lyrics visible during that lookup. A real
# track change still explicitly uses Base.Loading in setTrack.
lyrics = repo_file.read_text(encoding="utf-8")
lyrics = once(lyrics,
    """    fun chooseSource(trackKey: String, providerId: String?) {
        settingsStore.setSourceChoice(trackKey, providerId)
        val request = currentRequest?.takeIf { it.cacheIdentity() == trackKey } ?: return
        fetchJob?.cancel()
        derived.clear()
        romanizable.clear()
        languages.clear()
        base.value = Base.Loading
        fetchJob = scope.launch {
            fetchBase(request)
            announce()
        }
    }""",
    """    fun chooseSource(trackKey: String, providerId: String?) {
        settingsStore.setSourceChoice(trackKey, providerId)
        val request = currentRequest?.takeIf { it.cacheIdentity() == trackKey } ?: return
        fetchJob?.cancel()
        derived.clear()
        romanizable.clear()
        languages.clear()
        // Keep previous lyrics visible *for this same track* while switching
        // sources. fetchBase() replaces them when the alternative resolves.
        // A real track change still clears to Base.Loading in setTrack().
        fetchJob = scope.launch {
            fetchBase(request)
            announce()
        }
    }""",
    "keep current lyrics while next source loads")
repo_file.write_text(lyrics, encoding="utf-8")

sheet = settings_file.read_text(encoding="utf-8")
sheet = once(sheet,
    'title = "Amazon Music auto-next (32-bit TV test)",',
    'title = "Amazon Music Auto-Next (Experimental)",',
    "Amazon TV experiment label")
sheet = once(sheet,
    'subtitle = "Experimental: send Next at 1.0s remaining. Amazon Music only; Off by default.",',
    'subtitle = "32-bit TV only: Next 1 second before end. Prime skip-limit warnings possible. Off by default.",',
    "Amazon Prime skip warning")
settings_file.write_text(sheet, encoding="utf-8")

print("Melisma v2.8.5: TV L↻ source cycling, cached misses skipped, lyrics held during lookup; Amazon Auto-Next Experimental.")
