namespace MelismaLyricsViewer;

/// <summary>
/// A mapped file remains alive in the reader after Foobar2000 exits. A
/// successful memory-mapped read alone does not mean the playback clock is
/// still advancing. Compare the native writer's monotonic tick against the
/// Windows monotonic clock; release stale views and reconnect.
/// </summary>
internal static class PlaybackClockHealth
{
    public const long MaxSampleAgeMs = 1800;

    public static bool Fresh(long nowTickMs, long sampleTickMs) =>
        sampleTickMs > 0 &&
        nowTickMs - sampleTickMs >= -200 &&
        nowTickMs - sampleTickMs <= MaxSampleAgeMs;
}
