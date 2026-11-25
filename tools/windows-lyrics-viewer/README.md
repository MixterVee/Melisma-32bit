# Melisma Universal — Windows v0.5 (test build)

Windows 11 x64 · foobar2000 · synchronized lyrics on your laptop and second monitor

## New: real display settings, grounded in Melisma Android TV

Open **Display settings…** on the controller. Changes apply immediately to the controller preview and to the clean TV/second-monitor display. Settings are automatically saved locally.

**View**
- **Layout**: Lyrics (full-width) or Cinema (cover art plus lyrics)
- **Artwork on the**: Left or Right (landscape), matching Android's Cinema 42% media panel / 58% lyrics proportions
- **Show album art**: toggles image display; playback and lyric timing are unaffected
- Cinema panel includes album cover, current title and artist

**Background**
- **Style**: Living, Living (classic), Auto, Cover art, Artist (cover fallback), Colour, Black
- **Cover blur**: 0–67 (downsample/upscale approach as on Android)
- **Background transition**: 1.0–10.0 seconds in 0.2-second steps, kept in the existing artwork settings
- Backgrounds and the separate Cinema cover panel are independently faded (cover panel: 3.4 seconds)

**Lyrics display**
- **Text size**: 0.7–1.6x
- **Font**: System, Sans, Condensed, Serif, Mono
- **Depth effect**: adds a subtle secondary shadow to dim lyrics
- **Simple mode**: calmer font-size emphasis
- **Minimal mode**: hides already sung lines
- Lyrics continue to be pre-wrapped before entering the highlighted position; word-timed enhanced LRC continues to use the original playback clock.

### Scope and current limitations

The new Windows background settings use **lightweight GDI+ approximations** for Living and Living (classic), rather than a port of Android's Compose/Kawarp shader. Auto currently selects Living. Artist currently falls back to Cover art because Windows does not yet fetch artist photos. Spotify Canvas video playback and extra online album-art sources are **not implemented** in this build; no misleading inactive controls are shown.

Word-by-word progress still requires word-timed enhanced LRC. LRCLIB's ordinary line-timed lyrics remain line-based. The Android lyric-provider pipeline and full syllable-following renderer are planned separately. Stem Separator Karaoke Mode and Firebase Sync are not yet integrated.

## Install and test

1. **Keep the verified v0.4.2 folder as a backup.** Leave the working foobar2000 x64 bridge installed.
2. Download **Melisma-Universal-Windows-v0.5-x64** from the GitHub Actions run for this feature branch. Extract it into a **new folder**.
3. Start foobar2000 playing a song and launch \`MelismaLyricsViewer.exe\`.
4. Click **Display settings…**, select **View → Cinema**, then switch artwork between **Left** and **Right**. Confirm no lyric lines extend into the cover panel.
5. Choose **View → Lyrics** to return to full-width synchronized lyrics.
6. Select **Background → Black**, **Colour**, **Cover art** and **Living**. Use **Cover blur** with Cover art; compare 0 with ~40. If no cover is available through Windows, use **Open cover image**.
7. Try fade durations of 1.0 and 6.0 seconds and switch songs.
8. Try a larger font (e.g. 1.2x), Minimal mode, and text font selections. Verify long lyrics are *already wrapped before* being highlighted.
9. Click **Show on second monitor** and confirm the layout/background changes apply to the TV while the controller stays on the laptop.
10. Close and reopen both viewer and settings: verify layout, background, lyric preferences and your **per-song +2000 ms** adjustment persist. Pause/seek should still follow the foobar2000 bridge.

## Settings files

- Display settings: \`%LOCALAPPDATA%\MelismaUniversal\WindowsLyricsViewer\display-settings.json\`
- Artwork enabled and transitions: \`%LOCALAPPDATA%\MelismaUniversal\WindowsLyricsViewer\album-art.json\`
- Per-song lyric offsets: \`%LOCALAPPDATA%\MelismaUniversal\WindowsLyricsViewer\timing-offsets.json\`

No MP3 tags/audio files are edited. The foobar2000 bridge is read-only; Stem Separator processing is separate.

## Android reference sources

- \`MelismaApp/melisma\` tag \`v2.11.0\`: \`SettingsStore.kt\`, \`SettingsSheet.kt\`, \`PlayerScreen.kt\`, \`DynamicBackground.kt\`
- \`MixterVee/Melisma-32bit\`: Android TV patches, including \`patch_tv_v27.py\` and \`patch_tv_v287.py\`

This is a new visual feature build requiring on-device testing, **not** a declaration of full Android settings parity.
