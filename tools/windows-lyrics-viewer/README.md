# Melisma Universal — Windows Lyrics Preview v0.3

Windows 11 x64 · Foobar2000 · direct playback bridge

## What's new in v0.3

- **Per-song timing memory.** Adjust the lyrics with ±100 ms buttons; the offset is saved automatically and restored when you return to the same song. Reset also clears that song's saved correction.
- **Enhanced LRC word timing.** If an LRC file contains `<mm:ss.xx>` word markers, completed words are progressively highlighted. Ordinary timestamped lyrics continue to highlight complete lines (no fabricated word timing).
- **Smoother line transitions.** Adjacent lyric lines glide into position during normal playback. Large seeks jump directly to the correct line.
- **Longer lyrics fit better.** The viewer and TV display shrink oversized text to fit the available width before truncating it.

These changes apply to both the controller preview and the existing full-screen second-monitor display. The Foobar2000 bridge and Stem Separator are unchanged.

## Install and test

1. Leave your existing working Foobar2000 x64 Bridge component installed.
2. Download the `Melisma-Windows-Lyrics-Preview-v0.3-x64` ZIP from the GitHub Actions build for this branch and extract it into a **new** folder (keep v0.2 as a backup).
3. Launch `MelismaLyricsViewer.exe`. Play a song in Foobar2000 and verify that lyrics follow it.
4. Adjust timing, close the viewer, reopen it and replay the same song. The saved offset should reappear.
5. Switch to a different song; its correction should be independent. Use **Reset** to clear an offset for the current song.
6. For dual-monitor testing, use **Show on second monitor** with Windows display mode set to **Extend**.
7. For word highlighting, open an **enhanced** .lrc file that includes inline `<mm:ss.xx>` timestamps. Standard LRCLIB timed lyrics generally provide only line timestamps.

## Notes

- Timing corrections are stored locally in `%LOCALAPPDATA%\MelismaUniversal\WindowsLyricsViewer\timing-offsets.json`.
- The maximum correction is ±20,000 ms. Settings never modify audio files, MP3 tags, Foobar2000 settings, or the bridge.
- Playback metadata and direct bridge timing are read-only. Automatic LRCLIB searches send track title, artist, optional album and duration, **not** audio.
- No Firebase Cloud Sync, album-art backgrounds or stem controls are included yet.
- Windows 11 64-bit; self-contained .NET 8 build. The app can be unsigned during testing; use only artifacts from this repository.

Source: `tools/windows-lyrics-viewer/` and `.github/workflows/windows-lyrics-viewer.yml`.
