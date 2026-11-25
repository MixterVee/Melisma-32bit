using System.Text.Json;
using MelismaLyricsViewer;

var songs = new[]
{
    (Title: "Blinding Lights", Artist: "The Weeknd", Album: "After Hours", Duration: 200000L),
    (Title: "Shake It Off", Artist: "Taylor Swift", Album: "1989", Duration: 219000L),
    (Title: "Shape of You", Artist: "Ed Sheeran", Album: "Divide", Duration: 234000L)
};
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
client.DefaultRequestHeaders.UserAgent.ParseAdd("MelismaWindowsProviderProbe/0.6.2");

foreach (var song in songs)
{
    Console.WriteLine($"=== {song.Title} / {song.Artist} ===");
    string E(string s) => Uri.EscapeDataString(s);
    await Inspect($"AMLL search by title",
        $"https://api.amll.dev/v1/lyrics/search?musicName={E(song.Title)}&pageSize=20",
        "data.items", true);
    await Inspect($"AMLL search by title+artist",
        $"https://api.amll.dev/v1/lyrics/search?musicName={E(song.Title)}&artistName={E(song.Artist)}&pageSize=20",
        "data.items", true);
    var queryArgs = BetterLyricsWordProvider.Query(song.Title, song.Artist, song.Album, song.Duration);
    await Inspect("BetterLyrics TTML", $"https://api.betterlyrics.org/getLyrics?{queryArgs}",
        "ttml", false);
    await Inspect("BetterLyrics QQ", $"https://api.betterlyrics.org/qq/getLyrics?{queryArgs}",
        "lyrics", false);
    await ProbeLrcRed(song.Title, song.Artist);

    try
    {
        var red = await LrcRedWordProvider.FindAsync(
            client, song.Title, song.Artist, song.Duration, CancellationToken.None);
        Console.WriteLine($"  lrc.red application result: {(red == null ? "none" : $"{red.Count} lines, {red.Count(x => x.Words is { Count: > 1 })} word-timed")}");
    }
    catch (Exception ex) { Console.WriteLine($"  lrc.red application exception: {ex.GetType().Name}: {ex.Message}"); }
    // Diagnose whether source cue times agree with audio at 24 seconds.
    try
    {
        var timed = await LrcRedWordProvider.FindAsync(client, song.Title,
            song.Artist, song.Duration, CancellationToken.None);
        if (timed != null)
        {
            Console.WriteLine("  lrc.red first cue ms: " +
                string.Join(", ", timed.Take(8).Select(x => x.AtMs)));
            Console.WriteLine("  lrc.red final cue ms: " +
                string.Join(", ", timed.TakeLast(3).Select(x => x.AtMs)));
            Console.WriteLine("  lrc.red active index at 24s: " +
                timed.FindLastIndex(x => x.AtMs <= 24000));
            Console.WriteLine("  first word start ms: " +
                string.Join(", ", timed[0].Words?.Take(5).Select(x => x.AtMs) ??
                    Array.Empty<long>()));
        }
    }
    catch (Exception ex) { Console.WriteLine("  cue sample failed: " + ex.Message); }


    try
    {
        var amll = await AmllWordProvider.FindAsync(client, song.Title, song.Artist, CancellationToken.None);
        Console.WriteLine($"  AMLL application result: {(amll == null ? "none" : $"{amll.Count} lines, {amll.Count(x => x.Words is { Count: > 1 })} word-timed")}");
    }
    catch (Exception ex) { Console.WriteLine($"  AMLL application exception: {ex.GetType().Name}: {ex.Message}"); }
    try
    {
        var better = await BetterLyricsWordProvider.FindAsync(client, song.Title, song.Artist, song.Album, song.Duration, CancellationToken.None);
        Console.WriteLine($"  Better application result: {(better == null ? "none" : $"{better.Value.Source}, {better.Value.Lines.Count} lines, {better.Value.Lines.Count(x => x.Words is { Count: > 1 })} word-timed")}");
    }
    catch (Exception ex) { Console.WriteLine($"  Better application exception: {ex.GetType().Name}: {ex.Message}"); }
    await Task.Delay(350);
}

async Task Inspect(string label, string url, string field, bool isSearch)
{
    try
    {
        using var response = await client.GetAsync(url);
        string body = await response.Content.ReadAsStringAsync();
        Console.Write($"  {label}: HTTP {(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($" response: {body[..Math.Min(130, body.Length)].Replace('\n', ' ')}");
            return;
        }
        using var json = JsonDocument.Parse(body);
        JsonElement item = json.RootElement;
        foreach (var part in field.Split('.'))
        {
            if (!item.TryGetProperty(part, out var member))
            {
                Console.WriteLine($" missing field {field}; keys = {string.Join(",", json.RootElement.EnumerateObject().Select(p => p.Name))}");
                return;
            }
            item = member;
        }
        if (isSearch)
        {
            if (item.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine($" unexpected items type {item.ValueKind}");
                return;
            }
            Console.WriteLine($" candidates={item.GetArrayLength()}");
            int inspected = 0;
            foreach (var candidate in item.EnumerateArray())
            {
                if (++inspected > 3) break;
                string name = candidate.TryGetProperty("musicNames", out var t)
                    ? t.ToString() : "missing";
                string artist = candidate.TryGetProperty("artistNames", out var a)
                    ? a.ToString() : "missing";
                Console.WriteLine($"    candidate: title={name[..Math.Min(90,name.Length)]}, artist={artist[..Math.Min(90,artist.Length)]}");
            }
        }
        else
        {
            string raw = item.GetString() ?? "";
            Console.WriteLine($" document chars={raw.Length}");
            List<LrcLine> parsed = field == "ttml"
                ? WordTimedLyrics.ParseTtml(raw)
                : QrcWordParser.Parse(raw);
            Console.WriteLine($"    parser={parsed.Count} lines, {parsed.Count(l => l.Words is { Count: > 1 })} word-timed, HasWordTimings={WordTimedLyrics.HasWordTimings(parsed)}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  {label}: EXCEPTION {ex.GetType().Name}: {ex.Message}");
    }
}

async Task ProbeLrcRed(string title, string artist)
{
    string search = "https://lrc.red/search.json?q=" +
        Uri.EscapeDataString(title + " " + artist);
    try
    {
        using var response = await client.GetAsync(search);
        string body = await response.Content.ReadAsStringAsync();
        Console.Write($"  lrc.red search: HTTP {(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($" {body[..Math.Min(150, body.Length)]}");
            return;
        }
        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("hits", out var hits) ||
            hits.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine($" schema={string.Join(",", json.RootElement.EnumerateObject().Select(p => p.Name))}");
            return;
        }
        Console.WriteLine($" hits={hits.GetArrayLength()}");
        foreach (var hit in hits.EnumerateArray().Take(3))
        {
            string titleHit = hit.TryGetProperty("title", out var n) ? n.GetString() ?? "" : "";
            string artistHit = hit.TryGetProperty("artist", out var a) ? a.GetString() ?? "" : "";
            string isrc = hit.TryGetProperty("isrc", out var ir) ? ir.GetString() ?? "" : "";
            Console.WriteLine($"    hit: {titleHit} / {artistHit} ({isrc})");
            if (!string.Equals(title, titleHit, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(artist, artistHit, StringComparison.OrdinalIgnoreCase) ||
                isrc.Length == 0) continue;
            using var lyricResponse = await client.GetAsync("https://lrc.red/s/" +
                Uri.EscapeDataString(isrc) + ".ttml");
            Console.WriteLine($"    lrc.red TTML: HTTP {(int)lyricResponse.StatusCode}");
            if (!lyricResponse.IsSuccessStatusCode) continue;
            string xml = await lyricResponse.Content.ReadAsStringAsync();
            var parsed = WordTimedLyrics.ParseTtml(xml);
            Console.WriteLine($"    lrc.red parsed={parsed.Count} lines, timed={parsed.Count(l => l.Words is { Count: > 1 })}");
            break;
        }
    }
    catch (Exception ex) { Console.WriteLine($"  lrc.red: {ex.GetType().Name}: {ex.Message}"); }
}

namespace MelismaLyricsViewer
{
    internal sealed record LrcWord(long AtMs, string Text, long EndMs = -1);
    internal sealed record LrcLine(long AtMs, string Text, IReadOnlyList<LrcWord>? Words = null);
}
