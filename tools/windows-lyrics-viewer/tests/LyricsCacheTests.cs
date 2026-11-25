namespace MelismaLyricsViewer;

internal static class LyricsCacheTests
{
    internal static void Run(Action<bool, string> check)
    {
        string temp = Path.Combine(Path.GetTempPath(),
            "melisma-lyrics-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new LyricsResultCache(temp);
            var original = new List<LrcLine>
            {
                new(10000, "I am here", [
                    new LrcWord(10000, "I ", 10300),
                    new LrcWord(10300, "am ", 10600),
                    new LrcWord(10600, "here", 11000)
                ]),
                new(15000, "Another line", [
                    new LrcWord(15000, "Another ", 15600),
                    new LrcWord(15600, "line", 16000)
                ])
            };
            cache.Save("Edamame", "bbno$; Rich Brian", "Edamame",
                135000, "netease", "NetEase YRC", original);

            check(cache.TryLoad("Edamame", "bbno$; Rich Brian",
                    "Edamame", 135000, out var restored) && restored != null,
                "Previously selected lyrics instantly available from local cache");
            check(restored?.Lines.Count == 2 && restored.WordTimed,
                "Cached lyrics preserve genuine word-timing quality");
            check(restored?.Lines[0].Words?[1].AtMs == 10300 &&
                restored?.Lines[0].Words?[1].EndMs == 10600,
                "Disk cache roundtrip preserves source word onset and end");
            check(restored?.SourceId == "netease" &&
                restored.SourceName == "NetEase YRC",
                "Cached result retains source credit for diagnostics");
            check(!cache.TryLoad("Edamame", "Wrong artist", "Edamame",
                    135000, out _),
                "Cache does not reuse lyrics for another artist");
            check(!cache.TryLoad("Edamame", "bbno$; Rich Brian", "Edamame",
                    180000, out _),
                "Cache rejects metadata for a different track duration");
            check(cache.TryLoad("Edamame", "bbno$; Rich Brian",
                    "Single (different album tag)", 135000, out _),
                "Android-style cache identity tolerates differing album metadata");

            var shorterLineOnly = new LyricsSourceOrchestrator.Answer(
                "lrclib", "LRCLIB", [new LrcLine(20000, "line only")],
                "", false);
            check(restored != null &&
                !LyricsResultCache.ShouldReplace(restored, shorterLineOnly),
                "Do not downgrade cached true word timing to line-only lookup");
            var betterIntro = new LyricsSourceOrchestrator.Answer(
                "amll", "AMLL", [
                    new LrcLine(6000, "Earlier", [
                        new LrcWord(6000, "Ear"), new LrcWord(6300, "lier")
                    ]),
                    new LrcLine(13000, "Later", [
                        new LrcWord(13000, "La"), new LrcWord(13300, "ter")
                    ])
                ], "", false);
            check(restored != null &&
                LyricsResultCache.ShouldReplace(restored, betterIntro),
                "Allow an equally complete word source with earlier first verse");

            cache.Save("Edamame", "bbno$; Rich Brian", "Edamame",
                135000, "lrclib", "LRCLIB", [new LrcLine(12000, "line")]);
            check(cache.TryLoad("Edamame", "bbno$; Rich Brian",
                "Edamame", 135000, out var lineCached) && lineCached != null &&
                !lineCached.WordTimed,
                "Can cache a line-timed fallback when word timing is missing");
            check(lineCached != null &&
                LyricsResultCache.ShouldReplace(lineCached, betterIntro),
                "Real word timing upgrades cached line-only lyrics");
            cache.SavePlain("Unknown Lyrics", "", "Album A", 145000,
                "A plain lyric verse\nAnd another line");
            check(cache.TryLoad("Unknown Lyrics", "", "Album B", 145000,
                    out var plain) && plain != null &&
                plain.PlainLyrics?.Contains("another line") == true &&
                plain.Lines.Count == 0,
                "Plain fallback is cached even when artist and album tags differ");
            check(!cache.TryLoad("Unknown Lyrics", "", "Album B", 175000, out _),
                "Plain text cache still rejects a different duration");
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { }
        }
    }
}
