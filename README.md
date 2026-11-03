# Melisma 32-bit / Android TV compatibility builds

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
