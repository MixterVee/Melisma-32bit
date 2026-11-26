# Foobar2000 Bridge v0.4 — recover rapidly restarted Foobar sessions

Earlier v0.3 opened named memory mappings and **abandoned startup permanently** when a prior mapping name existed. Since Melisma holds read-only views, those names can survive after the old Foobar process exits. On immediate restart the new bridge then fails to publish native song/clock information, causing stale titles or an unusable playback clock.

This version acquires an exclusive writer mutex and checks the prior bridge heartbeat before taking over named shared memory. If a previous v0.3 process is still alive, the new writer does not overwrite it. If the writer is gone and the old mapping persists, it **retries** on Foobar's UI-thread timer until the old heartbeat is stale, reopens the existing timeline/metadata/art mappings and initializes them with a fresh process-generation epoch. The legacy memory layouts and Windows read-only consumers remain compatible.

No audio writes, player control, MP3 modifications, network server, or port forwarding.

**Installation:** In Foobar2000 → File → Preferences → Components → Install, select `foo_melisma_bridge.fb2k-component` from the v0.4 build, replacing v0.3, then restart Foobar. Pair with **Melisma Windows v0.6.21** for automatic old-lyric invalidation and restart-safe metadata.

**Test:** Play one song → exit Foobar while playing → double-click a different MP3 immediately. After Foobar's new process establishes its native bridge, Melisma must show only the new song and its own lyrics. Repeat with a second-screen TV display.

---

# Foobar2000 Bridge v0.3 — track-specific front cover

The bridge now publishes actual front-cover art from Foobar2000's centralized now-playing album-art service via read-only shared memory `Local\\MelismaFoobarArtwork_v1`. The snapshot has a track generation, a sequence guard, a loading/has-cover/no-cover state and up to 8MB of image bytes. The existing playback and metadata mappings remain untouched.

**Installation:** In Foobar2000 x64, open File → Preferences → Components → Install, select the new `foo_melisma_bridge.fb2k-component` from the v0.3 GitHub Actions artifact and restart Foobar2000. Use it with Melisma Windows v0.6.13 or later.

No network access, path exposure, cover modifications, or audio interception. This is a one-way read-only feed of the current album image. Native Foobar artwork is given priority over Windows media-session artwork to avoid stale images from previous songs.

---

# Melisma Foobar2000 Bridge v0.2 — read-only playback and song metadata

Foobar2000's Windows media-session API can omit the artist, album and useful duration while the native v0.1 timing bridge works. The Windows lyrics viewer then searches for incomplete song metadata and produces false misses or plain lyrics.

Bridge v0.2 keeps the **original** Local\\MelismaFoobarBridge_v1 time mapping unchanged, and adds a separate optional Local\\MelismaFoobarMetadata_v1 mapping holding only title, artist, album (UTF-8, 256 bytes each) and track generation. Song tags are read by the Foobar2000 SDK's title formatting from the current track, once on track-change/tag-edit events; no network, filesystem scan, audio samples or control of playback. No file path is exposed to the lyrics viewer.

## Installation

1. Extract the GitHub Actions bridge package and use Foobar2000 **File → Preferences → Components → Install** to select `foo_melisma_bridge.fb2k-component`.
2. Confirm replacement of old **Melisma Foobar2000 Bridge v0.1**, and restart Foobar2000.
3. Run Melisma Windows v0.6.11; **Sources…** now reports the native Foobar title, artist, album and source duration.
4. If the artist is *still* blank, check that Foobar2000 actually shows an Artist metadata tag for this file. The bridge does not invent artist identities.

This component is currently for **Foobar2000 64-bit on Windows x64**, and must be manually tested. v0.1 time-only readers remain compatible.

# Melisma Foobar2000 Bridge — Windows karaoke timing prototype

This is an **optional, read-only Foobar2000 x64 component** for the first Windows Melisma Karaoke proof of concept.

The earlier Windows detector confirmed Foobar2000 media **titles**, **artists**, **playing state** via Windows' media sessions, but *did not* receive a playback timeline. The native bridge reports exact **Foobar2000 SDK playback position**, **track duration**, **playing/paused/stopped**, and **new-track generations**.

## Installation / first test

1. From the GitHub Actions **Build Melisma Foobar Bridge Test Kit** artifact, extract the ZIP.
2. In Foobar2000 (64-bit), open **File → Preferences → Components → Install** and select `foo_melisma_bridge.fb2k-component`.
3. Restart Foobar2000 to load the component. It should appear in the component list as **Melisma Foobar2000 Bridge**.
4. Run the included **MelismaMediaDetector.exe** from the test ZIP and start playing an MP3 in Foobar2000.
5. The detector should display **Direct bridge** in its status and should show a moving **Position from Foobar2000** and a real track duration.
6. Pause, resume, jump to the middle, and switch tracks. Both counters should respond. Use **Save CSV** to export diagnostic samples if we need to inspect timing consistency.

The component has **no configuration**. Remove it through Foobar2000 Preferences → Components if desired.

## Design and privacy

- Read-only: no play/pause/seek commands, metadata edits, file scans, audio pipeline changes, outgoing network connections or Firebase.
- Calls Foobar2000's `playback_control::playback_get_position()` and `playback_get_length()` only from its **main thread** using a 100 ms Win32 timer. No heavyweight processing on Foobar's audio callback thread.
- Publishes only a small, versioned binary playback snapshot to session-local Windows shared memory, named `Local\MelismaFoobarBridge_v1`; it does **not** transfer track names or file paths through shared memory.
- A single writer uses an odd/even sequence to make partial reads detectable.
- Detector still uses the Windows media session to get title and artist. The direct bridge supplies position, duration and state.
- Only the current Windows login session can normally see the mapping; this is a **local diagnostics interface, not authentication**. Avoid running untrusted apps under the same account.
- Both the bridge and updated detector are **test builds**; their behavior needs real playback verification.

## Constraints

- **Foobar2000 64-bit, Windows 11 x64** for this initial test package.
- Source builds against the Foobar2000 SDK from the upstream-mirrored `reupen/foobar2000-sdk-unmodified` repository on a Windows GitHub Actions runner. This SDK isn't committed to Melisma.
- If Foobar2000 reports 0 ms despite the component being loaded, check Foobar2000 View → Console for the `foo_melisma_bridge` initialization line.
- This test does not yet include lyrics rendering, stem controls, or second-screen karaoke.

## Next steps

Once timing is confirmed, add audio-stem mixer integration and the dual-monitor karaoke lyrics window. The wireless Android TV receiver will be a later milestone.

The Melisma project preserves upstream AGPL-3.0 obligations. Foobar2000 SDK licensing and component distribution rules apply separately.
