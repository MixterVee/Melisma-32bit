# Melisma TV Receiver v0.1.0 — browser-free Android TV karaoke

**Companion for Melisma Windows v0.6.25.** Unlike Google Cast/tab casting, this is a native local-network receiver app. It can be installed once on your NVIDIA Shield, Onn 4K Pro, or Google TV. It requires **no Chrome, browser tab, Google Cast SDK, app registration, or Internet account**.

## Install

1. Transfer `Melisma-TV-Receiver-v0.1.0.apk` from the Android Actions artifact to an Android TV (for example with LocalSend or a USB drive), and install it. The TV may require permission to install apps from unknown sources. This test APK is debug-signed, not yet a Play Store release.
2. Open **Melisma Receiver** from the TV apps list. It displays its local IPv4 address and a six-digit pairing code. This code is stored on that TV, so a restart doesn't change it.
3. On the same home LAN, start Foobar2000 and **Melisma Universal Windows v0.6.25** with native Foobar Bridge v0.4.
4. In Windows Melisma choose **TV / Cast → Connect directly to Android TV (no browser)**. Select the discovered Shield/Onn/Google TV, enter the six-digit code shown by the TV app, and press Connect.
5. If no receiver is automatically found, enter the TV's displayed IPv4 address into the Windows dialog. Discovery uses UDP broadcast and may be blocked by guest networks/VLANs/VPNs; direct TCP works across routed LANs only when permitted by your router. Windows connects *outbound* to the TV on TCP port **49831**, so it should not need an inbound Windows Firewall exception.
6. The TV fills its screen with 1280×720 JPEG karaoke frames while the Windows PC remains the controller. Foobar's music stays on its configured PC audio device, and **is not transmitted to the TV**. To adjust TV-only lyrics latency, use **TV / Cast → Cast lyric timing (TV only)**, in ±250 ms increments.
7. To end a session use **TV / Cast → Stop direct TV casting**, or exit Melisma Receiver on the TV. Back/Exit from the TV remote closes the TV receiver. Press OK/Enter on the remote to show or hide the connection information.

## Technology / privacy

The TV app listens only while on screen, on TCP 49831 for a single authenticated, length-prefixed JPEG stream, and UDP 47916 for best-effort device discovery. Its six-digit pairing code must match before frames are accepted. The Windows sender uses a one-frame bounded buffer so it drops old frames instead of building visual delay over slower Wi-Fi. No usernames, network APIs, router port forwards or MP3 files are involved. This is NOT protocol-level Google Cast; it is a direct Wi-Fi/Ethernet LAN connection between Windows Melisma and the Android TV app.

The video feed is sent over the local network without TLS; use only on a trusted LAN. The six-digit code is a pairing convenience, not strong protection against a hostile local network.

## Test checklist

- Confirm TV discovery and pairing (including incorrect-code rejection and manual IP fallback).
- Confirm word animations, interlude dots, background cover transitions and line spacing survive projection to the TV.
- Change songs and exit/restart Foobar2000: old lyrics must never scroll to new music.
- TV-only timing adjustment should compensate for any Wi-Fi/display latency without changing Foobar audio or PC-side timing.
- Close/open the TV app or disconnect Wi-Fi: Windows should recover without freezing or leaving a growing frame queue.
- Test the exact TV and surround-system audio path you plan to use before guests arrive.

This is a **first test build**. Real Shield/Onn/TCL compatibility and Wi-Fi latency still need confirmation.
