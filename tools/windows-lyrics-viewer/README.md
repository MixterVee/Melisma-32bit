# Melisma Universal — Windows Lyrics Preview v0.2

First Windows karaoke lyric-display prototype for Foobar2000.

## Features

- Uses the existing **Melisma Foobar2000 Bridge** to read playback position, pause, seek, track transitions and duration directly from Foobar2000.
- Uses Windows Global System Media Transport Controls (GSMTC) to obtain Foobar2000 song title, artist and album.
- Automatically searches **LRCLIB** for time-synced lyrics. Sends title, artist and duration to LRCLIB for matching; no MP3 files or audio are uploaded.
- Can open a local **.lrc** lyrics file instead. Only timestamped lyrics are shown as synchronized.
- Highlights the current lyric line in a preview window; **Show on second monitor** opens a separate full-screen lyrics window on the TV while controls stay on the laptop.
- Live plus/minus 100ms correction buttons for testing line timing. **No cloud sync or stem controls yet.**

## How to test

1. Install our previous Foobar2000 x64 Bridge component and restart Foobar2000.
2. Download this Windows Lyrics Preview artifact ZIP; extract and start MelismaLyricsViewer.exe.
3. Play a track in Foobar2000. It should detect the title, look up synchronized lyrics and follow the playback position.
4. If no suitable synced lyrics are found, choose **Open .lrc** and select a time-stamped LRC file yourself.
5. Pause, seek, and switch tracks to test line highlighting. Use +/-100ms buttons to adjust timing experimentally.
6. Connect your laptop to a TV with HDMI, press Win+P → Extend, then choose **Show on second monitor**. The controller remains on the laptop and TV displays the lyrics full-screen.

### Notes

- Windows 11 x64; self-contained .NET 8 build, no extra .NET installation.
- This first prototype supports **line-synchronized LRC lyrics**. It does not yet support word-by-word animation; that will be a later development milestone.
- LRCLIB currently provides a free public API; an internet connection is needed for automatic lookup. Rate limiting is respected by making requests only on track changes or manual retry.
- If using copyrighted lyrics, comply with their source's terms. The viewer only displays lyrics on your computer and does not redistribute them.
- Playback timing is read-only, and no playback or DSP controls are issued to Foobar2000. The viewer does not alter MP3 tags or the Stem Separator.
- Second-monitor mode shows a **separate lyrics-only window**. Foobar2000 and the controller stay on the Windows laptop.
- Windows lyrics offset is currently an in-memory test adjustment and not synced to Firebase.

Project uses the existing Melisma Foobar2000 Bridge read-only shared-memory interface.
