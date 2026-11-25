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

    private static string CueTime(long milliseconds) =>
        TimeSpan.FromMilliseconds(Math.Max(0, milliseconds))
            .ToString(@"mm\:ss\.fff");

    internal static int EffectiveTier(Answer answer, int fullest)
    {
        if (answer.Total == 0) return 0;
        // Android's effectiveKind demotes word-timed fragments that have
        // fewer than 60% as many lines as the most complete answer.
        if (answer.WordTimed && (fullest == 0 ||
            answer.Total >= fullest * 0.6f)) return 2;
        return 1;
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
            Query("netease", "NetEase YRC (Android source)", async ct =>
                await NetEaseWordProvider.FindAsync(http, title, artist,
                    durationMs, ct), token),
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
            ["lrcred"] = 0, ["amll"] = 1, ["netease"] = 2,
            ["better"] = 3, ["lrclib"] = 4
        };
        int fullest = all.Max(a => a.Total);
        var reference = all.FirstOrDefault(a => a.Id == "lrclib")?.Lines;
        var best = all
            .Where(a => a.Total > 0)
            .OrderByDescending(a => EffectiveTier(a, fullest))
            .ThenByDescending(a => fullest > 0 && a.Total >= fullest * 0.6)
            // A word-timed file missing an audible first verse should lose
            // to one with genuine timing for that verse, even if provider
            // preference would otherwise rank it higher.
            .ThenBy(a => a.WordTimed
                ? LyricCoverage.MissingIntroLineCount(a.Lines, reference) : 0)
            .ThenBy(a => preference[a.Id])
            .ThenByDescending(a => a.Total)
            .FirstOrDefault();

        string status = string.Join(Environment.NewLine, all.Select(a =>
            $"- {a.Name}: {(a.Total > 0
                ? $"{a.Total} lines, {a.Timed} word-timed, " +
                  $"first cue {CueTime(a.Lines!.Min(x => x.AtMs))} " +
                  $"({(a.WordTimed ? "Word" : "Line")} tier)"
                : a.Reason)}"));

        // Timed syllables remain authoritative; other services only
        // restore spaces missing from a provider's raw English YRC text.
        // Never combine lines whose actual non-space characters differ.
        if (best is { WordTimed: true, Lines: { Count: > 0 } })
        {
            var referenceLines = all
                .Where(x => x.Id != best.Id && x.Lines is { Count: > 0 })
                .SelectMany(x => x.Lines!)
                .ToList();
            if (referenceLines.Count > 0)
            {
                var (spaced, fixedLines) = LyricWordSpacing.Restore(
                    best.Lines, referenceLines);
                if (fixedLines > 0)
                {
                    best = best with { Lines = spaced };
                    status += Environment.NewLine +
                        $"Word spacing restored on {fixedLines} matching " +
                        "timed lines using another source (timing unchanged).";
                }
            }
        }

        if (best is { WordTimed: true, Lines: { Count: > 0 } } &&
            reference is { Count: > 0 } && best.Id != "lrclib")
        {
            var (combined, introCount) = LyricCoverage.AddMissingIntro(
                best.Lines, reference);
            if (introCount > 0)
            {
                best = best with
                {
                    Name = best.Name + " + LRCLIB intro",
                    Lines = combined,
                    Reason = $"{introCount} genuine line-timed early vocal " +
                        "cues restored from LRCLIB; later word timing unchanged"
                };
                status += Environment.NewLine + $"Intro coverage repaired: " +
                    $"{introCount} early line-timed cues added from LRCLIB " +
                    $"ahead of the word-timed {best.Id} document.";
            }
        }

        return new Lookup(best, all, status);
    }
}
