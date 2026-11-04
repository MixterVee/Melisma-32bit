#!/usr/bin/env python3
"""Melisma Universal v2.8.7: keep cover visible and restore independent fades.

Apply after patch_tv_v286.py against upstream Melisma v2.11.0.
- Undo v2.8.6's unintended fixed 3.4s COVER_ART *background* transition:
  background always respects the saved 1–10s slider, e.g. 7.4 seconds.
- Keep the Cinema artwork panel composed while the next song's lyrics load.
  This avoids disposing/remounting its animation and flashing an empty frame.
- On first composition of SmoothCoverArt show available real album art immediately
  (no 3.4s fade from black/music note), then crossfade later real cover changes
  over exactly 3.4s. Transient null/placeholder art is already held upstream.
- Maintain v2.8.6 TV remote toolbar descriptions and the v2.8.5 experimental
  32-bit Amazon Music 1-second auto-next without any modifications.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
base = root / "app/src/main/java/com/melisma/app/ui"
bg_file = base / "background/DynamicBackground.kt"
player_file = base / "PlayerScreen.kt"
cover_file = base / "components/SmoothCoverArt.kt"

def once(text, before, after, label):
    n = text.count(before)
    if n != 1:
        raise RuntimeError(f"{label}: expected one anchor, found {n}")
    return text.replace(before, after, 1)

bg = bg_file.read_text(encoding="utf-8")
bg = once(bg,
    """durationMillis = if (resolved == BackgroundStyle.COVER_ART) 3_400
                    else transitionDurationMs,""",
    """durationMillis = transitionDurationMs,""",
    "restore slider-driven full-screen Cover Art background transition")
bg_file.write_text(bg, encoding="utf-8")

cover = cover_file.read_text(encoding="utf-8")
cover = once(cover,
    """    var shown by remember { mutableStateOf<Bitmap?>(null) }""",
    """    // On initial composition, show the already vetted/held image immediately.
    // A Cinema panel can be briefly recreated by a media-session gap; starting
    // at null would make the cover disappear, then fade in from nothing.
    var shown by remember { mutableStateOf(artwork) }""",
    "do not fade a newly composed album cover in from blank")
cover_file.write_text(cover, encoding="utf-8")

player = player_file.read_text(encoding="utf-8")
start = player.index("private fun MainContent(")
end = player.index("/** Opens [link] in the browser", start)
main = player[start:end]
main = once(main,
    """        document != null -> {
            val positionProvider = rememberPositionProvider(snapshot, settings)
            val sourceTap = sourceTap(LocalContext.current, container.lyrics.sourceLink(document.providerId))
            val lyrics: @Composable (Modifier) -> Unit = { mod ->
                Column(mod) {""",
    """        document != null || (settings.viewMode == ViewMode.CINEMA && snapshot.track != null) -> {
            // Keep CinemaLayout's MediaPanel composed even when lyrics enter
            // Loading between tracks. Its cover-art Crossfade then retains the
            // outgoing image instead of vanishing behind a status screen.
            val positionProvider = rememberPositionProvider(snapshot, settings)
            val sourceTap = document?.let {
                sourceTap(LocalContext.current, container.lyrics.sourceLink(it.providerId))
            }
            val lyrics: @Composable (Modifier) -> Unit = { mod ->
                if (document == null) {
                    Box(mod, contentAlignment = Alignment.Center) {
                        when (val state = lyricsState) {
                            LyricsState.NotFound -> StatusMessage(
                                "No lyrics found",
                                "No enabled lyrics source has lyrics for this song.",
                            )
                            LyricsState.Offline -> StatusMessage(
                                "You're offline",
                                "Lyrics aren't available for this track.",
                            )
                            is LyricsState.Failed -> StatusMessage("Couldn't load lyrics", state.message)
                            else -> LoadingLyrics(accent = colors.accent)
                        }
                    }
                } else {
                Column(mod) {""",
    "keep Cinema media panel while song lyrics load")
main = once(main,
    """                    UnsyncedNotice(visible = document.kind == LyricsKind.STATIC)
                }
            }

            val track = snapshot.track""",
    """                    UnsyncedNotice(visible = document.kind == LyricsKind.STATIC)
                }
                }
            }

            val track = snapshot.track""",
    "close loading lyrics branch without unmounting Cinema cover")
player = player[:start] + main + player[end:]
player_file.write_text(player, encoding="utf-8")
print("v2.8.7: background transition respects slider, cover crossfade fixed 3.4s, Cinema cover persists during lyrics loading.")
