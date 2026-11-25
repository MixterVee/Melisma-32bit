namespace MelismaLyricsViewer;

internal static class ProviderProgressTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        // Simulate an immediately responding line-synced provider and a
        // slow word-timed service. The UI must not wait for the second one.
        var slowWordSource = new TaskCompletionSource<
            LyricsSourceOrchestrator.Answer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new LyricsSourceOrchestrator.Answer(
            "lrclib", "LRCLIB", Enumerable.Range(0, 28)
                .Select(i => new LrcLine(8000 + i * 1200,
                    $"early lyric {i}")).ToList(), "ready", false);
        var later = new LyricsSourceOrchestrator.Answer(
            "netease", "NetEase YRC", Enumerable.Range(0, 28)
                .Select(i => new LrcLine(8000 + i * 1200,
                    $"word timed lyric {i}", [
                        new LrcWord(8000 + i * 1200, "word "),
                        new LrcWord(8400 + i * 1200, $"timed lyric {i}")
                    ])).ToList(), "ready", false);

        var snapshots = new List<LyricsSourceOrchestrator.Lookup>();
        Task<LyricsSourceOrchestrator.Lookup> running =
            LyricsSourceOrchestrator.CollectAsAvailableAsync(
                [Task.FromResult(first), slowWordSource.Task],
                CancellationToken.None, result => snapshots.Add(result));

        check(snapshots.Count == 1,
            "First lyrics published before slow provider has completed");
        check(!running.IsCompleted,
            "Other providers can continue searching without blocking first lyrics");
        check(snapshots[0].Best?.Id == "lrclib" &&
            snapshots[0].Best!.Total == 28,
            "Line-synced provisional result is immediately usable");

        slowWordSource.SetResult(later);
        var chosen = await running;
        check(chosen.Best?.Id == "netease" && chosen.Best!.WordTimed,
            "Better genuine word-timed result upgrades initial line lyrics");
        check(snapshots.Count == 2 &&
            snapshots[1].Best?.Id == "netease",
            "Upgrade delivered to controller as soon as word source finishes");

        var noLyrics = await LyricsSourceOrchestrator.CollectAsAvailableAsync(
            [Task.FromResult(new LyricsSourceOrchestrator.Answer(
                "amll", "AMLL", null, "no match", false))],
            CancellationToken.None);
        check(noLyrics.Best == null,
            "No-match providers do not generate invented timed lyric lines");

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool stopped = false;
        try
        {
            await LyricsSourceOrchestrator.CollectAsAvailableAsync(
                [Task.FromResult(first)], canceled.Token);
        }
        catch (OperationCanceledException) { stopped = true; }
        check(stopped,
            "Canceled track search cannot display the previous song's lyrics");
    }
}
