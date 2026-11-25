# Melisma Universal — Windows v0.6.4 (test build)

## Fix: word-timed songs with missing opening verses

Your v0.6.3 screenshots proved the foobar2000 native playback clock was
healthy at 00:24 while the active lyric was **None**. A live source probe
found that the selected **Blinding Lights** TTML begins only at **01:00.964**,
and **Shake It Off** begins at **01:00.290**. AMLL and Better Lyrics supplied
similarly abbreviated documents. The earlier lyrics were missing from those
sources; the viewer was not frozen.

v0.6.4:
- Compares the first cue in every word-timed source with the LRCLIB
  line-timed document. Prefer a word provider that covers the first verse
  instead of a preferred provider that omits it.
- When a good word-timed document begins mid-song and LRCLIB has two or more
  earlier cues, insert only its **line-timed opening lyrics** before the
  word-timed first cue. Preserve all genuine TTML words and timestamps later.
- Never manufacture timestamps or claim that the restored opening verse
  has word-level timing. Status identifies **Word-synced + line-timed intro**.
- Source details reports the **first lyric cue time** for each provider and
  the number of early LRCLIB lines added when this repair was needed.
- Keep the **lrc.red attribution** when its words are used in the mixed source.
- Keep v0.6.3's healthy bridge clock, Cinema, Living, background, artwork
  transitions, and saved lyric timing offsets.

### Test

1. Extract v0.6.4 into a separate folder. Keep v0.6.3 as your backup.
2. Play **Blinding Lights — The Weeknd** and inspect **Source details…**.
   The report should show a word-timed first cue around 01:00 and a line-timed
   earlier LRCLIB cue, plus the message **Intro coverage repaired**.
3. At ~00:24, a lyric should now be active and follow the first verse, instead
   of displaying the inactive chorus.
4. From ~01:01, the original genuine word-level progress should take over.
   Pause, resume and seek to verify alignment.
5. Try **Shake It Off** (also a missing-first-minute word source) and
   **Shape of You** (starts near 00:10 and should need no repair).
6. Verify the clean second-screen lyric display and Living idle fade.

## Retained from v0.6.3

## New: playback-clock diagnostics and stale bridge recovery

The Windows viewer receives the song name, artist and album from Windows
Global System Media Transport Controls, but it uses the optional native
**Melisma Foobar2000 Bridge** to get the playback position. These are
different interfaces. The title and word-timed lyrics can load even if
the playback clock is unavailable. In that case the lyrics do not advance.

- The bottom status line now displays a **live playback position**, duration,
  playback state, and clock origin (Foobar bridge or Windows media timeline).
  If no usable clock is available, the warning is explicit.
- Click **Source details…** to see **PLAYBACK TIMING (live)**, including
  the clock source, current position, state, native shared-memory bridge
  freshness, Windows timeline availability and active lyric index.
  It still includes all lyrics-provider diagnostics below.
- The viewer detects a **stale native bridge snapshot** after 1.8 seconds.
  A stale memory-mapped view is released rather than being mistaken for a
  live, moving playback position. This permits later bridge reconnection.
- When the native bridge is missing or stale, the viewer can use the
  Windows media session's native timestamped playback position **if it is
  actually provided**. Foobar2000 configurations that publish metadata
  but no usable Windows timeline cannot be synchronized automatically.
- If *neither* clock works, the viewer shows a warning rather than making
  the word-synced data appear broken. It does not invent elapsed time from
  the song title or silently assume playback started at zero.
- Playback state from Windows still prevents the Living idle screensaver
  from covering lyrics merely because the native bridge is disconnected.
- Automated tests verify fresh, stale, missing and invalid native sample
  timestamps. All prior lyric provider and Cinema/Living features remain.

### First test: verify the moving clock

1. Keep v0.6.2 as a backup, extract v0.6.3 into a separate folder, and
   start foobar2000 and Melisma.
2. Play **Blinding Lights — The Weeknd** and watch the bottom status line.
   The counter **must advance**. Word-synced lyrics cannot work until it does.
3. If it says **Playback position UNAVAILABLE**, click **Source details…**.
   The top section identifies whether the Foobar bridge is missing, stale,
   or whether the Windows media session has a usable timeline.
4. If the direct bridge is missing or stale, check **Foobar2000 → File →
   Preferences → Components** for **Melisma Foobar2000 Bridge**. Restart
   foobar2000 to reinitialize the component if necessary.
5. Confirm pause stops the counter, resume continues, seek jumps to the
   selected position, and cyan word highlighting tracks the counter.
6. Restart the viewer while music plays. Both the bridge clock and word
   timing should reconnect without requiring a lyrics provider lookup.
7. Test Cinema + second monitor and Living idle fade. Previous source
   selection, background, transitions and timing offsets should persist.

### Limitation

Windows cannot calculate a reliable exact playback position if foobar2000
does not expose one. In that case the native Foobar2000 Bridge component is
required; selecting a word-timed lyric provider alone does not provide a clock.

## Retained from v0.6.2

## Fix: follow the working Android TV Melisma lyrics pipeline

Windows v0.6/v0.6.1 searched sources in series and could silently lose
genuinely timed lyrics. This version ports key architecture from the Android
Melisma v2.11.0 app rather than inventing another heuristic.

- **Ask all accessible lyric providers in parallel**, not one after another:
  **lrc.red**, **AMLL TTML DB**, **Better Lyrics TTML/QQ**, and **LRCLIB**.
  Each has a 12-second timeout so one unavailable server cannot block another.
- **Direct lrc.red support**, ported from Android's LrcRedProvider:
  search the song and primary artist, rank the returned recording matches
  with Android-style title/artist/duration scoring, retrieve by **ISRC**
  and parse the genuine word/syllable-timed TTML. The public source often
  contains songs absent from AMLL.
- **Quality ranking** follows Android's key rules: real word timing beats
  line timing; at least half of the vocal lines must be word-timed, and
  fragments with fewer than 60% of the fullest answer's lines are demoted.
  For otherwise comparable sources, lrc.red takes priority over AMLL, then
  Better Lyrics, then LRCLIB.
- **No fabricated timings.** Songs that have only actual line timestamps
  continue showing line-synced lyrics.
- **Source details…** button reports the exact Foobar2000 title, artist,
  album, duration, the result of every queried provider (including errors
  and timeouts), and which source was selected. This enables diagnosing
  metadata problems without guessing.
- lrc.red requires visible attribution. When it supplies the lyrics, the
  TV lyrics view includes **Lyrics: lrc.red · https://lrc.red**.
- Improved QQ QRC decoding: can recover the timed lyric text even when
  the returned XML wrapper is malformed.
- Preserves the original foobar2000 Bridge synchronization, saved lyric
  delay, Cinema layouts, seamless backgrounds, artwork transitions and
  full-screen Living screensaver.

### How to verify the coverage fix

1. Keep v0.6.1 as a backup. Download and extract the v0.6.2 Windows x64 ZIP
   to a **new folder**, then start foobar2000 and the viewer.
2. Play **Blinding Lights — The Weeknd**, **Shape of You — Ed Sheeran**, or
   **Shake It Off — Taylor Swift**. A live GitHub runner found real timed
   TTML for these three songs through lrc.red.
3. Check the controller status: it should say **Word-synced · lrc.red**
   (or another genuine word-timed source). Pause and seek to test that the
   highlighting tracks the existing Foobar2000 timeline.
4. Click **Source details…**. It should show the real metadata being sent
   to the lyric providers, each provider's result, and the selected source.
5. Re-test five previously line-synced tracks. If any remain line-synced,
   inspect **Source details…** rather than retrying unknown settings.
6. Verify lyrics and Cinema still display correctly on the TV, including
   full-screen Living during long pauses and saved +100/-100ms adjustments.

### What is—and is not—ported from Android

The **parallel provider design**, **word-first ranking**, **matching score**
and **lrc.red ISRC path** are grounded in Melisma Android TV's source.
Windows still does not have every Android provider and feature: NetEase YRC,
Apple Music account-backed lyrics, Spotify token-backed lyrics,
Musixmatch, language annotations, full provider selection/order settings
and the cross-device lyric cache remain future work.

The free Better Lyrics API returns **401 on some uncached songs**, including
its QQ route. It is only an additional source, not relied upon for coverage.

Android source references:
- https://github.com/MelismaApp/melisma/blob/v2.11.0/app/src/main/java/com/melisma/app/lyrics/LyricsRepository.kt
- https://github.com/MelismaApp/melisma/blob/v2.11.0/app/src/main/java/com/melisma/app/lyrics/provider/LrcRedProvider.kt
- https://github.com/MelismaApp/melisma/blob/v2.11.0/app/src/main/java/com/melisma/app/lyrics/provider/LyricsProvider.kt

## Retained from v0.6.1

## Fix: vastly overrestrictive v0.6 word-lyrics matching

In v0.6, AMLL was checked with a **combined title+artist** query and then
the returned title/artist were compared almost exactly. The API uses
intersection (AND) matching for multiple search filters, so this discarded
many legitimate word-timed files whose metadata included featured artists,
release variants, or different artist/title forms.

v0.6.1 remedies the application-side under-detection. It does **not** create
fake word timestamps.

- **AMLL TTML**: searches first by title plus primary artist, then more
  broadly by title alone, but still verifies the actual candidate's title
  and primary artist before accepting it.
- Supports safe alternate-title variants (e.g. remastered/release labels,
  featuring credits). Continues rejecting unrelated titles and artists.
- Handles TTML tracks with *some* genuinely word-timed lines mixed with
  single-word or line-timed lines. The previous 50% threshold threw away
  otherwise useful matches.
- If AMLL has no suitable word timing, it now checks **Better Lyrics TTML**
  and then **Better Lyrics QQ QRC** (documented word-timing provider);
  each parsed word is based on source start/end timestamps.
- The QQ parser handles the documented XML envelope, absolute
  word-start/duration tokens, and skips song-title cards and lyric credits.
- Reassures against matching the wrong recording: checks QQ embedded artist/
  title metadata when available and rejects lyrics running beyond the track
  duration by more than 8 seconds.
- If none of these sources has real word timing, the existing reliable
  LRCLIB line-timed fallback remains intact, with no estimated word timing.
- Status labels show which word-timed source supplied the data.
- Automated smoke tests now exercise AMLL release variants and mismatch
  rejection plus QQ XML word timing, lyric credits and duration checks.
- All v0.5.4 Cinema layout, blurred backgrounds, album transitions,
  full-screen Living screensaver and offset preferences remain unchanged.

### Test plan

1. Keep v0.6 as a backup and extract v0.6.1 to a fresh folder. Leave
   the already-working foobar2000 bridge installed.
2. Try the same songs that were line-synced in v0.6.
3. Watch the status strip: successful results are marked
   **Word-synced · AMLL TTML DB** or **Word-synced · Better Lyrics · TTML/QQ**.
4. Pause and seek: verify actual words/partial words follow the song
   position, and the existing +100/-100ms correction still works.
5. Songs that still fall back to **Line-synced · LRCLIB** may genuinely
   lack available word timing. Provider uptime, rate limits and metadata
   still affect real-world coverage. No percentage improvement is promised.
6. Try one long line in Cinema and the full-screen Living idle fade to
   verify the earlier features are unchanged.

### Source availability notes

- AMLL public TTML database: https://amll.dev/reference/http-api/overview.html
- Better Lyrics TTML/QQ service:
  https://docs.betterlyrics.org/docs/providers
- Better Lyrics' TTML lookup may require an API key for **uncached** songs.
  The public cached lookup is best effort. QQ word timing is documented as
  accessible without an API key. Network conditions may still vary.
- This version does not integrate any service requiring the user's private
  credentials or secretly substitute generated/predicted word timestamps.

## Retained from v0.6

## New: genuine word-by-word lyric synchronization

The Windows Foobar2000 viewer now automatically attempts **AMLL TTML DB**, the same
account-free, community-timed provider used by Android Melisma. Only a strictly
matched title+artist with real TTML word/syllable timestamps is accepted. When no
genuine word-timed lyrics are available or the provider is offline, Windows falls
back automatically to the proven **LRCLIB line-synchronized lyrics**.

- **Real timestamps, not simulated timing**: uses each TTML word start/end
  for syllable/word progression; if only onset times are provided, highlighting
  steps forward on the real onset. Does not spread words artificially over a
  line's duration.
- **Smooth word fill**, enabled by default, gradually fills the current syllable
  from its actual begin to end timestamp. Turn it off under
  **Display settings → Lyrics display → Smooth word fill (real timestamps)**
  for discrete word-by-word steps.
- **Open lyrics file** now supports **TTML (.ttml/.xml)** and existing
  **enhanced-LRC (.lrc)**. The parser accepts explicit word timings, nested
  span groups, seconds/millisecond clock notation, and safe
  fallback to line mode if a file lacks usable word timing.
- The controller status shows **Word-synced · AMLL TTML DB** or
  **Line-synced · LRCLIB (no word timestamps)**, so the viewer never pretends
  ordinary line-timed lyrics are word-timed.
- Plays, pauses, seeks, track changes and saved +100/-100ms per-song timing
  all use the unchanged read-only Foobar2000 bridge. Accurate word highlighting
  uses the **same corrected timeline** as the line highlighting.
- New CI smoke tests cover real TTML word start/end timings, line-only
  fallback, whitespace between timed spans, nested spans, onset-only tokens
  and secure XML parsing. No internet or test song is needed for those tests.

### How to test v0.6

1. Keep v0.5.4 extracted separately. Download the
   **Melisma-Universal-Windows-v0.6-x64** artifact and extract it to a new
   folder. Leave the working Foobar2000 bridge installed.
2. Start a tagged song in Foobar2000; v0.6 searches AMLL for word-timed
   lyrics first. Watch the upper status strip:
   **Word-synced** means a real timed match, while **Line-synced** is the
   expected fallback for songs without such a match.
3. For a word-timed match, watch words fill progressively within the active
   cyan lyric line. Pause: it must freeze exactly; resume: it continues; seek:
   progress jumps to the correct word. Adjust the existing +/-100ms controls
   to correct a lyric file with consistent timing drift.
4. For a line-synced song, confirm whole lines continue highlighting with no
   fabricated word timing. This is **not an error**.
5. To test independently of online coverage, click **Open lyrics file** and
   load a properly word-timed .ttml file. The header should switch to
   **Word-timed lyrics · Local file** if it contains enough genuine spans.
6. Open **Display settings → Lyrics display** and toggle **Smooth word fill**.
   With it off, the active word switches colour on its actual onset timestamp;
   with it on, the timed word fills smoothly over its recorded duration.
7. Test the laptop preview and extended-screen TV in Cinema and Lyrics layouts,
   including long wrapped lyrics, and the Living idle screensaver. Verify
   saved layout/background preferences and per-song offsets remain intact.

### Availability and current limitations

Word-by-word is available **only where the source genuinely contains word
timestamps**. Not all AMLL records are the same recordings, and many popular
tracks in LRCLIB have only line times. To avoid showing the wrong lyrics we
require strict title/artist matching before using AMLL TTML. A manual TTML
file is supported for songs not found automatically.

This stage does not yet port every Android Melisma provider (Apple Music
credentials, NetEase YRC, Musixmatch, etc.), timed backing-vocal/duet
layout, phoneme timing, translations/romanization, or full shader-based
syllable animations. Those require separate integration.

AMLL TTML DB: https://github.com/amll-dev/amll-ttml-db and
https://amll.dev/reference/http-api/overview.html

## Retained from v0.5.4

## New in v0.5.4 — seamless Cinema background

- Removed the semi-transparent dark rectangle behind the artwork/metadata column in **Cinema** layout.
- Removed the thin divider line between the 42% album-art panel and 58% lyrics region.
- All background styles (Black, Cover art, Colour, Living, etc.) now flow continuously **across the entire window**, behind both artwork and lyrics.
- Kept the album cover, title and artist positioned in their column; tiny text-only drop shadows keep titles readable over lighter backgrounds without dimming an entire side of the screen.
- Preserved the v0.5.3 full-screen fluid Living screensaver, shared in/out fade duration and saved idle settings.

### Test

1. Extract v0.5.4 into a new folder; keep v0.5.3 as backup.
2. Switch to **View → Cinema** and select **Background → Cover art**, as shown in the report.
3. Check that there is **no darker vertical bar** behind the cover on either side of the screen and **no vertical dividing line**. The background should be the same brightness across both columns.
4. Change **Artwork on the** from Left to Right. Confirm the seamless background stays intact.
5. Check Black and Living backgrounds, and confirm lyrics still stay within their allotted right/left column.
6. Pause foobar2000 long enough to trigger full-screen Living; resume and verify artwork and lyrics reappear at the correct point using the existing fade setting.

## Retained from v0.5.3

## New: full-screen fluid Living idle screensaver

The previous idle feature changed only the background and left Cinema artwork,
song metadata and lyrics on top. v0.5.3 changes this deliberately:
**after 10–15 seconds without music, Living takes over the entire lyrics canvas**,
covering the Cinema panel, song information and lyrics. It is a screensaver, not
just another layer behind the lyrics.

- A new low-resolution organic, domain-warped colour-cloud renderer creates
  gently evolving liquid/smoke-like shapes using colours partially drawn from
  album art. It does **not** move a recognisable blurred cover image around.
- **Full-screen Living when idle**: on by default, under Display settings →
  Background. Applies to both laptop lyrics preview and the clean TV display.
  Normal Windows controller buttons remain accessible while idle.
- **Idle Living delay**: 10–15 seconds, default 12. Brief pauses and gaps
  between tracks never activate the screensaver.
- **Idle Living fade (in/out)**: one shared speed for both entrance and exit,
  adjustable **2–12 seconds**, default **6 seconds**. Living smoothly covers
  the entire display in 6 seconds, then fades out over the same 6 seconds
  when playback resumes, revealing lyrics at their **actual live playback position**.
- Foobar2000's read-only bridge and lyric timing continue to update while
  the screensaver covers the display; no seeking, restarting or freezing.
- Your saved Lyrics/Cinema layout, normal background style, background fade,
  independent Cinema cover fade and per-song lyric delay are preserved.
- The *normal* Living background also uses the new abstract fluid renderer;
  Living (classic) remains the older moving-gradient approximation.
- If there is no cover image, the fluid screensaver uses a calm fallback
  blue/purple/teal colour palette.

### First test

1. Download the v0.5.3 Windows artifact, extract into its own folder and keep
   v0.5.2 as a backup.
2. Select **View → Cinema**, set the normal **Background → Style** to Black
   (for an obvious contrast).
3. Enable **Full-screen Living when idle**, set **Idle Living delay = 12 seconds**,
   and **Idle Living fade (in/out) = 6.0 seconds**.
4. Play a song and pause for only 5 seconds: the normal display should stay.
5. Keep paused for more than 12 seconds: abstract fluid Living should gradually
   cover the *entire canvas* in about 6 seconds, hiding even the sharp album-cover panel and lyrics.
6. Resume foobar2000: over about 6 seconds, the full Cinema display and lyrics
   should gradually reappear, synchronized to the correct point in the song.
7. Stop music or wait between tracks for 12+ seconds: Living returns.
   Switch tracks within 12 seconds: no screensaver.
8. Turn off the idle option to disable the screensaver. Restart the app
   to confirm settings and song-specific lyric corrections persist.

## Retained from v0.5.2

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
