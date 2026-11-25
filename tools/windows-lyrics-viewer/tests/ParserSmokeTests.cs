using System.Xml;

namespace MelismaLyricsViewer;

// Match the records in the desktop app, using the real TTML parser source.
internal sealed record LrcWord(long AtMs, string Text, long EndMs = -1);
internal sealed record LrcLine(long AtMs, string Text, IReadOnlyList<LrcWord>? Words = null);

internal static class ParserSmokeTests
{
    private static int _passed;

    private static void Check(bool condition, string explanation)
    {
        if (!condition) throw new InvalidOperationException("Parser test failed: " + explanation);
        _passed++;
    }

    public static void Main()
    {
        string ttml = """
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata">
              <body><div>
                <p begin="00:01.000" end="00:03.000"><span begin="00:01.000" end="00:01.500">Hello</span> <span begin="00:01.600" end="00:02.000">world</span></p>
                <p begin="00:04.000" end="00:05.000">This line only has line timestamps</p>
                <p begin="00:06.000" end="00:07.000"><span begin="00:06.100" end="00:06.400">Sing</span><span begin="00:06.450" end="00:06.800">ing</span><span ttm:role="x-translation">translated words should not appear</span></p>
              </div></body>
            </tt>
            """;
        var lines = WordTimedLyrics.ParseTtml(ttml);
        Check(lines.Count == 3, "three lines returned");
        Check(lines[0].AtMs == 1000, "line 1 time");
        Check(lines[0].Text == "Hello world", "literal inter-span whitespace preserved");
        Check(lines[0].Words is { Count: 2 }, "word timestamps retained");
        Check(lines[0].Words![0].AtMs == 1000 && lines[0].Words![0].EndMs == 1500, "first word has start and end");
        Check(lines[0].Words![1].AtMs == 1600 && lines[0].Words![1].EndMs == 2000, "second word has start and end");
        Check(lines[1].Words == null, "line-only remains line-only");
        Check(lines[2].Text == "Singing", "adjacent syllable spans are concatenated");
        Check(lines[2].Words is { Count: 2 }, "adjacent syllables are correctly timed");
        Check(WordTimedLyrics.HasWordTimings(lines), "provider quality recognizes enough word timed lines");

        string onsetOnly = """
            <tt><body><div><p begin="00:00:10.000">
            <span begin="10.0s">The</span> <span begin="10.5s">song</span>
            </p></div></body></tt>
            """;
        var onset = WordTimedLyrics.ParseTtml(onsetOnly);
        Check(onset.Count == 1 && onset[0].Text == "The song", "onset-only TTML text");
        Check(onset[0].Words is { Count: 2 }, "onset-only word counts");
        Check(onset[0].Words![0].EndMs == -1, "no invented end timestamp");
        Check(onset[0].Words![1].AtMs == 10500, "seconds notation parsed");

        string nested = """
            <tt><body><div><p begin="00:00:12.500">
            <span><span begin="00:00:12.5" end="00:00:12.9">Never</span> <span begin="00:00:13.0" end="00:00:13.5">again</span></span>
            </p></div></body></tt>
            """;
        var n = WordTimedLyrics.ParseTtml(nested);
        Check(n.Count == 1 && n[0].Words is { Count: 2 }, "nested timed spans supported");
        Check(n[0].Text == "Never again", "nested spaces retained");
        Check(n[0].Words![1].AtMs == 13000, "nested word timestamp retained");

        bool blocked = false;
        try
        {
            WordTimedLyrics.ParseTtml("<!DOCTYPE tt [<!ENTITY test SYSTEM 'file:///C:/Windows/win.ini'>]><tt>&test;</tt>");
        }
        catch (XmlException) { blocked = true; }
        Check(blocked, "DTD/XML external entity input prohibited");

        // AMLL titles may append release/featured-artist variants. The
        // original v0.6 code rejected these despite a matching artist.
        using var variants = System.Text.Json.JsonDocument.Parse("""
            {"musicNames":["Blinding Lights (Remastered)","Blinding Lights"],
             "artistNames":["The Weeknd"]}
            """);
        Check(AmllWordProvider.Matches(variants.RootElement,
            "Blinding Lights", "The Weeknd"), "AMLL accepted alternate release title");
        Check(AmllWordProvider.Matches(variants.RootElement,
            "Blinding Lights (Remastered)", "The Weeknd"), "AMLL accepts remaster metadata");
        Check(!AmllWordProvider.Matches(variants.RootElement,
            "Save Your Tears", "The Weeknd"), "AMLL refuses incorrect title");
        Check(!AmllWordProvider.Matches(variants.RootElement,
            "Blinding Lights", "Taylor Swift"), "AMLL refuses incorrect artist");

        string qrc = """
            <QrcInfos><LyricInfo><Lyric_1 LyricType="1" LyricContent="[ti:Wanderlust]
            [ar:The Weeknd]
            [5767,324]Wanderlust - (5767,109)The (5876,106)Weeknd(5982,109)
            [6091,2858]Lyrics (6091,125)by：(6216,117)Somebody(6333,120)
            [292333,1910]Let (292333,136)it (292469,97)come (292566,202)to (293301,377)you(293678,565)" /></LyricInfo></QrcInfos>
            """;
        var q = QrcWordParser.Parse(qrc, "Wanderlust", "The Weeknd");
        Check(q.Count == 1, "QRC title/credit lines removed");
        Check(q[0].Text == "Let it come to you", "QRC words reconstruct the original text");
        Check(q[0].Words is { Count: 5 }, "QRC returns five word timestamps");
        Check(q[0].Words![2].AtMs == 292566 &&
            q[0].Words![2].EndMs == 292768, "QRC uses absolute start/duration");
        Check(QrcWordParser.Parse(qrc, "Wanderlust", "Other Artist").Count == 0,
            "QRC metadata rejects an incorrect artist");
        Check(BetterLyricsWordProvider.PlausibleDuration(q, 310000),
            "reasonable song length accepted");
        Check(!BetterLyricsWordProvider.PlausibleDuration(q, 180000),
            "different shorter recording rejected");

        // Android Melisma's recording-identity matching should accept release
        // tags and featured credits, but not a different artist.
        Check(AndroidLyricsMatch.Score("Blinding Lights", "The Weeknd", 200000,
            "Blinding Lights", "The Weeknd", 200000) > 0.95f,
            "Android-style exact matching accepts known recording");
        Check(AndroidLyricsMatch.Score("Blinding Lights (Remastered)", "The Weeknd", 200000,
            "Blinding Lights", "The Weeknd", 200000) > 0.90f,
            "Android-style title cleaning accepts remastered release");
        Check(AndroidLyricsMatch.Score("Blinding Lights", "The Weeknd", 200000,
            "Blinding Lights", "Taylor Swift", 200000) == 0,
            "Android-style artist contradiction veto");
        Check(AndroidLyricsMatch.Score("Shape of You", "Ed Sheeran", 234000,
            "Perfect", "Ed Sheeran", 234000) < 0.62f,
            "Android-style title mismatch rejection");

        var timedLine = new LrcLine(1000, "Hello world", [
            new LrcWord(1000, "Hello "), new LrcWord(1400, "world")
        ]);
        var longEnough = new LyricsSourceOrchestrator.Answer(
            "lrcred", "lrc.red", Enumerable.Repeat(timedLine, 20).ToList(), "", false);
        var fragment = new LyricsSourceOrchestrator.Answer(
            "amll", "AMLL", Enumerable.Repeat(timedLine, 2).ToList(), "", false);
        Check(LyricsSourceOrchestrator.EffectiveTier(longEnough, 30) == 2,
            "Android full word-timed document remains word-tier");
        Check(LyricsSourceOrchestrator.EffectiveTier(fragment, 30) == 1,
            "Android downgrades truncated word-timed fragment");

        string malformedXml = """
            <QrcInfos><LyricInfo><Lyric_1 LyricContent="[ti:Wanderlust]
            [ar:The Weeknd]
            [6000,1300]Hello (6000,700)& world(6700,600)" /></LyricInfo></QrcInfos>
            """;
        var fallbackQrc = QrcWordParser.Parse(malformedXml, "Wanderlust", "The Weeknd");
        Check(fallbackQrc.Count == 1 && fallbackQrc[0].Words is { Count: 2 },
            "Malformed QQ XML envelope falls back to safely extracted QRC");
        Check(fallbackQrc[0].Words![1].AtMs == 6700,
            "Malformed QQ QRC still preserves real word onset");
        Check(fallbackQrc[0].Text.Contains("world"),
            "Malformed QQ QRC still reconstructs lyric text");

        Check(PlaybackClockHealth.Fresh(10000, 9900),
            "Fresh Foobar bridge snapshot accepted");
        Check(PlaybackClockHealth.Fresh(10000, 8700),
            "Bridge snapshot within normal publication tolerance accepted");
        Check(!PlaybackClockHealth.Fresh(10000, 7000),
            "Stale bridge snapshot rejected instead of freezing lyrics");
        Check(!PlaybackClockHealth.Fresh(10000, 0),
            "Missing Foobar bridge timestamp rejected");
        Check(!PlaybackClockHealth.Fresh(10000, 10500),
            "Invalid future bridge timestamp rejected");

        CoverageTests.Run(Check);
        AdditionalSourceTests.Run(Check);
        WordSpacingTests.Run(Check);
        ActiveWordPulseTests.Run(Check);
        InterludeAndArtworkTests.Run(Check);
        ProviderProgressTests.RunAsync(Check).GetAwaiter().GetResult();
        LyricsCacheTests.Run(Check);
        Console.WriteLine($"PASS: {_passed} lyric parser checks.");
    }
}
