#!/usr/bin/env python3
"""Melisma Universal v2.7.3 — retain outgoing art through lyrics loading
and repair Players-to-follow saved-list XML encoding.

Apply after patch_tv_v272.py against upstream Melisma v2.11.0.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
player_path = ui / "PlayerScreen.kt"
store_path = root / "app/src/main/java/com/melisma/app/settings/SettingsStore.kt"

def once(text: str, before: str, after: str, label: str) -> str:
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one matching anchor, got {count}")
    return text.replace(before, after, 1)

player = player_path.read_text(encoding="utf-8")
player = once(player,
    """    // A media session can temporarily publish no cover while switching songs.
    // Keep the last real album art for a grace period instead of abruptly inserting
    // the synthetic fallback between Amazon Music/Deezer/Spotify tracks.
    var heldArtwork by remember { mutableStateOf<android.graphics.Bitmap?>(null) }
    LaunchedEffect(artwork) {
        if (artwork != null) {
            heldArtwork = artwork
        } else {
            delay(2_500)
            heldArtwork = null
        }
    }
    val backgroundArtwork = artwork ?: heldArtwork ?: fallbackArtwork""",
    """    // A media session can temporarily publish no cover while switching songs.
    // Keep the last real artwork visible throughout the next track's lyric lookup,
    // no matter how long it takes. The old 2.5 s countdown could expire while the
    // three-dot loader was showing, making a 10 s crossfade start from the synthetic
    // placeholder (or look blank) instead of from the previous song.
    //
    // If the new track truly has no cover, still switch to the unbranded fallback
    // after 2.5 s *once lyrics are no longer loading*. Arrival of new artwork
    // cancels that countdown automatically.
    var heldArtwork by remember { mutableStateOf<android.graphics.Bitmap?>(null) }
    LaunchedEffect(artwork, lyricsState, snapshot.track?.cacheKey) {
        if (artwork != null) {
            heldArtwork = artwork
        } else if (lyricsState == LyricsState.Loading && heldArtwork != null) {
            // Keep the outgoing cover behind the three loading dots.
            // When loading ends, this effect restarts and the grace clock begins.
        } else {
            delay(2_500)
            heldArtwork = null
        }
    }
    val backgroundArtwork = artwork ?: heldArtwork ?: fallbackArtwork""",
    "hold outgoing album art until lyrics-loading completes")
player_path.write_text(player, encoding="utf-8")

store = store_path.read_text(encoding="utf-8")
store = once(store,
    """                // \`package\\u0000label\`, because a label can contain anything a developer typed and a
                // separator that cannot appear in either half is the only kind worth using.
                val at = entry.indexOf('\\u0000')
                if (at <= 0) null else entry.take(at) to entry.substring(at + 1)""",
    """                // A package name cannot contain a tab, but a display label can.
                // Split on the first tab only. Legacy NUL-delimited entries are read
                // for migration; NUL is invalid in Android SharedPreferences XML.
                val tab = entry.indexOf('\\t')
                val at = if (tab > 0) tab else entry.indexOf('\\u0000')
                if (at <= 0) null else entry.take(at) to entry.substring(at + 1)""",
    "accept safe tab and legacy NUL encoded player labels")
store = once(store,
    """            putStringSet(KEY_PLAYERS_SEEN, next.map { (pkg, name) -> "$pkg\\u0000$name" }.toSet())""",
    """            // Store an XML-safe separator. NUL cannot survive preferences XML.
            // The package identifier never contains a tab; labels may contain any text.
            putStringSet(KEY_PLAYERS_SEEN, next.map { (pkg, name) -> "$pkg\\t$name" }.toSet())""",
    "save previously detected players with XML-safe delimiter")
store_path.write_text(store, encoding="utf-8")
print("TV v2.7.3: outgoing artwork survives lyrics loading until next art; player history persists using XML-safe labels.")
