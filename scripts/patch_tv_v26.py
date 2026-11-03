#!/usr/bin/env python3
"""Melisma TV v2.6: first-position ±ms shortcut, calm transitions, informative Deezer seek focus.

Apply only after patch_tv_hidden_down.py. Fail closed if upstream anchors change.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
ui = root / "app/src/main/java/com/melisma/app/ui"
pp = ui / "PlayerScreen.kt"
mp = ui / "components/MediaPanel.kt"
np = ui / "components/NowBar.kt"
hp = ui / "components/TvTransportFocus.kt"
bg = ui / "background/DynamicBackground.kt"
kw = ui / "background/Kawarp.kt"

def once(s: str, a: str, b: str, label: str) -> str:
    n = s.count(a)
    if n != 1:
        raise RuntimeError(f"{label}: wanted 1 anchor; found {n}")
    return s.replace(a, b, 1)

player = pp.read_text(encoding="utf-8")
# v2.5 put ±ms next to Settings, after a long horizontal-scroll toolbar.
# Place it at the extreme left and use it as the FIRST focus target on every reveal.
player = once(player,
    """                ActionChip(AppIcons.Refresh, "Look up again", false, accent, onRetry)
                if (onOpenTvSync != null) {
                    LabelChip(
                        "±ms",
                        "Quick timing adjustment for this song",
                        false,
                        Color(0xFF00D5FF),
                        onOpenTvSync,
                    )
                }
                ActionChip(AppIcons.Tune, "Settings", false, accent, onSettings)""",
    """                ActionChip(AppIcons.Refresh, "Look up again", false, accent, onRetry)
                ActionChip(AppIcons.Tune, "Settings", false, accent, onSettings)""",
    "remove hidden-at-end timing chip")

player = once(player,
    """            ) {
                if (romanizationAvailable) {""",
    """            ) {
                // Timing is the most-used TV control: always first, never hidden off-screen.
                if (onOpenTvSync != null) {
                    LabelChip(
                        "±ms",
                        "Quick timing adjustment for this song",
                        false,
                        Color(0xFF00D5FF),
                        onOpenTvSync,
                        focusRequester = tvFocusRequester,
                    )
                }
                if (romanizationAvailable) {""",
    "put ±ms at front of toolbar")

player = once(player,
    """                    "Cinema view",
                    settings.viewMode == ViewMode.CINEMA,
                    accent,
                    onToggleCinema,
                    focusRequester = tvFocusRequester,
                )""",
    """                    "Cinema view",
                    settings.viewMode == ViewMode.CINEMA,
                    accent,
                    onToggleCinema,
                )""",
    "reassign initial toolbar focus from Cinema to timing")

needle = "private fun LabelChip("
idx = player.find(needle)
if idx < 0 or player.find(needle,idx+1)>=0:
    raise RuntimeError("Cannot isolate LabelChip declaration")
prefix = player[:idx]
suffix = player[idx:]
suffix = once(suffix,
    """    onClick: () -> Unit,
) {
    var focused by remember { mutableStateOf(false) }""",
    """    onClick: () -> Unit,
    focusRequester: FocusRequester? = null,
) {
    var focused by remember { mutableStateOf(false) }""",
    "LabelChip focusRequester parameter")
suffix = once(suffix,
    """        Modifier
            .size(42.dp)
            .onFocusChanged { focused = it.isFocused }""",
    """        Modifier
            .size(42.dp)
            .then(if (focusRequester != null) Modifier.focusRequester(focusRequester) else Modifier)
            .onFocusChanged { focused = it.isFocused }""",
    "attach FocusRequester to LabelChip")
pp.write_text(prefix + suffix, encoding="utf-8")

# Seeking is offered iff the current media session advertises ACTION_SEEK_TO.
# Disabled Material3 Slider cannot receive D-pad focus; visually it looks like
# a navigation trap. Place a focusable message over it in TV mode. Never seek
# if the player has not offered the capability (avoid false lyric position updates).
now = np.read_text(encoding="utf-8")
now = once(now,
    """        Slider(
            value = fraction,
            // A player that does not accept seeks still shows progress; it just cannot be""",
    """        Box(Modifier.fillMaxWidth()) {
        Slider(
            value = fraction,
            // A player that does not accept seeks still shows progress; it just cannot be""",
    "NowBar seek fallback wrapper")
now = once(now,
    """                .tvSeekEscape(tvTransportFocusRequester, onTvSeekUp)
                .tvTransportFocus(RoundedCornerShape(8.dp)),
        )

        Row(""",
    """                .tvSeekEscape(tvTransportFocusRequester, onTvSeekUp)
                .tvTransportFocus(RoundedCornerShape(8.dp)),
        )
        if (!transport.seek) {
            TvSeekUnavailable(
                playRequester = tvTransportFocusRequester,
                onUp = onTvSeekUp,
                modifier = Modifier.fillMaxWidth().height(24.dp),
            )
        }
        }

        Row(""",
    "NowBar unavailable seek focus target")
np.write_text(now, encoding="utf-8")

media = mp.read_text(encoding="utf-8")
media = once(media,
    """    onTvSeekUp: (() -> Unit)?,
    tvTransportFocusRequester: FocusRequester?,
) {
    Slider(""",
    """    onTvSeekUp: (() -> Unit)?,
    tvTransportFocusRequester: FocusRequester?,
) {
    Box(Modifier.fillMaxWidth()) {
    Slider(""",
    "MediaPanel ScrubBar wrapper")
media = once(media,
    """            .tvSeekEscape(tvTransportFocusRequester, onTvSeekUp)
            .tvTransportFocus(RoundedCornerShape(8.dp)),
    )
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {""",
    """            .tvSeekEscape(tvTransportFocusRequester, onTvSeekUp)
            .tvTransportFocus(RoundedCornerShape(8.dp)),
    )
    if (!enabled) {
        TvSeekUnavailable(
            playRequester = tvTransportFocusRequester,
            onUp = onTvSeekUp,
            modifier = Modifier.fillMaxWidth().height(24.dp),
        )
    }
    }
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {""",
    "MediaPanel unavailable seek focus target")
mp.write_text(media, encoding="utf-8")

helper = hp.read_text(encoding="utf-8")
helper = once(helper,
    "import androidx.compose.foundation.background\n",
    """import androidx.compose.foundation.background
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Box
""",
    "seek fallback helper imports")
helper = once(helper,
    "import androidx.compose.ui.Modifier\n",
    """import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.material3.Text
import androidx.compose.ui.unit.sp
""",
    "seek fallback text imports")
helper += """

/** A disabled seek control stays reachable on Android TV and explains why.
 * No seek command is sent when a music app's media session lacks ACTION_SEEK_TO.
 */
@Composable
internal fun TvSeekUnavailable(
    playRequester: FocusRequester?,
    onUp: (() -> Unit)?,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    if (!context.packageManager.hasSystemFeature(PackageManager.FEATURE_LEANBACK)) return
    var focused by remember { mutableStateOf(false) }
    Box(
        modifier
            .onFocusChanged { focused = it.isFocused }
            .tvSeekEscape(playRequester, onUp)
            .focusable()
            .border(
                if (focused) 2.dp else 0.dp,
                if (focused) Color(0xFF00D5FF) else Color.Transparent,
                androidx.compose.foundation.shape.RoundedCornerShape(8.dp),
            )
            .background(
                if (focused) Color(0x3300D5FF) else Color(0x22000000),
                androidx.compose.foundation.shape.RoundedCornerShape(8.dp),
            ),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            "Seeking unavailable from music app",
            fontSize = 11.sp,
            color = if (focused) Color(0xFF00D5FF) else Color.White.copy(alpha = 0.7f),
        )
    }
}
"""
hp.write_text(helper, encoding="utf-8")

# Slow real track-to-track colour changes from 1s to a smooth ~3.4s crossfade.
# First artwork still appears immediately (no outgoing frame to hold).
kawarp = kw.read_text(encoding="utf-8")
kawarp = once(kawarp, "const val TRANSITION_MS = 1_000L",
              "const val TRANSITION_MS = 3_400L",
              "Kawarp colour-field transition duration")
kw.write_text(kawarp, encoding="utf-8")

background = bg.read_text(encoding="utf-8")
background = once(background,
    "import androidx.compose.animation.core.Animatable\n",
    """import androidx.compose.animation.core.Animatable
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.FastOutSlowInEasing
""",
    "background animation imports")
background = once(background,
    """    val resolved = resolveStyle(style, preferStill, stillAsCover, hasArtistImage = artistImage != null)
""",
    """    val resolved = resolveStyle(style, preferStill, stillAsCover, hasArtistImage = artistImage != null)
    // The field cross-fade is deliberately slow and the base wash must not jump to
    // the next song's palette on the very first frame.
    val blendedBase by animateColorAsState(
        targetValue = colors.base,
        animationSpec = tween(durationMillis = 3_400, easing = FastOutSlowInEasing),
        label = "Background base palette",
    )
    val blendedDark by animateColorAsState(
        targetValue = colors.darkVibrant,
        animationSpec = tween(durationMillis = 3_400, easing = FastOutSlowInEasing),
        label = "Background upper palette",
    )
    val softColors = colors.copy(base = blendedBase, darkVibrant = blendedDark)
""",
    "smooth background palette")

# Only edit inside DynamicBackground before StillCoverBackground declaration.
head, divider, tail = background.partition("/**\n * Album art held still")
if not divider:
    raise RuntimeError("DynamicBackground helper boundary missing")
head = once(head,
    "StillCoverBackground(source, colors, blurRadius, staticModifier)",
    "StillCoverBackground(source, softColors, blurRadius, staticModifier)",
    "soft colors in still background")
head = once(head,
    """            drawRect(colors.base)
            drawRect(
                brush = Brush.verticalGradient(
                    0f to colors.darkVibrant.copy(alpha = 0.85f),""",
    """            drawRect(softColors.base)
            drawRect(
                brush = Brush.verticalGradient(
                    0f to softColors.darkVibrant.copy(alpha = 0.85f),""",
    "soft colors in color background")
head = once(head,
    """            colors = colors,
            moving = playing && !preferStill,""",
    """            colors = softColors,
            moving = playing && !preferStill,""",
    "soft colors in Kawarp")
head = once(head,
    "fadeAnim.animateTo(1f, tween(durationMillis = 850, easing = LinearEasing))",
    "fadeAnim.animateTo(1f, tween(durationMillis = 3_400, easing = FastOutSlowInEasing))",
    "smooth Living classic transition")
head = once(head,
    """        drawRect(colors.base)

        val bitmap = current""",
    """        drawRect(softColors.base)

        val bitmap = current""",
    "soft classic canvas base")
background = head + divider + tail
bg.write_text(background, encoding="utf-8")
print("TV v2.6: first and default ±ms button, navigable seek-unavailable message, and 3.4-second palette crossfade.")
