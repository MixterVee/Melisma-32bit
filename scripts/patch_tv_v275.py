#!/usr/bin/env python3
"""Melisma Universal v2.7.5: name the actual music source in TV seeking-unavailable text.

Apply after patch_tv_v274.py against Melisma v2.11.0.
The source comes from PlayerSnapshot.sourceLabel (the active Android media
session), not a guessed label or package-name lookup. Changes only the disabled
seek message in Lyrics/NowBar and Cinema/MediaPanel; all background fixes stay.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
player_file = ui / "PlayerScreen.kt"
now_file = ui / "components/NowBar.kt"
media_file = ui / "components/MediaPanel.kt"
focus_file = ui / "components/TvTransportFocus.kt"

def once(s: str, before: str, after: str, label: str) -> str:
    n = s.count(before)
    if n != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {n}")
    return s.replace(before, after, 1)

# Source label is already resolved by MediaSessionRepository from the currently
# active media session; make sure both player views receive its exact value.
player = player_file.read_text(encoding="utf-8")
player = once(player,
    """                                artwork = artwork,
                                accent = colors.accent,
                                tvTransportFocusRequester = if (tvMode) transportFocus else null,""",
    """                                artwork = artwork,
                                accent = colors.accent,
                                sourceLabel = snapshot.sourceLabel,
                                tvTransportFocusRequester = if (tvMode) transportFocus else null,""",
    "NowBar actual source label")
player = once(player,
    """                            artwork = artwork,
                            accent = colors.accent,
                            tvTransportFocusRequester = tvTransportFocusRequester,""",
    """                            artwork = artwork,
                            accent = colors.accent,
                            sourceLabel = snapshot.sourceLabel,
                            tvTransportFocusRequester = tvTransportFocusRequester,""",
    "MediaPanel actual source label")
player_file.write_text(player, encoding="utf-8")

now = now_file.read_text(encoding="utf-8")
now = once(now,
    """    artwork: Bitmap?,
    accent: Color,
    transport: Transport,""",
    """    artwork: Bitmap?,
    accent: Color,
    sourceLabel: String?,
    transport: Transport,""",
    "NowBar sourceLabel parameter")
now = once(now,
    """            TvSeekUnavailable(
                playRequester = tvTransportFocusRequester,
                onUp = onTvSeekUp,
                modifier = Modifier.fillMaxWidth().height(24.dp),""",
    """            TvSeekUnavailable(
                playRequester = tvTransportFocusRequester,
                onUp = onTvSeekUp,
                sourceLabel = sourceLabel,
                modifier = Modifier.fillMaxWidth().height(24.dp),""",
    "NowBar seek-unavailable source")
now_file.write_text(now, encoding="utf-8")

media = media_file.read_text(encoding="utf-8")
media = once(media,
    """    artwork: Bitmap?,
    accent: Color,
    transport: Transport,""",
    """    artwork: Bitmap?,
    accent: Color,
    sourceLabel: String?,
    transport: Transport,""",
    "MediaPanel sourceLabel parameter")
media = once(media,
    """                rightLabel = formatTime(playback.durationMs),
                onTvSeekUp = onTvSeekUp,
                tvTransportFocusRequester = tvTransportFocusRequester,""",
    """                rightLabel = formatTime(playback.durationMs),
                onTvSeekUp = onTvSeekUp,
                tvTransportFocusRequester = tvTransportFocusRequester,
                sourceLabel = sourceLabel,""",
    "MediaPanel ScrubBar forwards source")
media = once(media,
    """    rightLabel: String,
    onTvSeekUp: (() -> Unit)?,
    tvTransportFocusRequester: FocusRequester?,
) {""",
    """    rightLabel: String,
    onTvSeekUp: (() -> Unit)?,
    tvTransportFocusRequester: FocusRequester?,
    sourceLabel: String?,
) {""",
    "MediaPanel ScrubBar source parameter")
media = once(media,
    """        TvSeekUnavailable(
            playRequester = tvTransportFocusRequester,
            onUp = onTvSeekUp,
            modifier = Modifier.fillMaxWidth().height(24.dp),""",
    """        TvSeekUnavailable(
            playRequester = tvTransportFocusRequester,
            onUp = onTvSeekUp,
            sourceLabel = sourceLabel,
            modifier = Modifier.fillMaxWidth().height(24.dp),""",
    "Cinema seek-unavailable source")
media_file.write_text(media, encoding="utf-8")

helper = focus_file.read_text(encoding="utf-8")
helper = once(helper,
    """internal fun TvSeekUnavailable(
    playRequester: FocusRequester?,
    onUp: (() -> Unit)?,
    modifier: Modifier = Modifier,
) {""",
    """internal fun TvSeekUnavailable(
    playRequester: FocusRequester?,
    onUp: (() -> Unit)?,
    sourceLabel: String?,
    modifier: Modifier = Modifier,
) {""",
    "helper source-label parameter")
helper = once(helper,
    """            "Seeking unavailable from music app",
            fontSize = 11.sp,""",
    """            "Seeking unavailable from " + (sourceLabel?.trim()?.takeIf { it.isNotEmpty() } ?: "music app"),
            fontSize = 11.sp,""",
    "active app in unavailable seek text")
focus_file.write_text(helper, encoding="utf-8")

print("TV v2.7.5: seeking-unavailable now names the active media app, in Lyrics and Cinema views.")
