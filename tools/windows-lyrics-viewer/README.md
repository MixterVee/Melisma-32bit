# Melisma Universal — Windows Lyrics Preview v0.3.1

Windows 11 x64 · Foobar2000 · direct playback bridge

## What's new in v0.3.1

- **Complete long lyrics without ellipses.** Oversized lines wrap onto two centered rows, including on the TV, using word boundaries when available. Font size adjusts if needed.
- **No overlapping lyric rows.** Neighboring lines automatically move farther apart when a wrapped line needs extra height.
- **Gentler transitions.** The line change uses an ease-in/ease-out movement over 340 ms. Foobar2000 timing and seek handling are unchanged.
- **Enhanced LRC still works with wrapping.** Word highlighting follows the correct timestamped characters across both visual rows.

## Retained from v0.3

- **Per-song timing memory.** Adjust the lyrics with ±100 ms buttons; the offset is saved automatically and restored when you return to the same song. Reset also clears that song's saved correction.
- **Enhanced LRC word timing.** If an LRC file contains `<mm:ss.xx>` word markers, completed words are progressively highlighted. Ordinary timestamped lyrics continue to highlight complete lines (no fabricated word timing).
- **Smoother line transitions.** Adjacent lyric lines glide into position during normal playback. Large seeks jump directly to the correct line.
- **Longer lyrics fit better.** The viewer and TV display shrink oversized text to fit the available width before truncating it.

These changes apply to both the controller preview and the existing full-screen second-monitor display. The Foobar2000 bridge and Stem Separator are unchanged.

## Install and test

1. Leave your existing working Foobar2000 x64 Bridge component installed.
2. Download the `Melisma-Windows-Lyrics-Preview-v0.3.1-x64` ZIP from the GitHub Actions build for this branch and extract it into a **new** folder (keep v0.3 and v0.2 as backups).
3. Launch `MelismaLyricsViewer.exe`. Play a song in Foobar2000 and verify that lyrics follow it.
4. Verify your saved per-song +2000 ms correction is restored automatically, without re-entering it.
5. Pause, resume, and seek; check smooth transitions on regular lyric changes.
6. For dual-monitor testing, use **Show on second monitor** with Windows display mode set to **Extend**.
7. For word highlighting, open an **enhanced** .lrc file that includes inline `<mm:ss.xx>` timestamps. Standard LRCLIB timed lyrics generally provide only line timestamps.

## Notes

- Timing corrections are stored locally in `%LOCALAPPDATA%\MelismaUniversal\WindowsLyricsViewer\timing-offsets.json`.
- The maximum correction is ±20,000 ms. Settings never modify audio files, MP3 tags, Foobar2000 settings, or the bridge.
- Playback metadata and direct bridge timing are read-only. Automatic LRCLIB searches send track title, artist, optional album and duration, **not** audio.
- No Firebase Cloud Sync, album-art backgrounds or stem controls are included yet.
- Windows 11 64-bit; self-contained .NET 8 build. The app can be unsigned during testing; use only artifacts from this repository.

Source: `tools/windows-lyrics-viewer/` and `.github/workflows/windows-lyrics-viewer.yml`.
