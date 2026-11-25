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
