# Melisma Universal — Windows Lyrics Preview v0.4.2

Windows 11 x64 · Foobar2000 · read-only direct playback bridge

## What's new in v0.4.2 — quiet footer

- Removed the continuously updating elapsed/duration counter, playback state, and bridge status from the bottom of the controller.
- The footer now displays only **Lyrics delay: +2000 ms (saved for this song)** (or the current song's actual correction).
- This label changes only when a track is detected or the user presses −100 ms / +100 ms / Reset; it never refreshes on the 55 ms drawing timer.
- Removed the redundant footer lyric-source row and returned that space to the preview.
- Source status and playback state remain internal for lyrics synchronization; playback timing and the TV screen are unchanged.

## Retained from v0.4.1 — controller preview stability

- Keep the controller preview's gradient, album art, and lyric text in one fully buffered paint, with no independent background erase.
- Fade partially visible lyrics at the **top and bottom of the controller preview** so lines enter and exit gracefully instead of flashing at its clipped edges.
- Round preview-only text positions to full pixels to reduce shimmering while scrolling. The full-screen TV display is unchanged.
- Lyric timestamps, track reading, word highlighting, pre-wrapping and saved per-song timing offsets are untouched.

## First test for v0.4.1

1. Keep the working v0.4 and v0.3.2 folders, extract the v0.4.1 build separately.
2. Play the same section used in your recordings and focus on the **second-to-last lyric near the bottom** of the controller preview.
3. Verify the lyric gently fades into place, rather than rapidly flashing as it approaches the bottom.
4. Check the second-monitor TV still looks the same and timing is unchanged.

## Retained from v0.4 — album-art backgrounds

- **Automatic artwork** from the current Foobar2000 Windows media session, when Foobar2000 makes a thumbnail available to Windows. Not every media session exposes a usable image; the app does **not** scrape the internet or read your MP3 files.
- **TV and preview backgrounds**: the same art is shown behind the lyrics in the Windows controller preview and on the extended-monitor TV karaoke window.
- **Gentle crossfading** when songs change or images are selected. Fade length is adjustable from **1 to 10 seconds**, default **4 seconds**.
- **Artwork on/off** checkbox, with the setting and fade duration remembered between launches.
- **Open cover image** lets you manually use a JPG, PNG, WebP or BMP (up to 8 MB) for the current display when automatic artwork is unavailable. Manual artwork does not change your MP3 or sync to the cloud, and resets at the next song.
- **Readability first**: cover images fill the window behind a dark overlay; the v0.3.2 pre-wrapped lyrics and highlighting remain foreground content. Without artwork, the existing dark-gradient background remains.

## Retained from v0.3.2

- Complete lyric wrapping is calculated at the final highlighted font size ahead of time so incoming text never snaps from one line into two at the last moment.
- Consistent lyric spacing and smooth 340 ms vertical progression.
- LRCLIB synced lyrics and manual LRC files, with optional enhanced LRC word timestamps when supplied.
- Read-only Foobar2000 playback position, pause, seeking and second-screen fullscreen karaoke.
- Saved **per-song timing corrections**, including the previously verified +2000 ms example.

## First test

1. Keep your known-good v0.3.2 extracted folder and the existing Foobar2000 x64 Bridge component.
2. Download **Melisma-Windows-Lyrics-Preview-v0.4.2-x64** from the GitHub Actions artifact for this branch and extract to a **different folder**.
3. Launch `MelismaLyricsViewer.exe` and start a track in Foobar2000. With **Album art background** selected, check whether the cover appears behind the lyrics.
4. Choose a 1-second fade, then a 6-second fade, and change songs to compare. If no automatic art appears, use **Open cover image** to test the rendering and transitions with a local image.
5. Test **Show on second monitor** and verify the full-screen TV display has the same darkened background and still displays pre-wrapped lyrics.
6. Uncheck **Album art background** to restore the original dark-gradient-only view; close and reopen the app to confirm on/off and fade length persist.
7. Verify timing adjustment, track changes, pauses, seeks and lyric layout are unchanged.

## Notes

- Art and fade preferences: `%LOCALAPPDATA%\MelismaUniversal\WindowsLyricsViewer\album-art.json`.
- Saved timing corrections: `%LOCALAPPDATA%\MelismaUniversal\WindowsLyricsViewer\timing-offsets.json`.
- Artwork from Windows comes from the existing media-session **Thumbnail** property; it is not fetched from LRCLIB or external artwork sites. If foobar2000 does not publish a thumbnail, automatic artwork cannot be displayed until we add a different source.
- Manual images are in memory only. We do not modify MP3 tags, change Foobar2000 playback controls or touch the Stem Separator.
- Album art is a local-only display feature. Firebase Cloud Sync and Stem Separator controls are not included yet.
- Windows 11 x64, .NET 8 self-contained. This is a test build, not a validated release.

Source: `tools/windows-lyrics-viewer/`.
