# Melisma Media Detector — Foobar2000 (Windows proof of concept)

A small **read-only Windows 11 app** to test whether Foobar2000 exposes usable playback timing for a future Melisma Universal Karaoke app.

The detector uses Windows' **Global System Media Transport Controls** (GSMTC) API. It does not install a Foobar2000 component, change Foobar settings, analyze audio, modify MP3 tags, or send any data to Firebase.

## Download and run

1. In the GitHub Actions run for **Build Melisma Windows Media Detector**, download the `MelismaMediaDetector-win-x64` artifact ZIP.
2. Extract it and open `MelismaMediaDetector.exe`.
3. Play an MP3 in Foobar2000.
4. Watch **Position reported by Windows**, **Estimated live position**, duration, and timeline age. Test pause, seek and track changes.
5. Click **Save CSV** to export timing samples (local file only).

**Windows 11 64-bit.** Self-contained .NET 8 executable; .NET installation not needed. This is an unsigned test application, so Windows SmartScreen or antivirus may warn. Only run binaries you trust; source is in this directory.

## How to interpret it

- **Reported position** is the raw playback time provided by the Windows media session.
- **Estimated position** adds elapsed time since the Windows timeline's last reported timestamp while playing. This is an approximation, not a guarantee of synchronized karaoke timing.
- **Timeline age** indicates when the source said it last updated its position. Large values or jumps imply Windows API alone may be too imprecise.
- **Reported position changes** counts changed raw position readings; test seeks and pauses.

If the media panel displays Foobar2000 but the detector sees no usable timeline, our next step is a small optional Foobar2000 component that reports accurate playback callbacks directly.

## Planned follow-on

Use this detector's results to choose a timing source, then add Melisma lyrics rendering, Stem Separator integration, and HDMI extended-display karaoke. Later, add low-latency streaming to Melisma on Android TV.

## Build from source

Requires Windows 11 and .NET SDK 8:

```powershell
dotnet run --project tools/windows-media-detector/MelismaMediaDetector.csproj
```

Project inherits the repository's AGPL-3.0 license and upstream attribution requirements.
