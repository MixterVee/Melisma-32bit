using MelismaLyricsViewer;

Console.WriteLine("Live catalog/NetEase tests — no lyrics text logged");
const string song = "Blinding Lights";
const string artist = "The Weeknd";
using var coverCancel = new CancellationTokenSource(TimeSpan.FromSeconds(12));
try
{
    var image = await OnlineArtworkProvider.FindAsync(song, artist, "", 201700,
        coverCancel.Token);
    Console.WriteLine("iTunes Blinding Lights image bytes: " +
        (image?.Length.ToString() ?? "NONE"));
}
catch (Exception ex) { Console.WriteLine("iTunes error: " + ex.GetType().Name); }

using var net = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
net.DefaultRequestHeaders.UserAgent.ParseAdd("MelismaWindows/0.6.5");
using var netCancel = new CancellationTokenSource(TimeSpan.FromSeconds(9));
try
{
    var lines = await NetEaseWordProvider.FindAsync(net, song, artist,
        201700, netCancel.Token);
    Console.WriteLine("NetEase Blinding Lights: " +
        (lines == null ? "NONE or no public word timing" :
         "lines=" + lines.Count + " firstMs=" + lines[0].AtMs));
}
catch (Exception ex) { Console.WriteLine("NetEase error: " + ex.GetType().Name); }

namespace MelismaLyricsViewer
{
    internal sealed record LrcWord(long AtMs, string Text, long EndMs = -1);
    internal sealed record LrcLine(long AtMs, string Text, IReadOnlyList<LrcWord>? Words = null);
}
