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

namespace MelismaLyricsViewer
{
    internal sealed record LrcWord(long AtMs, string Text, long EndMs = -1);
    internal sealed record LrcLine(long AtMs, string Text, IReadOnlyList<LrcWord>? Words = null);
}
