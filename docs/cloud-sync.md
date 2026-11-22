# Melisma Universal v2.8.12 — Optional Firebase cloud sync

**Status:** Cloud sync implementation and TV Settings UI are in the v2.8.12 test build, but it is **not active by default** and cannot be integration-tested without a configured Firebase project. v2.8.11 local memory remains the playback source of truth.

## What synchronizes

- Per-track manually chosen lyrics provider (the existing `sourceChoices` settings).
- Per-track/per-provider timing offsets (the `songOffsets` preferences added in v2.8.11).
- Resets: Automatic provider and Reset to 0 become cloud tombstones, so corrections can be cleared on every device.

**Not synchronized:** the lyrics documents or cache, album art, audio, passwords, provider credentials, playback history, personal user data, other Settings, or Emby/Amazon accounts.

The user is authenticated through the Firebase Android SDK. Only Firebase email/password sign-in is supported in this first version. The SDK persists authentication securely in private app storage and uses Firestore's offline queue and listener. User-visible correction preferences are always stored locally first. Devices signed in to the **same Firebase user** see each other's changes; different accounts are isolated using Firestore Security Rules.

## 1. Create your own Firebase project (one-time)

1. Go to [Firebase Console](https://console.firebase.google.com/), sign in to your Google account and create a project. Analytics isn't needed for Melisma cloud corrections.
2. Select **Build → Authentication → Sign-in method → Email/Password**, then enable the email/password provider.
3. In **Authentication → Users**, create a user with an email and strong password. The same account will be used on your TVs.
4. Select **Build → Firestore Database** and create a database.
5. Replace the Firestore rules with [firebase/firestore.rules](../firebase/firestore.rules) from this repository. Verify the rules are deployed and restrict every document to its owning Firebase Auth UID.
6. In **Project settings → Your apps**, register an Android application with package name **`com.melisma.app.debug`** for our test APK. (For an eventual release build, register its actual release package separately.)
7. Download the **google-services.json** for that registered app **to your own computer only**. Do **not** commit it to GitHub or post it in chat. Use the following three **public Firebase identifiers** in Melisma:
   - **Project ID:** `project_info.project_id`
   - **Android App ID:** `client[0].client_info.mobilesdk_app_id`
   - **Web API key:** `client[0].api_key[0].current_key`
   - These identifiers are configuration values, not credentials. Firestore Security Rules and Firebase Authentication control access.
8. Check that **API key restrictions**, if configured, permit Firebase Authentication on the test package and signing certificate, or sign-in may fail.

## 2. Connect each TV

1. Install **Melisma Universal v2.8.12**, open **Settings → Cloud sync**.
2. Enter the project ID, Android App ID, and Web API key; select **Save Firebase configuration**.
3. Enter the account email/password created above and select **Sign in**. Password is discarded from the Melisma Settings UI after the attempt.
4. Watch for the status **Connected · corrections syncing automatically**. If unavailable, local playback and correction memory still work.
5. Use **Sync now** for a server read/connection status check; ordinary changes are pushed automatically.
6. Repeat the configuration/sign-in with the same account on your Shield, Onn and TCL devices.

## Security and known limits

- Cloud connectivity is **opt-in**. Firebase account credentials and app configuration are **never embedded** in the built APK.
- Firestore records are under `users/{uid}/corrections/{hashedEntryKey}`, using SHA-256 keys. The record body contains the plain track identity and correction value. Track identities can disclose listening habits, so keep Firestore private.
- Firestore is a **last-write-wins** store for concurrent edits to the same track/provider. Different songs/providers use different documents. Firestore offline changes are queued for retry.
- Device-local changes made while the cloud listener is disconnected are staged as pending records in private storage; pending edits take priority when reconnecting. First linking an existing TV prefers cloud values where both devices already contain a value, unless a local edit was pending.
- Each test APK is a separately installed debug app. **Do not uninstall without a Settings backup.** Installing a differently signed debug APK may force uninstall and erase local corrections.
- A change to the Firebase project configuration after initialization requires restarting Melisma.
- Firebase may require billing or quota monitoring depending on project configuration; check the Firebase Console. No cloud integration has been tested against the user's account yet.
- QR-code activation/pairing is **not included**. This first version uses one-time email/password sign-in on each TV. A secure QR pairing service needs a separately deployed server-side authorization flow, and should not expose an account password or reusable access token in a QR image.
