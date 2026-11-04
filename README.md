# Melisma 32-bit / Android TV compatibility builds

## v2.8.1 — transparent clock, Exit on main toolbar, and language defaults Off

**[Melisma Universal v2.8.1 Android TV test APK](https://github.com/MixterVee/Melisma-32bit/actions/runs/41843691636)** — open **Artifacts** and download `melisma-universal-v2.8.1-test-v2.11.0`; extract `melisma-universal-v2.8.1-TEST.apk`.

- **Clock:** removed the black translucent rounded background. The time is now plain white text over the artwork, at the same top-right position and with the device's configured 12/24-hour format.
- **Exit:** moved the confirmed **Exit Melisma** action out of Settings and into the **main toolbar**, immediately after the Settings gear (look for the door-and-arrow icon; scroll toolbar horizontally if required). Confirmation still exits Melisma without stopping music in the source app.
- **Language defaults for new installs / previously unset preferences:** Romanization **Off**, Furigana **Off** (already its default), automatic Hokkien recognition **Off**, and lyric translation **Off** rather than Provider. Existing explicitly saved preferences are **not reset**; turn them off once under **Settings → Language** if they were saved as On.
- Preserves v2.8's optional five-second auto-hide, Android TV remote focus, v2.7.5 player-specific seeking and Open app wording, and the tested album-art transitions.

**Test on Shield/Onn:** enable Clock and Auto-hide in Settings → View, confirm the floating clock has no dark box, press Up/OK to reveal the toolbar, select the Exit icon and confirm that the music app keeps playing. For language defaults, check a clean-install profile; an existing profile uses previously saved values.

## v2.8 — optional clock, clean-screen mode, and Exit

**[Melisma Universal v2.8 Android TV test APK](https://github.com/MixterVee/Melisma-32bit/actions/runs/41832807911)** — under **Artifacts**, download `melisma-universal-v2.8-test-v2.11.0` and extract `melisma-universal-v2.8-TEST.apk`.

In **Settings → View** there are two new independently saved options (both default OFF):
- **Clock in top-right corner:** displays the device's local time in its configured 12/24-hour format. Remains visible when the controls are hidden.
- **Auto-hide all playback controls:** on Android TV, five seconds after the last remote interaction the top action row, Lyrics bottom play/seek/volume controls, and Cinema transport/seek/volume/**Open [music app]** button disappear. **Up**, **OK**, **Down**, or **Back** brings the controls back. Without this setting, the earlier TV toolbar behaviour is unchanged; background transitions remain as in v2.7.5.

To exit, open **Settings**, choose the **Exit** button beside the help icon, and confirm **Exit Melisma**. This stops following media sessions, asks Android to unbind the notification listener, and closes Melisma's task without stopping the source app's music.

**TV test suggestions:** Turn on both switches, let the remote sit idle and check that the clock stays visible while the UI disappears, then press OK/Up to reveal controls. Test both Lyrics and Cinema, and verify Exit returns to the Android TV home screen. Keep v2.7.5 available as a working fallback until the Shield test is successful.

## v2.7.5 — show which music app does not allow seeking

**[Melisma Universal v2.7.5 test APK](https://github.com/MixterVee/Melisma-32bit/actions/runs/41753135702)** — download the `melisma-universal-v2.7.5-test-v2.11.0` artifact under **Artifacts** and extract `melisma-universal-v2.7.5-TEST.apk`.

- When the active player does not advertise seeking, **Lyrics/NowBar** and **Cinema/MediaPanel** now identify it: `Seeking unavailable from Amazon Music`, `Seeking unavailable from Deezer`, etc., using the live Android media session's app label. If no label is supplied, the previous generic `music app` wording is retained.
- **Cinema bottom action:** the generic `Open player` now reads **`Open Amazon Music`**, **`Open Deezer`**, etc., using the same active source label; the generic text is retained only if no app label is available.
- Preserves the confirmed **v2.7.4 Cover Art fade**, the saved **1–10 s** background transition slider, Timing remote navigation, and all previous 32-/64-bit Android TV improvements.
- Seeking itself is unchanged: the app never attempts to seek when the active player's media session does not allow it.

## v2.7.4 — track-change handoff without a blank intermediary

**[Melisma Universal v2.7.4 test APK](https://github.com/MixterVee/Melisma-32bit/actions/runs/41750199229)** — download the `melisma-universal-v2.7.4-test-v2.11.0` artifact and extract `melisma-universal-v2.7.4-TEST.apk`.

This revision goes beyond v2.7.3: it holds the *approved last-visible background artwork* until incoming music artwork is ready, rather than passing through any raw/null/near-black media-session placeholder. It defers accepting a new cover while the **three-dot lyrics-loading screen** is active and lets a metadata transition settle for 600ms first, then uses the existing **1–10 s** Cover Art crossfade. If a song never has artwork, the usual unbranded fallback eventually takes over. Other backgrounds, Timing remote navigation, and saved player history fixes are left unchanged.

**Test:** Select **Cover Art**, **0 px Cover Blur**, **10 s Background transition**. On Deezer or Amazon Music change songs: the outgoing cover should remain visible *behind* the loading dots, then blend into the incoming art. A black interval would mean a separate rendering path is still clearing the image, and should be reported rather than assumed fixed.

## v2.7.3 — preserve outgoing artwork through lyrics lookup

**[Download Melisma Universal v2.7.3 test APK](https://github.com/MixterVee/Melisma-32bit/actions/runs/41748717724)** — select **Artifacts**, download `melisma-universal-v2.7.3-test-v2.11.0`, and extract `melisma-universal-v2.7.3-TEST.apk`.

- **Seamless art during track changes:** the previous album cover now stays visible while Melisma searches for the next track's lyrics (the three centered dots). The old **2.5 s artwork hold** could expire during this lookup and make a long fade blend from a fallback background instead. When the next artwork appears, the selected **1–10 s crossfade** can run directly from the outgoing cover into the next one.
- **Still handles genuinely missing album art:** if the new song has no cover after its lyric lookup finishes, Melisma falls back to its unbranded gradient after 2.5 seconds instead of holding the wrong cover forever.
- **Players to follow history:** replaced a NUL character, which is invalid in XML preferences, with a safe delimiter. Previously detected music-app names should now remain listed after restarting Melisma, not just while they have active sessions. Previously readable entries remain compatible.
- Preserves v2.7.2 Timing remote navigation and other previous fixes.

**Diagnostic:** To check the background handoff, use Amazon Music or Deezer with **Cover Art**, **0 px blur**, **10 s transition**. Change tracks and observe whether the old cover remains behind the loading dots until the new cover begins fading in. Three dots may still appear when lyrics are being retrieved; this patch concerns the artwork *behind* them.

## v2.7.2 — Shield Timing section D-pad navigation

**[Melisma Universal v2.7.2 test APK](https://github.com/MixterVee/Melisma-32bit/actions/runs/41745929327)** — download the `melisma-universal-v2.7.2-test-v2.11.0` artifact, then extract `melisma-universal-v2.7.2-TEST.apk`.

This update specifically repairs **Settings → Timing** on Android TV. The expanded section's **Down** target is now the built-in **−50 ms** clickable button, not the custom slider container that remained unreachable on the NVIDIA Shield. Press OK to subtract 50 ms, use Right to navigate to **Reset** and **+50**, and Down to reach subsequent timing controls. All v2.7.1 Amazon Music Cover Art crossfade, Players to follow and 1–10-second background duration changes remain intact.

The patch builds against Melisma v2.11.0; its D-pad behavior still needs on-device confirmation.

## v2.7.1 — Amazon Music Cover Art crossfade and Settings D-pad focus

**[Download Melisma Universal v2.7.1 test APK](https://github.com/MixterVee/Melisma-32bit/actions/runs/41744578566)** — select **Artifacts** and download `melisma-universal-v2.7.1-test-v2.11.0`; extract `melisma-universal-v2.7.1-TEST.apk`.

- **Cover Art / Artist Header:** Replaced the stateful manual fade with Compose's interruption-aware **Crossfade**. Previously, the incoming bitmap could be drawn at full opacity before the animated fade was reset. Now the entire artwork layer fades in/out over the chosen **Settings → Background → Background transition** duration (1–10 s); repeated Amazon Music artwork updates also go through the crossfade.
- **Settings → Timing:** The first **Sync offset** row is an explicit Android TV remote focus target, navigable from the section header via **Down**. While focused, **Left/Right** adjust timing by **50 ms** and **Up/Down** navigate to surrounding controls. Other sliders are unaffected.
- **Settings → Players to follow:** The first real player in the saved player list is now the section's focus target; **Down** can enter the section and move between players.
- Preserves all previous v2.7 and v2.6.x fixes, including saved **1–10 s** transition duration and 32-/64-bit support.

This is a debug build. Compilation checks cannot prove remote navigation or animation behaviour on a physical Shield, so please test there before considering this a stable baseline.

## v2.7 — adjustable 1–10 second background transitions

**[Universal Android TV v2.7 test APK](https://github.com/MixterVee/Melisma-32bit/actions/runs/41731567111)** — under **Artifacts**, download `melisma-universal-v2.7-test-v2.11.0` and extract `melisma-universal-v2.7-TEST.apk`.

Open **Settings → Background → Background transition** to adjust track-to-track fades in **0.2-second increments from 1.0 through 10.0 seconds**. The default remains **3.4 seconds** until changed, preserving v2.6.4 behaviour. The choice is saved locally and applied to **Color, Living, Living (Classic), Cover Art and Artist Header** backgrounds. Black backgrounds remain black. The slider is compatible with Android TV D-pad focus and the value persists across restarts. Cover Blur remains a separate setting.

Includes all v2.6.4 navigation, language, artwork fallback and Shield fade improvements. This is a debug test APK, which may require replacing a previous matching debug install.

## v2.6.4 — Android TV Language menu focus fix

**[Universal Android TV v2.6.4 test APK](https://github.com/MixterVee/Melisma-32bit/actions/runs/41714977532)** — under **Artifacts**, download `melisma-universal-v2.6.4-test-v2.11.0` and extract `melisma-universal-v2.6.4-TEST.apk`.

Restores remote **Down** navigation from the expanded **Settings → Language** header into the actual first setting, **Romanization (文A)**. The first-focus mapping still targeted the pre-v2.6.2 label ("Romanization"), so the header intercepted Down but could not transfer focus. The mapping now matches the visible setting, allowing access to the remaining language controls. Includes all v2.6.3 adjustments and prior improvements.

## v2.6.3 — precise Cover Blur D-pad adjustment

**[Universal Android TV v2.6.3 test build](https://github.com/MixterVee/Melisma-32bit/actions/runs/41713460013)** — under **Artifacts**, download `melisma-universal-v2.6.3-test-v2.11.0` and extract `melisma-universal-v2.6.3-TEST.apk`.

Fixes the **Settings → Background → Cover blur** slider on Android TV. The range is 0–67; fractional D-pad adjustments were truncated by `toInt()` and could decrease on Left but not increase on Right. The blur slider now exposes 67 exact increments and rounds to the nearest integer. **Left = −1** and **Right = +1** per D-pad press. All other slider sensitivities are unchanged. Includes the v2.6.2 language preferences and v2.6.1 Shield background transition improvements.

## v2.6.2 — persistent language preferences, improved Shield/Amazon Music fades

**[Download Android TV universal v2.6.2 test APK from GitHub Actions](https://github.com/MixterVee/Melisma-32bit/actions/runs/41712641196)**

Under **Artifacts**, download `melisma-universal-v2.6.2-test-v2.11.0` and unzip `melisma-universal-v2.6.2-TEST.apk`. Compatible with ARMv7 32-bit and ARM64 64-bit Android TV hardware.

- **Global language controls:** `文A` Romanization and the Translate toolbar icon have been removed. Find persistent **Romanization (文A)** and **Lyric translation (global)** in **Settings → Language**. Turning either **Off** persists across songs, services, and restarts on that device. No source auto-enables these settings when the track changes.
- **Shield artwork transitions (v2.6.1):** Still Cover Art / Artist Header styles crossfade artwork over 3.4 seconds instead of jumping, and the renderer holds the preceding cover through brief player artwork gaps instead of flashing a synthetic fallback.
- **Existing v2.6:** Smooth base-palette and animated background crossfades, first-position **±ms** button, and clearer D-pad focus on seek-disabled music apps.

This is a debug APK: a matching debug installation can update in place, but a different debug signature may require uninstalling the previous debug app first. Do not uninstall the official Melisma release to install a test APK.

## v2.6.1 — Shield / Amazon Music album-art transition fix

**[Universal v2.6.1 test APK (GitHub Actions)](https://github.com/MixterVee/Melisma-32bit/actions/runs/41712322268)**

Under **Artifacts** download `melisma-universal-v2.6.1-test-v2.11.0`, then unzip `melisma-universal-v2.6.1-TEST.apk`. This build retains the original 32-bit/64-bit ARM universal APK support and TV navigation/timing features.

Fixes over v2.6:
- **Still Cover Art / Artist Header** modes now blend the actual reduced artwork images over 3.4 seconds. Previously only their underlying base colour was animated, so the album cover itself could jump to the next song immediately.
- **Transient no-artwork gaps** in a media session (e.g. on a Shield when changing songs in Amazon Music) keep the outgoing artwork for 2.5 seconds before switching to the synthetic fallback. This avoids a distracting intermediate colour jump if album-art metadata arrives late. This is a preventive fix; the actual Amazon Music transition depends on the media session's metadata timing.
- Animated Kawarp and Living Classic retain their existing 3.4-second crossfades. v2.6 toolbar ±ms first position and seek focus fixes remain unchanged.

**TV validation:** With Amazon Music on Shield, skip between songs with sharply different album artwork colours and inspect the transition with both Animated and Cover Art backgrounds. This is a debug build and may require uninstalling the previous debug build if signatures differ.

## v2.6 — quick timing first, accessible Deezer seek state, calmer colour changes

**[Universal Android TV v2.6 build (download the artifact)](https://github.com/MixterVee/Melisma-32bit/actions/runs/41670720750)**

Under **Artifacts**, download `melisma-universal-v2.6-test-v2.11.0` and unzip `melisma-universal-v2.6-TEST.apk`. GitHub action checks both `armeabi-v7a` and `arm64-v8a`. This is a debug package `com.melisma.app.debug`; uninstall older debug builds before installing if signatures differ. The official release app (`com.melisma.app`) is separate.

Changes from v2.5.1:

1. The **±ms** TV-only button is now **the first top-toolbar button** (far left) and gets initial focus when the toolbar is shown, instead of Cinema. It opens the side timing panel while the lyrics remain visible; offset continues to reset on a different song and persist on replay of the same track.
2. Where the music app's media session does **not** advertise `ACTION_SEEK_TO`, the disabled seek slider is **focusable on TV** with a clear *Seeking unavailable from music app* message. It preserves remote Up/Down/Back escape and never attempts an unauthorized seek. This is intended to distinguish Deezer playback limitations from Melisma D-pad navigation issues; Spotify's working seek control is unchanged.
3. Animated colour-field transitions are extended from about 1 second to **3.4 seconds** with slower easing. The underlying base palette also blends smoothly rather than changing immediately. The first frame with no outgoing background still displays promptly.
4. All previous custom TV improvements are retained, including direct remote Down to playback when the toolbar is hidden, the Settings sheet drag-handle fix and all-section D-pad focus improvements.

**Testing requested:** On a TCL/Onn/Shield, compare Deezer and Spotify during music playback; move from Pause to the seek position; check the ±ms button is first and highlighted when the top toolbar is opened; change songs to assess gradual background colour transitions. Device-level behaviour remains unverified until tested using the actual remote.

## v2.5.1 — Down reaches playback without showing the toolbar

**[GitHub Actions test build v2.5.1](https://github.com/MixterVee/Melisma-32bit/actions/runs/41575060841)**

Under **Artifacts**, download **`melisma-universal-v2.5.1-test-v2.11.0`**. Unzip it to get **`melisma-universal-v2.5.1-TEST.apk`**.

Remote behaviour to verify on the Onn:
- From the main lyric display with the top toolbar hidden, press **Down** to focus **Play/Pause** directly.
- Do not unhide the toolbar when using that shortcut.
- Once inside the player, normal **Up/Down/Left/Right** navigation (seek, skip and Open Player) must remain usable.
- **Up** while focus is on the idle root reveals the toolbar, with the previously-added hold-to-adjust timing shortcut still available.
- Settings, welcome screens, quick timing panel and selection mode must keep their own navigation.

Source changes track whether the playback panel already owns remote focus, so Down is not stolen from the player controls. The debug APK still uses temporary GitHub-run signing and may require uninstalling the old `com.melisma.app.debug` test app before installing. **This erases the test package's settings, not the official Melisma app.** The universal package includes both `armeabi-v7a` and `arm64-v8a`; device-level navigation remains to be tested.

## v2.5 UNIVERSAL test build — Onn, TCL and Nvidia Shield

**[Successful v2.5 build with downloadable APK](https://github.com/MixterVee/Melisma-32bit/actions/runs/41563268067)**

At the bottom of the workflow run, download artifact **`melisma-universal-v2.5-test-v2.11.0`**; extract **`melisma-universal-v2.5-TEST.apk`** (approximately 43 MB ZIP).

This is a debug/test package `com.melisma.app.debug`, **not** an update to the upstream official Melisma app. GitHub debug signing differs between runs: uninstall the earlier custom *debug* package first; this deletes only that package's saved settings and notification access.

```powershell
adb -s 192.168.1.206:5555 uninstall com.melisma.app.debug
adb -s 192.168.1.206:5555 install "C:\\path\\to\\melisma-universal-v2.5-TEST.apk"
```

Built and checked with both `armeabi-v7a` and `arm64-v8a`, for Android TV hardware including Onn 4K Pro, Nvidia Shield and supported TCL devices.

### What's new in v2.5

- **±ms** in the top toolbar opens a small live lyrics-sync panel instead of a modal settings sheet. It has a slider, ±50ms nudges, Reset, Done and D-pad focus styling.
- The live adjustment uses a temporary **per-track offset** (identified by non-null track cacheKey), reset to zero on a *different* track. Seeking, pausing and replaying the same song leave it unchanged.
- On the lyrics screen, the timing panel occupies a left side lane instead of covering the lyrics. It can also be opened with a **700 ms long hold of Up while the main lyrics/root has focus and the toolbar is hidden**; a short Up press continues to reveal the toolbar.
- The existing Settings → Timing offset on Android TV edits that same temporary track offset rather than saving a global one. Other platforms preserve upstream settings behaviour.
- TV settings modal's grab handle is hidden and swipe-to-drag disabled, to prevent the sheet moving off screen on first remote navigation. Phone touch interaction is preserved.
- Retains v2.4 settings D-pad improvements, v2.2 seek-focus escape and artwork-independent animated backgrounds.

**Device test needed:** The build passes Kotlin compilation, APK-signature checks and both ABI checks, but GitHub Actions cannot validate every Onn or Shield remote-navigation path. Test opening the side panel, changing lyrics timing during playback, replaying the same song, changing to a different song, and navigating settings on the physical remote.

## v2.4 — Settings-wide D-pad navigation audit

**[Successful GitHub build # v2.4](https://github.com/MixterVee/Melisma-32bit/actions/runs/41558999365)**

Download the **`melisma-32bit-v2.4-test-v2.11.0`** artifact from the run's **Artifacts** section. Unzip it to get **`melisma-32bit-v2.4-TEST.apk`**.

This release extends the v2.3 Background Style navigation fix across Melisma's Settings screen:

- Group expanded settings sections and their choice rows for remote traversal.
- Explicitly route Down from most expanded section headers into their first control.
- Focus all choice-button groups (Layout, Style, Spotify Canvas, fonts, translations, etc.), not only Background.
- Give toggles, sliders, actions, text fields, drag handles and the Settings Help button visible focus styling.
- Move a settings slider's focus Up/Down rather than trapping it; Left/Right still adjust the value.
- Scroll commonly focused controls into view.
- Preserve the v2.2 Cinema playback and seek-slider escape fixes.

**Limitations:** Settings with changing/dynamic content, such as Players to follow, still rely on Compose's focus search. Passing CI proves the source compiles and is 32-bit compatible, but cannot prove every Onn/TCL remote navigation path. Please test with your actual remote before considering the settings fixed.

Install: GitHub debug signing changes each run, so uninstall the old custom `com.melisma.app.debug` test app before installing this one. This deletes only that test app's saved settings. Do not uninstall the official Melisma app.

```powershell
adb -s 192.168.1.206:5555 uninstall com.melisma.app.debug
adb -s 192.168.1.206:5555 install "C:\\path\\to\\melisma-32bit-v2.4-TEST.apk"
```

## v2.2 — Fix seek bar remote focus trap

**[GitHub Actions v2.2 build](https://github.com/MixterVee/Melisma-32bit/actions/runs/41556450078)**

Download the `melisma-32bit-v2.2-test-v2.11.0` artifact from the run's **Artifacts** section and extract `melisma-32bit-v2.2-TEST.apk`.

This corrects a new v2.1 bug where the cyan-highlighted seek slider could trap D-pad focus on the Onn 4K Pro. On the seek slider:

- **Left / Right** continues to seek.
- **Down** focuses Play/Pause.
- **Back** focuses Play/Pause without stopping music.
- **Up** shows the top toolbar and focuses its first actionable button (when controls are at the top; otherwise returns to Play/Pause).

The parent Back handler was adjusted not to steal keys from focused child controls. The app still needs **physical remote validation** for a complete fix; GitHub compilation and APK verification cannot test remote focus behaviour.

Because GitHub's **debug** signing key changes on each runner, uninstall the previous custom test package `com.melisma.app.debug` first. This clears the test app settings and notification access. It does not remove the official Melisma app.

```powershell
adb -s 192.168.1.206:5555 uninstall com.melisma.app.debug
adb -s 192.168.1.206:5555 install "C:\\path\\to\\melisma-32bit-v2.2-TEST.apk"
```

## v2.1 TV playback-control fix — 32-bit TEST APK

**[Successful GitHub build: download here](https://github.com/MixterVee/Melisma-32bit/actions/runs/41555096341)**

Scroll to **Artifacts** and download **`melisma-32bit-v2.1-test-v2.11.0`**. Extract the ZIP to get **`melisma-32bit-v2.1-TEST.apk`**.

**What changed:** In v2 the top-menu auto-hide could steal focus from the player's transport buttons, and the global Up handler could intercept navigation. v2.1 avoids stealing playback focus, lets Down from a focused top toolbar move to the player, adds cyan focus borders to the playback/seek/Open Player controls in Cinema and Lyrics views, and lets Back reveal the hidden top toolbar.

**Onn installation:** GitHub Actions signs each debug APK using a new debug keystore. Uninstall the old `com.melisma.app.debug` first (this deletes only the custom test app's settings), then install the extracted new APK. Never uninstall the official `com.melisma.app` by mistake.

```powershell
adb -s 192.168.1.206:5555 uninstall com.melisma.app.debug
adb -s 192.168.1.206:5555 install "C:\\path\\to\\melisma-32bit-v2.1-TEST.apk"
```

**Test on real Onn remote before marking fixed:** focus after five seconds, Down from top menu to Play, Left/Right between Previous/Pause/Next, Up to seek, Down to Open Player, and Back to show top menu. Confirm that Emby casting and song-to-song lyrics stay uninterrupted. Compilation succeeds, but device navigation still needs confirmation.

## v2 TV test build (Melisma v2.11.0)

**[Successful GitHub build #3](https://github.com/MixterVee/Melisma-32bit/actions/runs/41552335012)**

Download the `melisma-32bit-v2-test-v2.11.0` artifact from the bottom of the run page, extract it, and install `melisma-32bit-v2-TEST.apk`.

Changes applied reproducibly by `scripts/patch_tv_ui.py` to the pinned upstream version:

- Artwork-free animated background and brighter colours.
- Cyan D-pad focus indicator on the top-toolbar chips.
- TV focus-outline support on common settings rows.
- Top toolbar fades after five idle seconds and can be restored by pressing **Up**.

**Test build only:** GitHub runners use a new debug signing key for each run. The v1 test APK (`com.melisma.app.debug`) may need to be **uninstalled** before installing v2. Uninstalling erases that test app's saved settings. This debug package does not replace the official Melisma app. Use v2 on an Onn first; verify Emby playback and remote focus before wider rollout.

Example for your currently configured Onn (replace the IP/path if different):

```powershell
adb -s 192.168.1.206:5555 uninstall com.melisma.app.debug
adb -s 192.168.1.206:5555 install "C:\\path\\to\\melisma-32bit-v2-TEST.apk"
```

The release workflow will also apply the v2 patch but still requires **persistent signing secrets** and remains manual-only until tested. An upstream code change can require updating this exact-source patch.


**Unofficial project** to test and automate Android 32-bit (`armeabi-v7a`) builds of [Melisma](https://github.com/MelismaApp/melisma) for Onn 4K Pro and TCL Google TV devices. Not affiliated with Melisma's developer.

## Why?

Melisma's official release (v2.11.0) is packaged for `arm64-v8a` only. The tested Onn 4K Pro and TCL Google TV report `armeabi-v7a,armeabi` through ADB, so they cannot install that APK. This project checks out an official upstream release and changes only the CPU architecture filter. **A successful build does not guarantee that the app will work on Android TV.**

## Phase 1: Test on one Onn device

1. Open this repository's **Actions** tab.
2. Choose **Test Melisma on 32-bit Android TV**.
3. Click **Run workflow**. Leave `upstream_tag` blank to use the latest stable release.
4. If the run succeeds, download its **melisma-32bit-test-vX.Y.Z** artifact (ZIP), extract `melisma-32bit-TEST.apk`, and sideload it on **one Onn first**.
5. Test whether Melisma launches, can obtain notification-listener access, and can detect music from your Android TV player.

The test is a **debug build** using package `com.melisma.app.debug` and an ephemeral signing key. It does **not** replace your existing official Melisma install and is not meant for in-place updates. It can be uninstalled after testing.

### Optional ADB installation (your Onn)

```powershell
adb -s 192.168.1.206:5555 install -r "C:\\path\\to\\melisma-32bit-TEST.apk"
```

Use the currently correct device IP if it changes. For notification access, try the Shield-like Android TV **Special app access → Notification access** menu first. If absent and you trust this build:

```powershell
adb -s 192.168.1.206:5555 shell cmd notification allow_listener com.melisma.app.debug/com.melisma.app.media.MediaNotificationListener
```

## Phase 2: Signed universal releases and updates (after testing)

The **Build and publish universal Melisma updates** workflow is deliberately **manual-only for now**: automatic daily checks should be enabled **only after** a 32-bit test build succeeds and signing secrets are configured. The production workflow, when enabled, will fetch new upstream tags and create a signed 32-bit/64-bit APK using the *same* key each time.

To configure signing:

1. Generate a private Android keystore with `scripts/create-signing-key.ps1` on your computer (Java `keytool` is required).
2. **Back up the keystore and passwords securely. Never upload or commit them.**
3. Go to **Settings → Secrets and variables → Actions → New repository secret** and create four repository secrets: `KEYSTORE_BASE64`, `KEYSTORE_PASSWORD`, `KEY_ALIAS` (`melisma_tv`), and `KEY_PASSWORD`. The script explains how to base64-encode the keystore locally.
4. Run the release workflow manually; after verifying the resulting APK we can enable its daily schedule.

**Signing warning:** The official Melisma release is signed by the developer. A custom release is signed with your key, so it **cannot update an official installation in place**. Keep the original app on your OnePlus 15 and Nvidia Shield; use the custom version on 32-bit TV devices. Every subsequent custom release must use the same key to update without uninstalling.

## Troubleshooting

- An `INSTALL_FAILED_NO_MATCHING_ABIS` error means an incompatible APK / missing 32-bit native libraries.
- A build failure due to changed upstream Gradle configuration is intentional: the ABI patch fails rather than silently producing the wrong APK.
- Building and installing are separate from **working runtime detection**: Android TV media apps must publish compatible media sessions, and Melisma needs notification-listener access.
- The upstream project and its dependencies can change; an upstream update may require the patch or build logic to be revised.
- Do not enable automatic signing of arbitrary unreviewed upstream changes if you do not trust the upstream source and build system.

[Melisma source](https://github.com/MelismaApp/melisma) · [Official releases](https://github.com/MelismaApp/melisma/releases)
