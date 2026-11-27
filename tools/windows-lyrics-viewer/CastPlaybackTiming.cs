using System.Text.Json;

namespace MelismaLyricsViewer;

/// <summary>
/// Chrome Cast tab transport has an independent video latency. Advance or
/// retard only the cast TV's visual playhead; never modify local Foobar
/// audio, cached source timestamps or saved per-song lyric offsets.
/// </summary>
internal static class CastPlaybackTiming
{
    internal const int StepMs = 250;
    internal const int MaxLeadMs = 4000;

    internal static int ClampLead(int value) =>
        Math.Clamp(value, -MaxLeadMs, MaxLeadMs);

    internal static long Apply(long lyricPositionMs, int leadMs) =>
        Math.Max(0L, lyricPositionMs + ClampLead(leadMs));

    internal static int ActiveLine(IReadOnlyList<LrcLine> lines, long positionMs)
    {
        int active = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].AtMs > positionMs) break;
            active = i;
        }
        return active;
    }
}

internal sealed class CastTimingSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MelismaUniversal", "WindowsLyricsViewer", "cast-timing.json");

    internal static int Read()
    {
        try
        {
            if (!File.Exists(FilePath)) return 0;
            var setting = JsonSerializer.Deserialize<CastTimingPayload>(
                File.ReadAllText(FilePath));
            return CastPlaybackTiming.ClampLead(setting?.LeadMs ?? 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            JsonException)
        {
            return 0;
        }
    }

    internal static bool Save(int leadMs)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(
                new CastTimingPayload(CastPlaybackTiming.ClampLead(leadMs))));
            File.Move(temp, FilePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record CastTimingPayload(int LeadMs);
}
