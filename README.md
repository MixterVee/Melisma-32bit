# Melisma 32-bit / Android TV compatibility builds

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
