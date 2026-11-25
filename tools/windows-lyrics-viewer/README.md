# Melisma Universal — Windows v0.5.2 (test build)

## New: Living background after sustained inactivity

Melisma temporarily switches its full-window background to **Living** when the
foobar2000 Bridge is no longer playing for a while. Short track-change gaps
are ignored.

- **Display settings → Background → Use Living when playback is idle** is on by default.
- **Idle Living delay** can be selected from **10 to 15 seconds**; default is **12 seconds**.
- Paused, stopped, or unavailable playback for at least that long starts a gentle
  0.9-second transition into Living. Playback resuming fades back to the chosen
  background style over 0.9 seconds.
- Changing between songs quickly leaves the selected background untouched.
- The saved background style, blur, and independently adjustable background/cover
  fade settings are not changed.
- The Lyrics/Cinema layout, sharp album-cover panel, song metadata, synced lyrics,
  and saved song-specific correction remain in place.
- The laptop controller and TV share one inactivity timer.
- Without an available image, Living displays its fallback colour field.
- Turn the idle option off to prevent automatic background switching. The setting
  and delay are saved in the existing display-settings.json.

### First test

1. Extract the v0.5.2 ZIP to a new folder. Keep v0.5.1 as a backup.
2. Play a song in foobar2000 and choose Background = Black or Cover art.
3. Enable idle Living and set its delay to 12 seconds.
4. Pause playback for 5 seconds: the chosen background should stay.
5. Keep paused beyond 12 seconds: the background should fade to Living.
6. Resume: the chosen background should return, with the Cinema cover and lyrics intact.
7. Stop playback (or close foobar2000) long enough to trigger Living again.
8. Disable the idle feature to verify that it no longer switches; restart to
   confirm settings and timing corrections remain saved.

## Retained from v0.5.1

## New in v0.5.1 — independent Cinema cover-art transition

- **Display settings → Background → Cover art transition** adjusts the *separate Cinema album-cover panel* fade from **1 to 10 seconds**, in 0.2-second steps. Default is **3.4 seconds**, retaining the behavior of v0.5.
- The existing **Background transition** separately adjusts the full-screen background fade, also in 0.2-second steps. For example, a 6-second background and a 2-second cover fade work independently.
- Both settings are saved in `album-art.json` and restored when reopening Melisma. Existing v0.4/v0.5 settings files without the new cover-fade value automatically use 3.4 seconds.
- Changes preserve the working Lyrics/Cinema layout, cover blur, lyric wrapping, Foobar2000 synchronization, quiet footer, and song-specific lyric delays.

### First v0.5.1 test

1. Extract v0.5.1 into a new folder. Leave working v0.5 and v0.4.2 untouched.
2. In **Display settings → View**, select **Cinema** and show the album-cover panel.
3. In **Background**, set **Background transition = 6.0 seconds** and **Cover art transition = 2.0 seconds**.
4. Change to a song with different artwork. The small Cinema cover should fade in about 2 seconds while the full-window background continues fading for about 6 seconds.
5. Try a **Cover art transition = 8.0 seconds** to confirm the small album cover now fades more slowly, independently of the background.
6. Close/reopen the viewer and verify both values are remembered. Confirm that saved per-song timing corrections still work.

## Retained from v0.5

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
