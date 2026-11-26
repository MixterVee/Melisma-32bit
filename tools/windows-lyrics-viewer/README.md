# Melisma Universal Windows v0.6.23 — actual on-screen crossfades

## User feedback
Windows v0.6.22 **still switches songs visually too abruptly**, despite the earlier fix preserving the outgoing cover in memory. Treat this as a regression: passing opacity math tests without testing the **actual rendered screen** was insufficient.

## Two additional defects confirmed in source

1. **The default Auto → Living background bypassed the fade.** The painting code fed the fluid renderer `_artwork.Current` immediately. The renderer detected a changed image and instantly replaced its entire palette; user-selected background FadeSeconds was not used. v0.6.23 now gives the renderer outgoing and incoming cover references plus its actual user-selected progress. It interpolates all four palette colours over that progress, including full-screen idle Living, while continuing to render at the existing **10 fps cap**, with no extra animation timer or CPU-heavy redraw.
2. **Native artwork sometimes processed twice on the same song.** During a new Foobar generation, the native cover callback can arrive before the async title/artist metadata. The earlier logic accepts the new bitmap, then resets `_nativeCoverSequence=-1` when metadata arrives, accepts the same bitmap again and moves it into both Previous and Current. The user then sees an immediate jump or a same-image fade. v0.6.23 keeps the already accepted generation and native sequence when metadata confirms the same song. It also fingerprints artwork bytes so repeated Foobar or catalog callbacks containing identical art cannot reset the cover transition.

### Preserved behavior

- Foobar Bridge **v0.4** restart safety: invalidates old song lyrics immediately when playback stops/disconnects and rejects the old song's metadata on new audio.
- Cover-panel and background fade durations remain separately configurable in **Cover…**, 1–10 seconds; new art can arrive before or after the metadata, but accepted artwork is not repeatedly installed.
- Cached lyrics still get a distinct 750ms fade-in for each track (as added in v0.6.22).
- All v0.6.19 animation styles, v0.6.20 English spacing, three-dot timing, lyric source timestamps, native track artwork priority and CPU budget remain unchanged.

## Manual test — verify both actual visual surfaces

1. Extract Windows v0.6.23 into a new folder; **keep Bridge v0.4** installed. In **Cover…** choose a clearly observable longer transition (e.g. **6 seconds**) for **both background and cover**. This is a test setting, not a new default.
2. In **Display…**, use **Cinema** with background **Living** (also test Auto, which resolves to Living). Play *Travelin' Soldier* and immediately switch to a song with a contrasting cover. Watch both the album square and the surrounding moving colours. The background must *blend*, not instantly take on the new colours, and the album cover must reveal old → new.
3. Try **Lyrics-only** mode and another background style. Then repeat close Foobar mid-song and reopen another MP3 quickly; previous lyrics must clear for safety while the new cover/background transition remains smooth.
4. Watch a track with no cover and repeated same-album tracks. Cover data should neither stick forever nor reset the fade on duplicate native callbacks. Replaying the same track should preserve accurate sync and show a fresh lyric fade.
5. If the UI still jumps, provide a short video showing **the whole display during a direct track change** and **Sources…**. The current code now has independent tests for the actual drawn Living palette, not merely the fade formula.

### New automated visual regression

The Windows test harness draws two distinct solid-colour album covers through the **production FluidLivingRenderer**, samples the actual output pixels at 0%, 50%, and 100% transition, and checks for gradual intermediate colours. Additional tests verify same-generation metadata cannot reset an accepted native cover and identical artwork bytes yield an equal fingerprint.

**No Foobar component upgrade for this build.**

---

# Melisma Universal Windows v0.6.22 — restore smooth transitions after restart-safety fix

## User report
The user confirmed that v0.6.21 fixed the serious bug where *Travelin' Soldier* lyrics followed a different song after Foobar2000 closed/reopened. However, **the pleasing fade-in and cover-to-cover transitions disappeared** and changing tracks became abrupt.

## Fix: keep playback identity strict, presentation transitions gentle

- **Preserve outgoing cover on quick stop/reopen:** v0.6.21 invalidates playback on Foobar stop by calling `AlbumArtworkState.Replace(null)`. That moves the old cover into `Previous` and sets `Current=null`. When the next song's image arrives, the old `Replace()` disposed `Previous` and put the new cover over blank rather than fading old → new.
- The cover transition now **transfers the outgoing image** into the next transition if either the user-selected **background fade** or **Cinema cover fade** remains active. The incoming cover fades over the old cover at the configured rate. If both fade windows have expired, the old cover is discarded, so an unrelated new song cannot indefinitely inherit old artwork.
- When no cover is available for the next track, the old image fades out to the placeholder. Repeated null-art responses do not reset the fade timer.
- **New lyrics always fade in:** a unique per-playback visual epoch is advanced whenever the native track identity is invalidated. The 750ms lyric reveal now includes that epoch in its document key, so even *the same song replayed from disk cache* has a fresh readable fade when its next playback starts.
- **Safety retained:** closing/stopping Foobar still cancels the prior lyric lookup, clears old text, and rejects stale Foobar/Windows metadata. The old lyrics are NEVER animated under the next song. Foobar Bridge v0.4 remains required for robust restart handling; **no bridge update for this particular release**.
- No change to the five v0.6.19 animation styles, v0.6.20 word spacing, Android three-dot landing, lyric timestamp offsets, or the metadata/caching strategy.

### Test sequence

1. Extract Melisma Windows v0.6.22 separately; keep Foobar Bridge v0.4 installed.
2. Play *Travelin' Soldier*, then switch directly to another song with a different album cover. New art should slowly reveal **over** the outgoing artwork, using the times set in **Cover…**. The new song's lyrics should softly fade in even when they load immediately from cache.
3. Repeat the exact previous regression: close Foobar2000 mid-song, immediately open a different MP3, and watch the cover-to-cover transition. The previous lyrics must clear right away and the next song's correct lyrics must appear with a fresh fade.
4. Let Foobar remain closed longer than both configured artwork transition durations, then reopen a different track: the old cover must *not* return. Check a song with no embedded art too.
5. Replay the same song twice, pause/resume, and use the second screen to confirm consistent transitions without shifting synchronized word highlighting.

Automated checks cover preservation of outgoing artwork only within configured fade windows and playback-epoch-triggered lyric reveal. The real Windows animations still need your visual confirmation.

---

# Melisma Universal Windows v0.6.21 — Foobar restart session safety

**Reproduction:** The user played *Travelin' Soldier*, closed Foobar2000, opened another track, and Melisma resumed scrolling *Travelin' Soldier* lyrics to the new audio.

**Root causes:** Closing Foobar can leave three old shared-memory mappings open in the Windows viewer. When Foobar launches again, a new process could reuse the old bridge's per-process track generation and Windows GSMTC could report a previous song's title. The old bridge v0.3 also refused to publish anything if the prior instance's named mapping was still alive through the reader's handle.

**Windows v0.6.21**
- Treat Foobar stop, bridge loss, and a track-generation change as hard lyric invalidation boundaries. Cancel old lookups and clear old lyric lines, track identity, plain text and offset before presenting new lyrics.
- Dispose *all three* native mappings—timeline, tags, artwork—when the bridge disappears. Do not retain a disconnected process's tags or album art.
- Require both a **fresh Foobar playback clock and native song tags whose generation matches the clock** before loading lyrics. Once a native Foobar bridge was observed, delayed Windows GSMTC metadata can never resurrect the old song after restart.
- An actual paused song remains displayed (no unnecessary clearing), and the usual native track-change path reloads immediately when tags are ready.
- Older Windows-only setups still have their prior fallback behavior if no native bridge has ever been connected.

**Foobar Bridge v0.4 REQUIRED for fully reliable rapid restarts.** It now retries instead of permanently failing when the previous shared-memory map is still held by Melisma. It can reclaim the stale mapping after the old process's heartbeat has stopped, and generates distinct track identity across Foobar process restarts. Existing timeline/tag/art formats remain compatible.

### Acceptance test
1. Install Foobar Bridge v0.4 through Foobar2000 → Preferences → Components → Install; restart Foobar. Extract Melisma Windows v0.6.21 to a separate folder.
2. Play *Travelin' Soldier* until its word highlighting and animation are visible. **Close Foobar2000 while it is playing**. Melisma should stop and clear the old lyric document, not continue scrolling it.
3. Open a different MP3 from Explorer, which restarts Foobar2000. Melisma should display the new song title, retrieve its own lyrics, and use its own playback position.
4. Repeat with a *rapid* close/reopen, then pause/resume and skip among multiple different tracks. Never display *Travelin' Soldier* lyrics on another song.
5. Check **Sources…**: native tags and playback generation must refer to the current track; old cached lyrics must not be reused on a different song. The saved cache stays intact, and v0.6.20 spacing, v0.6.19 animation styles, v0.6.18 third-dot timing and album-cover improvements remain preserved.

This is an automated-test build until confirmed by the user's actual Foobar playback.

---

# Melisma Universal Windows v0.6.20 — missing English word spaces

## Report

With v0.6.19 animations working well, **Travelin' Soldier — Dixie Chicks** displays lines like **“Hewaswaitin'forthe”** and **“businhisarmygreens”** without the spaces they need for readability. The font/animation itself should NOT be changed.

## Root cause and fix

- Some provider word-timing payloads preserve each syllable's start/end time but omit spaces between whole English words. Melisma Windows was concatenating those tokens as instructed by the source.
- The existing `LyricWordSpacing.Restore` could add spaces only when a **second synchronized lyric source** had a complete, matching line within 12 seconds. It ignored legitimate **plain** LRCLIB text as a spelling/spacing reference.
- **NetEase YRC** frequently comes with correctly spaced **LRC in the same HTTP response**. The provider now uses that own-track LRC to restore exactly matching YRC lines before returning/caching them.
- **Any word-timed provider:** When LRCLIB offers an exact matching plain-text lyric line, use only its **whitespace boundaries** to realign existing timed tokens. No guessing from dictionaries, no borrowed lyrics from other recordings, and no manufactured/scrambled timestamps. Repeated lines with conflicting space placement are deliberately ignored.
- **Old cache repair:** Existing successfully cached word-timed documents with suspiciously concatenated English segments still display immediately, but get **one background reference check per app session** instead of being skipped permanently. When matching text appears, fix only the visible line's spaces and update the on-disk cache. Replays then remain instant.
- If no trustworthy matching reference is found, the text remains unchanged rather than splitting legitimate syllables incorrectly. Open **Sources…** for a diagnostic note explaining why the spacing could not be restored.

### Verify

1. Keep v0.6.19 as backup, extract v0.6.20 separately, and play **Travelin' Soldier** from the beginning. Some cached lines may initially be joined while the background reference check runs; once a matching reference arrives they should become readable without altering cyan word motion.
2. Replay the song: corrected spacing should load immediately from disk; check **Sources…** for spacing repair details.
3. Test **…Ready For It?** and **Edamame** for already well-spaced lyrics, fast startup, word animations, final three-dot landing and album art. Those effects should be unchanged.
4. If lines remain collapsed, send **Sources…** diagnostics so we can distinguish an unavailable reference source from a particular provider's malformed words. A screenshot alone does not identify the source.

Automated tests cover the user's joined-word example, exact normalized text matching, ambiguity rejection, and identical original word timestamps. Visual testing on Windows remains required.

---

# Melisma Universal Windows v0.6.19 — selectable karaoke animations (test build)

Build on the user-approved **v0.6.18** lyrics / third-dot / scrolling baseline. The new effects only decorate currently sung, real word-timed lyrics. No word timestamps, audio, album art, song-offset values or MP3 files change.

### Select the style

Open **Display… → Lyrics display → Animation style**. The selector saves immediately to the normal per-user Windows display settings and affects both the controller preview and TV second-screen projection.

| Style | Behavior |
|---|---|
| **Android-style enlargement** (default) | The existing v0.6.16 original-glyph, pivot-anchored spring enlargement is unchanged |
| **Bouncing ball** | A cyan ball travels smoothly from the previous word and bounces above the current word, touching down before the next word |
| **Underline** | A short, softly glowing cyan underline moves from left to right beneath the actual active word |
| **Glow** | A soft halo brightens and recedes around the existing stationary word; the text remains crisp and the same size |
| **Highlight only** | Word/line cyan highlighting with no added word animation |

Style changes persist after restarting Windows Melisma and don't require a restart. Existing v0.6.18 installations with enlargement disabled migrate to Highlight only; otherwise Android-style enlargement remains the default. The old **Enlarge sung word** toggle is replaced by this selector to avoid conflicting controls.

### Sync and fallbacks

- **TTML/YRC and other true word-timed lyrics:** animation follows Foobar's playhead and source word onset/end; the existing smooth cyan word fill remains independently selectable.
- **Enhanced LRC with word onsets only:** movement is a short presentation pulse, without inventing lyric end timestamps.
- **Line-timed or plain text:** no fake word-level movement. Regular line highlighting or clearly labeled static text remains available.
- No change to Android-style three-dot interludes, their landing timing, 420ms after-gap scroll, centered Lyrics view, Cinema view, v0.6.13 native Foobar Bridge v0.3 artwork selection, or the adaptive CPU refresh.
- The animated glyph enlargement uses the exact established pivot renderer. Ball, underline, and glow **do not** resize or shift the lyric text itself.

### Acceptance checklist

1. Extract Windows v0.6.19 into a separate folder; retain v0.6.18 as a backup. Foobar Bridge v0.3 remains installed.
2. Play a genuinely word-timed song such as *…Ready For It?*, switch among all five styles while playing, and ensure each visibly responds to the sung word without altering the Foobar timing or scrolling.
3. For the bouncing ball, watch the ball travel between neighboring words and bounce once per word. For underline, check the sweep stays beneath its word. For glow, the word should remain stationary while its halo pulses. Highlight only should show no decorative motion.
4. Close and reopen Windows Melisma and confirm the last selected style was retained. Open the second screen and confirm it uses the same style.
5. Recheck the third dot's final landing and the scroll handoff in a long instrumental intro. Inspect CPU and cover-art crossfades for regressions.

**Limitations:** Automated tests cover the motion formulas and source timing; actual on-screen smoothness and clarity require visual confirmation on a Windows machine.

---

# Melisma Universal Windows v0.6.18 — hold position until third dot finishes

The user confirmed that v0.6.17's third dot **now drops**, but the lyric viewport scrolls upward **before that downward movement finishes**.

## Root cause

In `AndroidInterludeRow.Focus`, v0.6.17 began moving the viewport from the dotted interlude row toward the next vocal line **780 ms before the next line's source timestamp**. Meanwhile, the third dot did not reach its baseline until about **120 ms before that timestamp**. This made the dot appear to shift upward with the whole screen while still descending, despite its own motion being correct.

## Correction

- **Hold the interlude row at a fixed screen position** for the entire third-dot landing and its brief grounded hold. The dots still follow the exact song position, with no synthetic lyric timing.
- **Begin the upward lyric scroll at vocal onset, not 780 ms beforehand.** Use a 420-ms eased transition from the interlude row to the incoming line *after* the line starts. The cyan lyric highlighting remains tied to the original timestamp; only the camera/viewport position moves.
- **No snap at the timestamp:** An explicit post-interlude glide covers the first 420 ms of the new lyric, then joins the normal line position continuously, including after seeks/pauses.
- Retain v0.6.17's third-dot descent and last-110-ms fade, v0.6.16 stationary word enlargement, native Foobar album art, Android interlude row styling, and adaptive CPU usage.

### Real playback acceptance

1. On *…Ready For It?*, observe the gap before **“Knew I was a robber first…”**. Watch the **entire dot row**, not only the third dot. It must stay stationary while dot three comes down and settles.
2. The dots fade after landing; *only then* should the lyric column begin its smooth upward movement, at the actual lyric onset.
3. Verify cyan highlighting still follows the singer exactly; it must not be delayed by the 420-ms scrolling presentation.
4. Pause and seek around the last second of an intro/interlude and confirm the dot landing and scroll follow Foobar's playhead correctly.

Automated timing tests cover holding, early onset boundaries, smooth exit and no final snapping; final visual verification remains a live Windows comparison.

---

# Melisma Windows v0.6.17 — third-dot landing fix

The uploaded video `2026-11-25_20h26_02.mp4` shows the third interlude dot still raised just before the lyrics resume in v0.6.16, though the individual-word enlargement looks improved.

**Cause:** Windows copied Android's three timing windows, but hid the dot row 500ms before the next vocal while the third dot's vertical spring was still raised. It also faded out the dots before the landing could finish.

**Fix:** The Windows renderer now keeps the dot row visible through a smooth landing and fades it only in the final 110ms before the real next-line timestamp. The third dot's raised spring offset is eased down to its original baseline by 120ms before vocals. Android's one-third-gap timing, left-aligned dot row, and word motion remain unchanged. No lyric timestamps, audio, track delay or files are altered.

**Test:** Play `…Ready For It?` through the gap immediately before “Knew I was a robber first…” (about 26–31 seconds into the submitted clip). The third dot should visibly lower onto the same baseline as the other two before the first word turns cyan. Also test pausing and seeking near the end of another long instrumental introduction.

This is a CI test build; the actual Windows animation still requires visual confirmation.

---

# Melisma Universal Windows v0.6.16 — stationary word enlargement

## Correct the lateral drift still visible in v0.6.15

The user supplied **2026-11-25_20h13_26.mp4**, showing a 25-second segment of *…Ready For It?* in full-screen Lyrics mode. Individual sung words still appear to slide to the left during enlargement despite the previous numeric-centre correction.

### What was wrong

The v0.6.15 animation independently measured the extracted word, created a new larger font and re-rendered that word inside a rectangle calculated from string-prefix widths. Those independent font metrics do not exactly match the original complete-row glyph spacing and GDI+ aligned layout. Even though the drawn rectangle had a calculated fixed midpoint, the **actual letters** could shift.

### What changed

- **Scale the original rendered glyphs**, not a newly measured/re-laid-out string: use the unchanged complete lyric row, font, alignment and DrawString rectangle.
- **Measure the active characters inside the complete row** using GDI+ `MeasureCharacterRanges` to get the original word ink region and immutable X/Y pivot. The word's exclusion mask uses the same region.
- **Apply one uniform graphics transform around that pivot** (both X and Y), preserving the original kerning, baseline, horizontal centering and word position. Cyan progression is clipped within the transformed word region.
- **Single-render word**: the base row omits the word and the transformed full-row artwork is clipped only to those characters, preventing doubled text.
- **Preserve Android's subtle 0.95–1.0505× spring targets**, exact Foobar word timestamps, centered Lyrics mode from v0.6.15, Cinema, three instrumental dots, cached lyrics, and v0.6.13 Foobar Bridge v0.3 artwork.
- **Regression tests** assert every font scale expands equally in all directions around an unchanged glyph centre. These are geometry tests; actual GDI+ output still needs visual verification.

### Manual acceptance test

1. Keep Foobar Bridge v0.3 installed and extract v0.6.16 into a separate folder.
2. In **Display → View → Layout**, use **Lyrics** and enable **Enlarge sung word (Android style)**.
3. Play *…Ready For It?*, especially the line *In the middle of the night, in my dreams* and the first chorus. Watch the middle of each growing word. The centre must remain stationary while its ink expands equally left/right and up/down, with no text double-exposure.
4. Watch multi-row centered lyrics and Cinema separately. Surrounding words should remain still except for the intentional scrolling of the lyric rows.
5. Compare CPU use, sync, and coverage with v0.6.15. This change does not request new lyric providers or affect MP3 data.

---

# Melisma Universal — Windows v0.6.15 (test build)

## Restore centered Lyrics mode and prove active words actually resize

Windows v0.6.14 changed text alignment to left throughout both display layouts while porting Android's spring/dot timing. The user wants **Lyrics mode centered**.

- **Lyrics mode:** independently center each wrapped synchronized lyric row across its full-width lyrics area; also center fallback plain-lyric lines.
- **Cinema mode:** preserve the left-aligned lyric column beside the album-cover panel.
- **Precise active-word enlargement:** the active word's base position is computed from the exact same centered row origin used for the normal lyric line and the exclusion mask. The larger font is drawn with the same fixed word center, so enlargement changes actual font size, not word position.
- **Measured size change:** Android's word-scale targets run from 0.95× through 1.0505× on held notes and settle back to 1.00×; a damped spring smooths the transition. Short words may show less growth, as on Android. The full lyric line is not resized or rewrapped.
- **Interlude positioning preserved:** Android-style timed dots remain in their reserved row before the upcoming lyric, aligned with the left edge of the first centered upcoming line in Lyrics mode. Cinema keeps existing left-column alignment.
- No Foobar bridge upgrade needed. Existing v0.6.14 providers, animation, CPU/adaptive redraw and v0.6.13 native artwork are unchanged.

### Test checklist
1. Select **Display → View → Layout → Lyrics**. Lyrics should be horizontally centered, including short lines; words must no longer drag toward the left.
2. Try **Cinema** and confirm lyrics remain beside the cover with their normal left-aligned column.
3. With **Enlarge sung word (Android style)** enabled, play a word-timed song with a few sustained words. Watch letters actually enlarge (typically a subtle 5% maximum) about a stationary centre. In Android's implementation the spring can settle short syllables before a large swell occurs.
4. Watch three dots on a song with an instrumental intro; in Lyrics mode their row should begin at the left edge of the centered upcoming lyric.
5. Recheck cover art, seek/pause and second-screen playback.

Automated layout tests validate centering and a stationary word centre, but final visual confirmation needs Windows playback.

---

# Melisma Universal — Windows v0.6.14 (Android lyric layout parity test)

## Source-based comparison (Android TV vs Windows)

User videos **2026-11-25_17h46_23.mp4 (Windows)** and **2026-11-25_17h59_28.mp4 (Android)** show that Windows' interlude dots were centered *below* the upcoming lyrics instead of being in a separate, left-aligned row **before** the next lyric. Windows also made inactive text only 67% of full-size and enlarged the entire active line, shifting its left edge instead of gently enlarging each sung word.

Reviewed Android's implementation, including `Lyrics.withInterludes`, `LyricsLayout.interludeLayout`, `LyricsRenderer.drawInterlude`, `LyricsAnim.wordScale`, and `core.Spring`. This build ports their key behaviors:

- **Interlude rows in the actual scrolling layout:** An intro or >=3-second break reserves a stable dot row before the next vocal. The three dots are left-aligned with the words, NOT horizontally centered or placed below upcoming text.
- **Android-staggered three dots:** Each dot uses its own one-third segment of the real instrumental gap; last dot ends 500ms before the next vocal. Shape, opacity, size, lift and time smoothing reference Android's source; no independent repeating 1.8-second spinner during a song.
- **Scroll focus around 42% of canvas height:** Dot row takes the focus during an interlude, smoothly transitioning to the next vocal line. Upcoming text remains visible and readable throughout, without premature cyan highlighting.
- **Stable real font size:** All lyric lines use the preplanned full-size font in a consistent left-aligned column, matching Android's lead-line layout. Windows no longer renders inactive lines at only 67% and scales entire active text blocks (the sideways-moving effect).
- **Smooth center-anchored active word zoom:** The sung word is masked from normal rendering and re-rendered once with Android's word-scale spline and damped spring. The word expands around its measured own centre, rather than moving the whole line left. Cyan sweep still obeys original Foobar and word timestamps.
- **Prior fixes preserved:** v0.6.13 embedded artwork priority (Foobar bridge v0.3), track-safe artwork, configurable cover transitions, v0.6.12 early lyric fade, improved provider selection and adaptive CPU rendering. No new Foobar bridge installation required.

### Manual acceptance

1. Start **.Ready For It — Taylor Swift** and watch the Android and Windows versions side by side. During an intro/gap, three dots should appear **before** the forthcoming lyric, close to the left edge of the lyric text column; upcoming lyrics remain visible beneath.
2. Watch how the active words grow: a steady left text edge, full-size inactive words, and spring-driven enlargement of only the active word. Test with long sustained words and short syllables.
3. Check a long instrumental pause, seek forward/backward, pause/resume, and the second-screen display.
4. Verify the per-track embedded cover remains correct, and compare CPU usage with v0.6.13.

This is a CI test build, so real Windows presentation still needs visual confirmation.

---

# Melisma Universal Windows v0.6.13 — correct Foobar artwork (test build)

## Why this update exists

On **.Ready For It — Taylor Swift**, the MP3's embedded 600×600 front cover (Taylor wearing a black hat with red band across eyes) differs from the **reputation** image Melisma displayed. Windows v0.6.12 used Windows GSMTC's player thumbnail as authoritative, which can be a cached/previous-song or different-cover image. Some tracks retained prior-song artwork even when they had no cover.

## Fix

- Requires **Foobar Bridge v0.3**, which publishes the same track's actual front-cover bytes from Foobar2000's central now-playing artwork service. This preserves the read-only v0.1 position and v0.2 metadata channels unchanged.
- The viewer reads the native artwork snapshot in a separate memory-mapped channel, with a generation number and consistent odd/even sequence guard. A cover from the *previous* track is never accepted as the current track's image.
- Native Foobar art takes priority over Windows GSMTC thumbnails and online iTunes art. When native artwork is missing, the viewer clears the prior track's cover and optionally checks catalog artwork. Stale async lookup results are invalidated when the track changes.
- During track changes, the outgoing art can remain **only as a transition** until the new cover is ready. Artless tracks cannot indefinitely show the old image under Living background or Cinema cover.
- Diagnostics show the artwork source: Foobar front cover vs optional catalog vs legacy Windows media thumbnail.
- Fix preserves v0.6.12's fade-in upcoming lyrics, dot animation, CPU budget, and controller window placement.

## Manual acceptance

1. Extract **Melisma Windows v0.6.13** in a separate folder.
2. Install **Foobar Bridge v0.3**: Foobar2000 → File → Preferences → Components → Install → `foo_melisma_bridge.fb2k-component`; restart Foobar2000. This replaces v0.2.
3. Play **.Ready For It — Taylor Swift**. Melisma should show the file's black-hat/red-band 600×600 *reputation* cover, not the black-and-white newspaper-style image shown by Windows earlier. In **Sources…**, confirm `Foobar2000 front cover (track matched)`.
4. Switch between two files with distinct covers, then one with no embedded cover. No image from the preceding track should remain indefinitely, even with Living background.
5. Try a track that previously showed no art, and test the user-selected album-cover transition duration with two embedded covers.
6. If a track still shows the wrong cover, inspect Foobar2000's now-playing artwork pane and compare with Properties → Artwork. Album-art preferences or external artwork may differ in Foobar itself.

The native bridge and Windows executable need both GitHub CI and a real Foobar playback check. Do not assume a Windows-only update fixes stale GSMTC art when the bridge remains v0.2.

---

# Melisma Universal Windows v0.6.12 — lyrics visible before the vocals (test build)

## Fix the instrumental-gap lyric blackout

In the November 25 test video for **International Harvester**, Melisma already had 57 synchronized lines in its cache, but showed only three bouncing dots for a roughly 30-second intro. The v0.6.9 Windows renderer returned from paint when `LyricsInterlude.At()` was true. That made the dots **replace the entire lyric page**, whereas Android Melisma represents the interlude as an animated row **within** its normally visible, scrolling lyrics.

- **Show upcoming lyrics as soon as they're ready**, including during long instrumental introductions, gaps, and before the first timestamp. They ease into view in 750ms, independently of playback time; no lyrics, MP3s or timestamps are modified.
- **Keep the dots** below the upcoming line, between surrounding lyric rows, and fade them out shortly before singing starts (Android's 500ms pre-hide).
- **Gently center the next line during long gaps** within 850ms, leaving it in place through the final half-second so the first sung word doesn't make the page jump backwards.
- **Do not highlight prematurely**: upcoming text is readable in a muted neutral color, previously sung lines dim, and cyan/word animation still follows source timestamps.
- **Performance preserved**: no extra timer or render loop; v0.6.10 adaptive painting and v0.6.11 Foobar metadata and NetEase LRC fallback remain intact.
- Added automated regression cases for gap preview, pre-hide window, upcoming index, fade timing and scroll focus.

### Manual test
1. Play **International Harvester** from time 0. The first upcoming lines should softly appear immediately; three dots may accompany the instrumental introduction but should **not hide any lyrics** until vocals begin.
2. Watch the first lyric begin: it should turn cyan at its genuine timestamp without a jump, and later lines should remain visible beneath it.
3. Play a song with a long instrumental break after the first verse: the upcoming line should glide into position while dots remain separate.
4. Seek backwards into the intro and forwards into a chorus, and compare the second-screen output with the controller.
5. Monitor Melisma CPU with and without the TV preview, and confirm the earlier cover-to-cover crossfade works.

This is a Windows CI build, not a fully confirmed real-device test. The existing Foobar Bridge v0.2 remains compatible; no bridge reinstall is needed.

---

# Melisma Universal — Windows v0.6.11 (test build)

## Fix: correct Foobar tags and genuine line-synced lyrics

The two test videos from November 25 show two distinct issues:
- **International Harvester:** native playback 3:50.748, but lyric search title only (artist blank, duration 0).
- **Misty Blue — Crystal Shawanda:** fast **plain** lyrics, no real timestamps, so no karaoke highlighting.

The older Foobar bridge reports playback position and duration only. Foobar's Windows media session sometimes omits the artist or album, so providers get an incomplete query. This build pairs with the new read-only **Foobar Bridge v0.2**, which publishes the playing track's actual title, artist, album and generation via an independent versioned shared-memory snapshot; no file paths or audio data are exposed. Playback timing's original v0.1 mapping stays unchanged, so existing clients still work.

- The viewer prefers **direct Foobar metadata** with consistent generation to avoid a stale track name. If the new bridge is absent it retains GSMTC fallback.
- Foobar's real duration is captured before creating lyric searches; the Windows session's zero duration no longer dictates search identity.
- NetEase now falls back to a genuine line-timed LRC when a recording has no YRC per-syllable timings, matching Android's implementation; the prior Windows provider silently discarded those usable timestamps.
- Missing artist tags can still use title plus **strict recording duration** matching, not unguarded title-only guesses.
- Native and Windows track metadata are shown separately in **Sources…** diagnostics.
- Keep the v0.6.10 CPU/adaptive refresh fixes, v0.6.9 album crossfade, instrumental dots, and window memory.

### Install / test
1. Extract Melisma Windows v0.6.11 in a fresh folder.
2. **Install the Foobar Bridge v0.2** file through Foobar2000 → Preferences → Components → Install, replacing the older bridge; restart Foobar2000. (This is needed for reliable artist and album tags, not merely the new EXE.)
3. Play **International Harvester** again. In **Sources…**, confirm the native metadata has an artist and nonzero lookup duration, or see if the MP3 itself is untagged. If blank in both places, correct the MP3's artist tag rather than guessing it.
4. Play **Misty Blue — Crystal Shawanda** again. A genuinely synchronized provider result will animate; if providers offer text only, it is correctly labelled plain and cannot be highlighted from audio without timing data.
5. Check CPU usage with/without second-screen TV and word highlights. Inspect the provider outcome report before drawing conclusions.

Note: this is an automated build. The SDK bridge must pass Windows compilation and both changes need playback testing in Foobar2000. A source with no timed lyrics cannot be made synchronized by this update.

---


# Melisma Universal — Windows v0.6.10 (test build)

## Android lyric lookup parity + lower CPU use

**Reported regressions:** On v0.6.9 many songs remained at "Waiting for synchronized lyrics"; others took too long to display. CPU usage rose sharply. This release repairs provider matching and fallback, not just the waiting message.

- **Android-style metadata:** the title alone makes a request usable. A missing artist is searched cautiously (no arbitrary title-only mismatches). LRCLIB first checks tagged exact data, then a cleaned song title and first credited artist, then a scored catalogue search. Excessively strict album tagging and the artificial search delay are removed.
- **Plain lyrics when synchronization is unavailable:** LRCLIB plain text is shown in the lyric viewport, clearly labelled as **not synchronized**, with mouse-wheel scrolling. If another provider returns real timestamps, synced lyrics replace plain text automatically. No invented timing or changes to MP3 files.
- **First paint from cache:** Windows now reuses local cached lyrics regardless of inconsistent album tags. Old cache entries remain readable. A cached word-timed track no longer re-queries all providers on every replay; cached line-only/plain tracks get at most one background improvement request per app session. Manual Find lyrics still refreshes.
- **Provider isolation:** exceptions from one broken provider are reported as that source's failure; they do not abort other sources or erase a successfully retrieved lyric.
- **Adaptive CPU rendering:** 40fps only for actually sung word-timed lines, about 15fps for normal timed lyrics, 13fps while waiting/intro dots, and about 8fps when idle/paused. The controller does not paint while minimized. The TV display no longer has its own duplicate 40fps timer; both displays share the same playback-driven redraw clock.
- **Prior v0.6.9 fixes retained:** direct cover-to-cover transitions, three instrumental dots, window placement; retains v0.6.8 smooth word enlargement and cached loading.

### Validation checklist
1. Try five to ten songs that previously sat at "Waiting for synchronized lyrics." They should display timed lyrics, static fallback text, or an explicit final no-match message. If still missing, open **Sources…** for the exact source outcomes.
2. Play the same track twice: second play should open immediately from cache. Change a file's album metadata only: cache should still work for the same title/artist/duration.
3. Watch Task Manager with Melisma on the main window alone, then with the second-screen output. Compare idle, instrumental, word-timed verse and paused CPU, and ensure active words still animate smoothly.
4. Recheck cover transitions and remembered window placement.

---

# Melisma Universal — Windows v0.6.9 (test build)

## Fix: continuous cover-art changes, Android instrumental dots, remembered window

- **Actual cover-to-cover crossfade:** do not clear the outgoing cover on a new track. Keep it visible while Foobar's embedded image or the optional catalog fallback loads, then transition directly to the incoming artwork. The Cinema cover panel keeps the outgoing art fully visible underneath the incoming art, avoiding a dip through the dark placeholder. An artless track fades out only after lookups confirm no cover; stale asynchronous results cannot replace later songs.
- **Separate user timings preserved:** Display → Background transition controls the background art and palette; Display → Cover art transition controls the Cinema artwork panel. Both stay adjustable at 1–10 seconds. The palette now also blends old-to-new rather than snapping to incoming colours.
- **Three bouncing dots from Android:** an instrumental introduction or 3000ms-or-longer gap between lyric lines shows a gently staggered three-dot animation, disappearing 500ms before vocals return. Uses accurate word end times where available and conservative estimates otherwise. The placeholder also animates while lyrics are still loading. No synthetic lyric timestamps are written to files.
- **Remember controller placement:** last restored window coordinates, size and maximized state are saved to LocalAppData/MelismaUniversal/WindowsLyricsViewer/window-placement.json on close and restored on launch. Disconnected displays are handled by clamping/centering within an available screen. The independent borderless TV display remains full screen on its selected monitor.

### Acceptance test

1. On v0.6.9, switch between two songs with different embedded covers and confirm no black or empty frame while the incoming cover loads. Adjust both transitions to 8 seconds to make the blend easy to verify, then restore your preferences.
2. Play a track whose first lyric starts after three seconds, then seek into a long instrumental section. Confirm three cyan-ish dots bounce until roughly 500ms before the next lyric.
3. Move and resize the main window, exit and restart; it should reopen in the same place. Check maximized state separately. The TV output should stay independent.
4. Check v0.6.8's faster lyric loading and smooth single-render word enlargement are unchanged.

---

# Melisma Universal — Windows v0.6.8 (test build)

## More fluid active-word growth (no abrupt pop)

The user confirmed v0.6.7 lyrics load much faster but said the
Android-style word growth still looks unnatural: it pops up too quickly.

The prior effect completed a **full 8.5% zoom in 60% of each word's
duration**. On a 150ms syllable this meant reaching full scale in about
90ms, which is visually abrupt—especially when Windows painted at only
~18 frames per second.

v0.6.8 makes four targeted improvements without touching the working
Foobar2000 clock, word timing, provider lookup, artwork or Cinema layout:

- **Duration-aware growth:** syllables lasting 120ms enlarge by only
  approximately 1%, a 300ms word grows around 1.7%, and a long
  900ms note grows up to about 5.8%. Fast words no longer jump by 8.5%.
- **Smooth in/out motion:** a sine-squared envelope starts and ends with
  zero velocity, so words gradually rise and settle instead of abruptly
  snapping back. The effect remains tied to the real source timestamps,
  so pause and seek still freeze/restore the right frame.
- **No mid-word activation:** the prior renderer waited until 70% of
  the line's transition had completed before replacing the normal word
  with the animated version. That could switch the display mid-syllable.
  Word animation now begins at the timed onset, with scale initially
  exactly 1.0, and maintains the single-render fix from v0.6.7.
- **Smoother display refresh:** controller preview and second-screen
  TV animation intervals reduced from 55ms (~18 fps) to 25ms
  (~40 fps). Actual Windows frame delivery depends on GPU/CPU load.
  The Foobar2000 timestamp remains authoritative.

**Display… → Lyrics display → Enlarge sung word (Android style)**
is still on by default. Switching it off preserves cyan word timing.

### Test

1. Extract v0.6.8 into a separate folder; keep v0.6.7 as backup.
2. Play **Edamame** in Foobar2000 and watch the short sung words.
   They should feel like a subtle, gentle swell instead of popping out.
3. Compare a longer held syllable; it should visibly grow a little
   more and ease back softly.
4. Pause halfway through a word, resume and seek backward. The
   enlargement should stay tied to the same lyric progress.
5. Confirm no doubled letters, exact cyan highlighting, fast lyric
   loading and instant cached replays remain intact. Check the TV
   second screen, Cinema art and full-screen Living animation.

The next project milestone is selectable animation styles (for
example classic bouncing ball, underline glide and glow). They should
build on this stable motion/word-position baseline, not restore the
fast-popping effect.

## Retained from v0.6.7

## Fix: doubled word animation and slow lyric appearance

The user recorded **Edamame — bbno$, Rich Brian** in v0.6.6.
The video exposed words that looked **doubled or collided** while their
Android-style growth animation was active, and the user reported that
lyrics did not appear until ~15–20 seconds after singing began.

### Correctly render active words only once

- v0.6.6 painted the entire synchronized lyric line normally and then
  drew the same active word again at an enlarged size on top. This
  produced visible extra letters and overlapping words.
- v0.6.7 first identifies the current timed word and its precise row/
  character positions, excludes its original glyph region from the
  normal text paint, and **draws the enlarged word only once**.
- Word position uses the same measured full-row coordinates as the
  baseline, reducing kerning-related drift. Wrap positions stay fixed.
- The old cyan word fill, real Foobar2000 timestamps, pause/seek and
  per-song timing offsets are untouched.
- **Display… → Lyrics display → Enlarge sung word (Android style)**
  still switches the grow effect on/off. Turn it off if you want only
  the reliable cyan highlighting.

### Display lyrics immediately when a provider answers

- Older builds asked all providers concurrently **but waited for all of
  them to finish before displaying any lyrics**. A slow service (or
  12-second timeout) could hold up usable lyrics already returned by
  LRCLIB, lrc.red or NetEase.
- v0.6.7 reports and displays the **first available valid lyric result**
  as soon as it is returned. Remaining provider requests continue.
- If another source later provides more complete *genuine word timing*,
  the current lyrics upgrade automatically using the existing source
  ranking and missing-intro repair; no word durations are invented.
- Searches from a previous song are cancelled on track change.
- Following Android's immediate cached-result path, successful selected
  lyrics are now also saved locally in
  `%LOCALAPPDATA%\MelismaUniversal\WindowsLyricsViewer\lyrics-cache`.
  Playing the same tagged track again displays those saved lyrics at once,
  before the new search finishes. Matches include title, artist, album
  and a duration sanity check. Entries expire after 30 days; they do
  **not modify MP3 tags** and never degrade cached genuine word timing
  to an inferior line-only provisional result.
- This is **not full Android prefetch parity**. The Windows foobar2000
  media-session bridge currently exposes only the playing song, not
  the complete playback queue, so first-time uncached tracks still
  need a live provider response.
- **Sources…** now shows the number of providers completed and
  **"First playable lyrics displayed after N seconds"**, so network
  delays can be distinguished from missing first-verse timestamps.
- Note: this fixes **lookup-related lateness**, not a lyric file that
  genuinely has no timestamps for its opening verse. The existing
  LRCLIB intro merge and completeness checks still address those cases.

### Test this version

1. Keep v0.6.6 as a backup. Extract v0.6.7 into a new folder and run
   the new executable while foobar2000 plays **Edamame**.
2. Watch the animated current word. It should be **one readable word**
   growing gently, not normal and enlarged copies overlapping.
3. Turn "Enlarge sung word" off and back on to compare. Both modes
   should keep exactly the same syllable timing.
4. Restart the track. Lyrics should now appear as soon as a provider
   answers instead of waiting for the slowest of five providers.
5. Click **Sources…** after the lookup completes. The diagnostic
   report includes first-result latency, all provider outcomes,
   selected source and earliest lyric timestamps. A genuinely late
   first cue can be investigated separately from network response time.
6. Verify pause/resume and seeking, seamless Cinema, album cover,
   second-screen rendering and Living idle takeover.

## Retained from v0.6.6

## Fixes: no flickering footer, readable NetEase words, animated active syllables

v0.6.6 is an incremental build from v0.6.5, preserving the confirmed
Foobar2000 bridge, first-verse NetEase word synchronization, on-demand
cover artwork, Cinema background and full-screen Living screensaver.

### 1. Stop flashing the playback footer

The bottom controller line previously repainted its *entire WinForms Label*
roughly every 400 milliseconds to display a moving mm:ss counter. This
produced visible flashing/flickering in the user's screenshot, especially
at higher DPI scaling.

The stable footer now shows:

- The saved lyric timing offset in milliseconds;
- Playing/paused state and playback clock source;
- An explicit warning if playback time is unavailable.

The precise current playback position, track duration and bridge freshness
remain accessible by clicking **Sources…** (PLAYBACK TIMING — live). The
actual lyrics are still redrawn at the regular animation refresh rate.
No change to the synchronized playback clock or active lyrics algorithm.

### 2. Fix merged English words in NetEase YRC

The screenshot demonstrated "howto", "I'mgoing", "havetoo" in the current
NetEase lyrics. Rather than guessing where a word breaks, the app now
cross-checks *matching text* from another fetched lyric source:

- Accept a spacing reference only when all non-whitespace characters
  match, in the same order, in a corresponding line within 12 seconds.
- Restore the missing spaces to the affected NetEase source characters.
- Retain the **same number of timed word tokens** and all original
  onset and end timestamps. Cyan fill and pause/seek alignment are
  unchanged.
- If other providers do not contain the same lyric text, leave the
  original word timings untouched rather than inventing lyric text.
- Sources… reports the number of lines whose spacing was restored.

### 3. Enlarge the currently sung word, like Android TV Melisma

Android uses a subtle word-scale/y-lift spline that swells the *active*
word or syllable, then settles back at the end of its timing window.
Windows now adds this visual cue:

- The active word enlarges smoothly by up to about 8.5%; Simple mode
  uses a gentler, ~4.5% maximum.
- The effect follows the real TTML/YRC word start/end timestamps and
  the same cyan fill progress. Pause freezes it; seek moves it to the
  correct word immediately. For onset-only enhanced LRC, a brief visual
  pulse occurs at the recorded onset without modifying lyric sync.
- **Display… → Lyrics display → Enlarge sung word (Android style)**:
  switch on/off; enabled by default and saved with other display settings.
- Each word is drawn as an overlay using the same fixed wrapping and
  centered line layout. The whole line is **not repositioned** as each
  word grows. Works on the controller and on an extended-TV display.
- Ordinary line-timed lyrics keep their existing whole-line emphasis.

### How to test

1. Extract v0.6.6 to its own folder and retain v0.6.5 as your backup.
2. Play **Blinding Lights — The Weeknd**. Confirm the bottom footer no
   longer flashes as the song plays; the Playing/Paused text still changes
   when appropriate. Open **Sources…** to inspect the accurate live clock.
3. Watch the current words on your TV. The cyan-highlighted syllable
   should gently grow and lift as it is sung, then return to normal size.
   Pause, resume and seek; it should not drift from the singer.
4. Confirm the previously merged English words have spaces when a matching
   independent lyric source provides them. A few unmatched lines may
   remain unchanged by design.
5. Toggle **Enlarge sung word** off and on in Display settings; verify
   turning it off stops the enlargement but NOT the cyan progression.
6. Verify the album cover, seamless Cinema backdrop, Living idle fade,
   saved ±100ms timing correction and second-screen layout continue working.

Automated tests cover screenshot-derived spacing cases, wrong-reference
rejection, unchanged word start/end timestamps, active word selection,
scale progress, pause/seek and onset-only visual effects.

## Retained from v0.6.5

## Improvements: missing cover art, readable controls and earlier word timing

This version builds on the working v0.6.4. No changes were made to its
Foobar2000 playback clock, TTML word-fill renderer, LRCLIB intro merge,
Cinema layout, full-screen Living screensaver or saved per-song timing
offsets.

### 1. Automatic album artwork fallback

- First use artwork published by the Foobar2000 Windows media session.
- If the media session exposes metadata but **no thumbnail**, optionally
  search **Apple's public iTunes catalog** for a closely matching
  title/artist/album, with a duration check when available. Download the
  matched album cover at up to 600px and cache it locally for 30 days.
- **Display settings → Background → Find artwork when player supplies none**
  is on by default and can be disabled. Turning it off avoids catalog
  lookups for future tracks.
- A catalog cover is only an approximate substitute for the local MP3's
  *actual embedded image*. To select exactly the right image for a song,
  use the existing **Cover…** button to import a local file.
- Source details now shows whether the artwork came from the player, the
  catalog fallback, or was selected manually.
- Album lookup errors cannot affect synchronized lyrics or playback.
  Old album-image fade settings remain intact.

### 2. Better lyric-source coverage with real NetEase YRC words

- Adds **NetEase Cloud Music** in parallel with lrc.red, AMLL TTML DB,
  Better Lyrics and LRCLIB, following the upstream Android Melisma provider.
- Uses Android's title / primary artist / duration matching and the public
  search and song/lyric API, attempting its *actual YRC or klyric word-
  timed* track. Every syllable uses timestamps supplied by the source.
- A live Windows test confirmed **Blinding Lights** has NetEase YRC
  word-timed vocals beginning at ~23.55 seconds. Its initial time-coded
  producer/composer credits at 0/1/2 seconds are filtered from the display.
- NetEase entries with genuine earlier word timings are preferred to
  otherwise matching songs with incomplete opening verses.
- If NetEase is unavailable, blocked or has no word timestamps, it is simply
  an additional "no result" in Sources diagnostics. Existing lrc.red +
  LRCLIB intro playback remains the fallback. No predicted timestamps.
- NetEase's public endpoints may rate-limit or block anonymous connections;
  do not interpret a rejected request as a playback problem.

### 3. Text and button fit

- Shortens the overlong "35 lines, ..." controller notice to
  "35 lines · Opening verse line-timed, later words synchronized".
- Renames controller buttons to compact **Find lyrics**, **Open lyrics**,
  **Second screen**, **Display…**, **Cover…** and **Sources…** to prevent
  truncation at 150% DPI. The available actions remain the same.

### First test

1. Keep v0.6.4 as a backup and extract v0.6.5 to its own folder.
2. Play **Blinding Lights — The Weeknd**. If the Windows media session still
   provides no thumbnail, the album art should appear using the optional
   catalog fallback. Inspect **Sources…** for "Artwork: ...".
3. Confirm the main status notice and buttons are readable without clipping
   on your normal laptop screen.
4. Play Blinding Lights from the start. Live testing confirmed NetEase
   YRC has genuine word-timed vocals beginning around 23.55 seconds.
   Producer/composer credits at 0/1/2 seconds are now filtered out.
   If NetEase is accessible on your computer, word-sync should start with
   the opening vocals; otherwise the established LRCLIB line-timed intro
   remains the fallback.
5. Inspect **Sources…** for the NetEase result (match, no match, timeout, or
   error) and the first cue of each other provider.
6. Confirm the cyan word fill follows foobar2000 exactly after 1:00.
   Pause, resume and seek. Check the TV/second-screen Cinema layout,
   full-screen Living idle fade and saved ±100ms offsets.

### Source notes

The public iTunes Search API is documented by Apple:
https://performance-partners.apple.com/search-api

NetEase's search/YRC logic is ported from upstream Android Melisma:
https://github.com/MelismaApp/melisma/blob/v2.11.0/app/src/main/java/com/melisma/app/lyrics/provider/NeteaseProvider.kt
https://github.com/MelismaApp/melisma/blob/v2.11.0/app/src/main/java/com/melisma/app/lyrics/parse/YrcParser.kt

## Retained from v0.6.4

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
