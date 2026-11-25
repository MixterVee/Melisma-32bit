using System.Net.Http;
using System.Text.Json;
using System.Xml;

namespace MelismaLyricsViewer;

/// <summary>
/// Mirrors Android Melisma's central idea: ask all configured accessible
/// providers concurrently, rank genuine word timing above line timing,
/// and report each provider's outcome. Individual HTTP failures cannot
/// suppress lyrics found by another provider.
/// </summary>
internal static class LyricsSourceOrchestrator
{
    internal sealed record Answer(
        string Id, string Name, List<LrcLine>? Lines, string Reason, bool Failed)
    {
        public int Total => Lines?.Count ?? 0;
        public int Timed => Lines?.Count(x => x.Words is { Count: > 1 }) ?? 0;
        // Matches Android's MIN_SYLLABLE_COVERAGE=0.5 rule.
        public bool WordTimed => Total > 0 && Timed > 0 &&
            Timed / (float)Total >= 0.5f;
        public int Tier => WordTimed ? 2 : Total > 0 ? 1 : 0;
    }

    internal sealed record Lookup(
        Answer? Best, IReadOnlyList<Answer> All, string Report)
    {
        public bool AnyWordTimed => All.Any(x => x.WordTimed);
    }

    private static async Task<Answer> Query(
        string id, string name, Func<CancellationToken, Task<List<LrcLine>?>> action,
        CancellationToken parent)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(parent);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var lines = await action(timeout.Token);
            if (parent.IsCancellationRequested) parent.ThrowIfCancellationRequested();
            if (lines is not { Count: > 0 })
                return new Answer(id, name, null,
                    "No accessible, matching word-timed lyrics", false);
            return new Answer(id, name, lines,
                $"{lines.Count} lines; {lines.Count(x => x.Words is { Count: > 1 })} with word timing",
                false);
        }
        catch (OperationCanceledException) when (parent.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new Answer(id, name, null, "Timed out after 12 seconds", true);
        }
        catch (Exception ex) when (ex is HttpRequestException or
                 TaskCanceledException or JsonException or XmlException or
                 InvalidOperationException or ArgumentException)
        {
            return new Answer(id, name, null,
                $"{ex.GetType().Name}: {ex.Message}", true);
        }
    }

    internal static async Task<Lookup> QueryAsync(
        HttpClient http, string title, string artist, string album, long durationMs,
        Func<CancellationToken, Task<List<LrcLine>?>> lineFallback,
        CancellationToken token)
    {
        // Android's provider order is a tie-breaker, not a reason to let
        // the first line-timed provider suppress a word-timed result.
        var jobs = new Task<Answer>[]
        {
            Query("lrcred", "lrc.red", async ct =>
                await LrcRedWordProvider.FindAsync(http, title, artist,
                    durationMs, ct), token),
            Query("amll", "AMLL TTML DB", async ct =>
                await AmllWordProvider.FindAsync(http, title, artist, ct), token),
            Query("better", "Better Lyrics (TTML / QQ)", async ct =>
            {
                var result = await BetterLyricsWordProvider.FindAsync(http,
                    title, artist, album, durationMs, ct);
                return result?.Lines;
            }, token),
            Query("lrclib", "LRCLIB", lineFallback, token),
        };
        var all = await Task.WhenAll(jobs);
        token.ThrowIfCancellationRequested();

        // Android's SOURCE order: lrc.red before AMLL; user's local lyrics
        // are handled before network lookup in the controller.
        var preference = new Dictionary<string, int>
        {
            ["lrcred"] = 0, ["amll"] = 1, ["better"] = 2, ["lrclib"] = 3
        };
        int fullest = all.Max(a => a.Total);
        var best = all
            .Where(a => a.Total > 0)
            .OrderByDescending(a => a.Tier)
            .ThenByDescending(a => fullest > 0 && a.Total >= fullest * 0.6)
            .ThenBy(a => preference[a.Id])
            .ThenByDescending(a => a.Total)
            .FirstOrDefault();
        string status = string.Join(Environment.NewLine, all.Select(a =>
            $"- {a.Name}: {(a.Total > 0
                ? $"{a.Total} lines, {a.Timed} word-timed ({(a.WordTimed ? "Word" : "Line")} tier)"
                : a.Reason)}"));
        return new Lookup(best, all, status);
    }
}
