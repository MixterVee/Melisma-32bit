#!/usr/bin/env python3
"""Melisma Universal v2.7.4: protect the outgoing Cover Art during track loading.

Applies after patch_tv_v273.py. The original 2.7.3 fix retained only NULL art.
Some media sessions publish a temporary black bitmap, or publish new metadata
before the lyrics state has advanced to Loading. Both can replace the outgoing
cover with a blank frame. Hold displayed art across those transitions.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
player_path = root / "app/src/main/java/com/melisma/app/ui/PlayerScreen.kt"
s = player_path.read_text(encoding="utf-8")

old = """    // A media session can temporarily publish no cover while switching songs.
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
    val backgroundArtwork = artwork ?: heldArtwork ?: fallbackArtwork"""

new = """    // Media sessions can briefly publish no album art OR a nearly black placeholder
    // during track changes. Neither should replace the outgoing visible cover.
    // Only the background is held; the actual player metadata/art stays untouched.
    val candidateArtwork = remember(artwork) {
        artwork?.takeUnless { it.tvIsEmptyCoverPlaceholder() }
    }
    var heldArtwork by remember { mutableStateOf(candidateArtwork) }
    LaunchedEffect(candidateArtwork, lyricsState, snapshot.track?.cacheKey) {
        if (candidateArtwork != null) {
            if (heldArtwork == null) {
                // First launch: show an available real cover immediately.
                heldArtwork = candidateArtwork
            } else if (candidateArtwork !== heldArtwork) {
                if (lyricsState != LyricsState.Loading) {
                    // Media metadata may arrive one frame before the lyric lookup
                    // enters Loading. A short grace period permits that state update
                    // to cancel this effect, avoiding a placeholder flash.
                    delay(600)
                    heldArtwork = candidateArtwork
                }
                // During Loading, keep the outgoing cover. Once Loading ends,
                // LaunchedEffect restarts with the most recent candidate.
            }
        } else if (heldArtwork != null) {
            // A genuinely artwork-free song must ultimately show the generated
            // fallback. 12 seconds accommodates slow artwork URI fetches and
            // avoids a blank screen partway through a long lyric lookup.
            if (lyricsState != LyricsState.Loading) {
                delay(12_000)
                heldArtwork = null
            }
        }
    }
    // Never bypass the handoff with raw artwork here: that was the key flaw in
    // earlier builds. Let Compose Crossfade target only the approved visible cover.
    val backgroundArtwork = heldArtwork ?: fallbackArtwork"""

n=s.count(old)
if n!=1:
    raise RuntimeError(f"Expected exactly one v2.7.3 player artwork block, found {n}")
s=s.replace(old,new,1)
if "tvIsEmptyCoverPlaceholder()" not in s:
    raise RuntimeError("Candidate cover filter missing")
# Leave original imports and all other player logic unchanged. The function
# uses fully-qualified Android APIs so no new import is necessary.
s += """

/**
 * Only reject clearly empty black/transparent interim bitmaps. Media players can
 * publish these while fetching an actual cover, which otherwise makes the 10 s
 * transition visibly blend into a black screen during three-dot lyric loading.
 * Sample only nine pixels (instead of reading/copying a full album bitmap);
 * inaccessible hardware bitmaps are accepted rather than falsely discarded.
 */
private fun android.graphics.Bitmap.tvIsEmptyCoverPlaceholder(): Boolean {
    if (width < 8 || height < 8) return true
    return runCatching {
        val fractions = intArrayOf(1, 2, 3)
        var allBlank = true
        for (fy in fractions) for (fx in fractions) {
            val pixel = getPixel((width - 1) * fx / 4, (height - 1) * fy / 4)
            val alpha = android.graphics.Color.alpha(pixel)
            val red = android.graphics.Color.red(pixel)
            val green = android.graphics.Color.green(pixel)
            val blue = android.graphics.Color.blue(pixel)
            if (alpha > 16 && maxOf(red, green, blue) > 12) {
                allBlank = false
                break
            }
        }
        allBlank
    }.getOrDefault(false)
}
"""
player_path.write_text(s, encoding="utf-8")
print("Melisma v2.7.4: background keeps outgoing cover through lyric loading, rejects empty black interim bitmaps, and waits for stable new artwork.")
