#!/usr/bin/env python3
"""Melisma Universal v2.8.6: remote toolbar focus descriptions and fixed 3.4s cover art fades.

Apply after patch_tv_v285.py against upstream Melisma v2.11.0.
- Every top-toolbar ActionChip and LabelChip announces itself in a one-line,
  unobtrusive TV-only description directly underneath the button row.
- Actual Cinema cover and Lyrics/NowBar thumbnail share a 3,400ms image crossfade
  with no blank intermediary (null and transient artwork are held).
- Cover Art background style itself has a 3,400ms image crossfade, independent
  of the user-selected background transition for other renderer styles.
- Reuse PlayerScreen's already approved backgroundArtwork for visible album art
  to avoid Amazon's double metadata refresh and black placeholders.
No changes to Amazon auto-next, lyric source cycling or TV timing.
"""
from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("melisma")
base = root / "app/src/main/java/com/melisma/app"
player_file = base / "ui/PlayerScreen.kt"
media_file = base / "ui/components/MediaPanel.kt"
now_file = base / "ui/components/NowBar.kt"
bg_file = base / "ui/background/DynamicBackground.kt"
helper_file = base / "ui/components/SmoothCoverArt.kt"

def once(s, old, new, label):
    n = s.count(old)
    if n != 1:
        raise RuntimeError(f"{label}: expected 1 anchor, got {n}")
    return s.replace(old, new, 1)

player = player_file.read_text(encoding="utf-8")
player = once(player,
    """// ---- chrome -------------------------------------------------------------------

@Composable
private fun ViewControls(""",
    """// ---- chrome -------------------------------------------------------------------

/** One TV-only tooltip line, updated by the focus of any toolbar chip. */
private val LocalToolbarFocusHelp =
    androidx.compose.runtime.compositionLocalOf<(String?) -> Unit> { {} }

@Composable
private fun ViewControls(""",
    "Toolbar focus description local")

# Keep layout fixed between focus changes, avoiding jumps while D-pad moves.
player = once(player,
    """    BoxWithConstraints(Modifier.fillMaxWidth()) {
        val sideBySide = maxWidth >= 560.dp""",
    """    val tv = androidx.compose.ui.platform.LocalContext.current.packageManager.hasSystemFeature(
        android.content.pm.PackageManager.FEATURE_LEANBACK
    )
    var focusedToolbarHelp by remember { mutableStateOf<String?>(null) }
    androidx.compose.runtime.CompositionLocalProvider(
        LocalToolbarFocusHelp provides { label: String? ->
            if (focusedToolbarHelp != label) focusedToolbarHelp = label
        },
    ) {
    Column(Modifier.fillMaxWidth()) {
    BoxWithConstraints(Modifier.fillMaxWidth()) {
        val sideBySide = maxWidth >= 560.dp""",
    "Toolbar description state and layout")

player = once(player,
    """        }
    }
}

@Composable
private fun ActionChip(""",
    """        }
    }
    if (tv) {
        Box(
            Modifier.fillMaxWidth()
                .height(26.dp)
                .padding(horizontal = 14.dp),
            contentAlignment = Alignment.CenterStart,
        ) {
            Text(
                text = focusedToolbarHelp.orEmpty(),
                color = Color.White.copy(alpha = 0.85f),
                fontSize = 12.sp,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
    }
}

@Composable
private fun ActionChip(""",
    "Toolbar TV focus help below button row")

# These two chips already have proper focus targets. Register their on-focus
# descriptions without disturbing existing D-pad focus behaviour or callbacks.
player = once(player,
    """private fun ActionChip(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    description: String,
    active: Boolean,
    accent: Color,
    onClick: () -> Unit,
) {
    Box(
        Modifier
            .size(38.dp)""",
    """private fun ActionChip(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    description: String,
    active: Boolean,
    accent: Color,
    onClick: () -> Unit,
) {
    val updateHelp = LocalToolbarFocusHelp.current
    Box(
        Modifier
            .size(38.dp)
            .onFocusChanged { focus ->
                updateHelp(if (focus.isFocused) description else null)
            }""",
    "ActionChip focus descriptions")
player = once(player,
    """private fun LabelChip(
    label: String,
    description: String,
    active: Boolean,
    accent: Color,
    onClick: () -> Unit,
    focusRequester: FocusRequester? = null,
) {
    var focused by remember { mutableStateOf(false) }""",
    """private fun LabelChip(
    label: String,
    description: String,
    active: Boolean,
    accent: Color,
    onClick: () -> Unit,
    focusRequester: FocusRequester? = null,
) {
    val updateHelp = LocalToolbarFocusHelp.current
    var focused by remember { mutableStateOf(false) }""",
    "LabelChip focus callback")
# The v2.6 modification added an existing focused state listener on LabelChip:
# extend it rather than stack another onFocusChanged.
player = once(player,
    """            .onFocusChanged { focused = it.isFocused }""",
    """            .onFocusChanged {
                focused = it.isFocused
                updateHelp(if (it.isFocused) description else null)
            }""",
    "LabelChip existing focus listener")

# Both the compact thumbnail and Cinema panel should see the *approved*
# image, already protected by the v2.7.4 600ms art handoff. The big background
# uses the same approved bitmap and will remain a separate crossfade animation.
player = once(player,
    """                                artwork = artwork,
                                accent = colors.accent,
                                transport = snapshot.transport,""",
    """                                artwork = backgroundArtwork,
                                accent = colors.accent,
                                transport = snapshot.transport,""",
    "NowBar uses approved artwork")
player = once(player,
    """            artwork = artwork,
            colors = colors,
            permissionGranted = permissionGranted,""",
    """            artwork = backgroundArtwork,
            colors = colors,
            permissionGranted = permissionGranted,""",
    "Cinema MainContent uses approved artwork")
player_file.write_text(player, encoding="utf-8")

media = media_file.read_text(encoding="utf-8")
media = once(media,
    """                    if (artwork != null) {
                        Image(
                            bitmap = artwork.asImageBitmap(),
                            contentDescription = "Album artwork",
                            contentScale = ContentScale.Crop,
                            modifier = Modifier.fillMaxSize(),
                        )
                    } else {
                        Icon(
                            AppIcons.MusicNote,
                            contentDescription = null,
                            tint = Color.White.copy(alpha = 0.4f),
                            modifier = Modifier.size(side * 0.3f),
                        )
                    }""",
    """                    SmoothCoverArt(
                        artwork = artwork,
                        modifier = Modifier.fillMaxSize(),
                        fallbackSize = side * 0.3f,
                    )""",
    "Cinema artwork uses 3400ms crossfade")
media_file.write_text(media, encoding="utf-8")

now = now_file.read_text(encoding="utf-8")
now = once(now,
    """                if (artwork != null) {
                    androidx.compose.foundation.Image(
                        bitmap = artwork.asImageBitmap(),
                        contentDescription = null,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.size(54.dp),
                    )
                } else {
                    Icon(
                        AppIcons.MusicNote,
                        contentDescription = null,""",
    """                if (artwork != null) {
                    SmoothCoverArt(
                        artwork = artwork,
                        modifier = Modifier.size(54.dp),
                        fallbackSize = 18.dp,
                    )
                } else {
                    Icon(
                        AppIcons.MusicNote,
                        contentDescription = null,""",
    "NowBar artwork uses 3400ms crossfade")
# A temporary lack of art should *not* bypass the smooth cover renderer;
# render the shared helper in both cases.
now = once(now,
    """                if (artwork != null) {
                    SmoothCoverArt(
                        artwork = artwork,
                        modifier = Modifier.size(54.dp),
                        fallbackSize = 18.dp,
                    )
                } else {
                    Icon(
                        AppIcons.MusicNote,
                        contentDescription = null,
                        tint = Color.White.copy(alpha = 0.4f),
                        modifier = Modifier.size(18.dp),
                    )
                }""",
    """                SmoothCoverArt(
                    artwork = artwork,
                    modifier = Modifier.size(54.dp),
                    fallbackSize = 18.dp,
                )""",
    "NowBar null cover retains previous art")
now_file.write_text(now, encoding="utf-8")

# Only the Cover Art BACKGROUND style gets this fixed duration. All other
# background settings and effects keep the user's own transition slider.
bg = bg_file.read_text(encoding="utf-8")
bg = once(bg,
    """        Crossfade(
            targetState = source,
            animationSpec = tween(
                durationMillis = transitionDurationMs,
                easing = FastOutSlowInEasing,
            ),
            label = "Background artwork crossfade",
        )""",
    """        Crossfade(
            targetState = source,
            animationSpec = tween(
                durationMillis = if (resolved == BackgroundStyle.COVER_ART) 3_400
                    else transitionDurationMs,
                easing = FastOutSlowInEasing,
            ),
            label = "Background artwork crossfade",
        )""",
    "Cover Art background 3.4 seconds independent of adjustable background fade")
bg_file.write_text(bg, encoding="utf-8")

if helper_file.exists():
    raise RuntimeError("SmoothCoverArt already exists in upstream or another patch")
helper_file.write_text("""package com.melisma.app.ui.components

import android.graphics.Bitmap
import androidx.compose.animation.Crossfade
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.Dp
import kotlinx.coroutines.delay

/**
 * Real (unblurred) cover artwork, independent of the background fade setting.
 * No new bitmap can cause an instantaneous swap. Null metadata does not blank
 * a previously rendered bitmap. Short-lived duplicate album art updates are
 * coalesced, avoiding the visible double-fade seen with Amazon Music.
 */
@Composable
internal fun SmoothCoverArt(
    artwork: Bitmap?,
    modifier: Modifier = Modifier,
    fallbackSize: Dp,
) {
    var shown by remember { mutableStateOf<Bitmap?>(null) }
    LaunchedEffect(artwork) {
        if (artwork != null && artwork !== shown) {
            // Let back-to-back cover sources settle before changing the picture.
            delay(350)
            shown = artwork
        }
    }

    Box(modifier, contentAlignment = Alignment.Center) {
        if (shown == null) {
            Icon(
                AppIcons.MusicNote,
                contentDescription = null,
                tint = Color.White.copy(alpha = 0.4f),
                modifier = Modifier.fillMaxSize(0.32f),
            )
        }
        Crossfade(
            targetState = shown,
            animationSpec = tween(
                durationMillis = 3_400,
                easing = FastOutSlowInEasing,
            ),
            label = "Cover art fixed 3.4s fade",
            modifier = Modifier.fillMaxSize(),
        ) { cover ->
            if (cover != null) {
                Image(
                    bitmap = cover.asImageBitmap(),
                    contentDescription = "Album artwork",
                    contentScale = ContentScale.Crop,
                    modifier = Modifier.fillMaxSize(),
                )
            }
        }
    }
}
""", encoding="utf-8")
print("Melisma v2.8.6: remote toolbar descriptions, Cinema/NowBar fixed 3.4s cover fades, COVER_ART background fixed 3.4s; other background styles unchanged.")
