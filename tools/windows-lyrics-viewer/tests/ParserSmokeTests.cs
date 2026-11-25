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

        Console.WriteLine($"PASS: {_passed} lyric parser checks.");
    }
}
