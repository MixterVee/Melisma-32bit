namespace MelismaLyricsViewer;

internal static class AdditionalSourceTests
{
    public static void Run(Action<bool, string> check)
    {
        string sample = """
            {"t":0,"c":[{"tx":"Lyrics by"}]}
            [10200,1900](10200,300,0)I (10500,420,0)can't (10920,330,0)sleep (11250,360,0)until
            [12400,1100](12400,500,0)sun(12900,450,0)rise
            """;
        var parsed = NeteaseYrcParser.Parse(sample);
        check(parsed.Count == 2, "NetEase YRC ignores credits, loads two timed lines");
        check(parsed[0].AtMs == 10200, "NetEase first vocal starts at genuine 10.2s");
        check(parsed[0].Text == "I can't sleep until", "NetEase English spacing preserved");
        check(parsed[0].Words is { Count: 4 }, "NetEase stores word timestamp segments");
        check(parsed[0].Words![1].AtMs == 10500 &&
            parsed[0].Words![1].EndMs == 10920,
            "NetEase stores source word onset/end without guessing");
        check(parsed[1].Text == "sunrise" &&
            parsed[1].Words is { Count: 2 },
            "NetEase adjoining syllables remain one visible word");
        check(WordTimedLyrics.HasWordTimings(parsed),
            "NetEase YRC correctly qualifies as word timing");

        // Live NetEase regression: the first 0/1000/2000ms entries in
        // Blinding Lights are producer/composer credits, while its first
        // genuine word-timed verse begins at approximately 23.55 seconds.
        string withCredits = """
            [0,500](0,200,0)制作人 : Producer
            [1000,500](1000,200,0)作词 : Writer
            [2000,500](2000,200,0)编曲 : Arranger
            [23550,1200](23550,400,0)First (23950,300,0)vocal (24250,300,0)line
            [27360,1200](27360,500,0)Next (27860,500,0)verse
            """;
        var withoutCredits = NeteaseYrcParser.Parse(withCredits);
        check(withoutCredits.Count == 2,
            "Timed NetEase metadata credits excluded from vocal lines");
        check(withoutCredits[0].AtMs == 23550 &&
            withoutCredits[0].Words is { Count: 3 },
            "First genuine verse at 23.55s keeps its exact word timing");
        check(withoutCredits[1].AtMs == 27360,
            "Later genuine NetEase word timing remains untouched");

        check(OnlineArtworkProvider.IsMatch(
            "Blinding Lights", "The Weeknd", "", 201700,
            "Blinding Lights", "The Weeknd", "After Hours", 200040),
            "Artwork fallback accepts right song/artist with matching duration");
        check(!OnlineArtworkProvider.IsMatch(
            "Blinding Lights", "The Weeknd", "", 201700,
            "Save Your Tears", "The Weeknd", "After Hours", 201700),
            "Artwork fallback rejects a different song");
        check(!OnlineArtworkProvider.IsMatch(
            "Blinding Lights", "The Weeknd", "", 201700,
            "Blinding Lights", "Taylor Swift", "1989", 201700),
            "Artwork fallback rejects wrong artist");
        check(!OnlineArtworkProvider.IsMatch(
            "Blinding Lights", "The Weeknd", "After Hours", 201700,
            "Blinding Lights", "The Weeknd", "Remixes", 201700),
            "Artwork fallback respects album when tagged");
        check(!OnlineArtworkProvider.IsMatch(
            "Blinding Lights", "The Weeknd", "", 201700,
            "Blinding Lights", "The Weeknd", "After Hours", 234000),
            "Artwork fallback rejects a distinct recording duration");
    }
}
