using System.IO.MemoryMappedFiles;

namespace MelismaLyricsViewer;

internal static class KaraokeStemBridgeTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(StemKaraokeBridge.MappingSize == 72 &&
              StemKaraokeBridge.Version == 1 &&
              StemKaraokeBridge.MappingName == @"Local\MelismaStemKaraoke_v1",
            "C# bridge matches versioned C++ native shared-memory layout");

        // Mimic the owner of an installed Foobar Stem Separator component.
        // Viewer is the client; it must NOT create the mapping itself.
        using var owner = MemoryMappedFile.CreateNew(
            StemKaraokeBridge.MappingName, StemKaraokeBridge.MappingSize);
        using var view = owner.CreateViewAccessor();
        long now = Environment.TickCount64;
        view.Write(0, StemKaraokeBridge.Magic);
        view.Write(4, StemKaraokeBridge.Version);
        view.Write(28, (int)KaraokeStemMode.Original);
        view.Write(32, 100);
        view.Write(36, 100);
        view.Write(40, 1);
        view.Write(44, 12345);
        view.Write(48, now);
        view.Write(56, now);
        view.Write(64, (long)20261127);

        using var client = new StemKaraokeBridge();
        check(client.TryRead(out var first) && first != null &&
              first.Mode == KaraokeStemMode.Original &&
              first.VocalPercent == 100 &&
              first.InstrumentalPercent == 100 &&
              first.DspActive,
            "Windows karaoke panel reads the actual running native Stem Separator mode");

        bool sent = client.Request(KaraokeStemMode.Blend, 30, 100, out int seq);
        check(sent && seq > 0 &&
              view.ReadInt32(16) == 3 &&
              view.ReadInt32(20) == 30 &&
              view.ReadInt32(24) == 100 &&
              view.ReadInt32(8) == seq,
            "Vocal blend slider sends complete requested native mode/gain payload");

        check(client.TryRead(out var pending) &&
              pending is { Pending: true, Mode: KaraokeStemMode.Original },
            "GUI never claims music changed before native Foobar acknowledges request");

        // Simulate Stem Separator's 75ms worker applying the audio change.
        view.Write(28, (int)KaraokeStemMode.Blend);
        view.Write(32, 30);
        view.Write(36, 100);
        view.Write(12, seq);
        check(client.TryRead(out var applied) &&
              applied is { Pending: false, Mode: KaraokeStemMode.Blend,
                  VocalPercent: 30, InstrumentalPercent: 100 },
            "Applied Foobar audio mode and actual blend gain are acknowledged");

        check(!client.Request((KaraokeStemMode)9, 100, 100, out _) &&
              !client.Request(KaraokeStemMode.Blend, -1, 100, out _) &&
              !client.Request(KaraokeStemMode.Blend, 30, 101, out _),
            "Invalid modes and out-of-range blend levels are rejected without audio writes");

        view.Write(28, (int)KaraokeStemMode.Instrumental);
        check(client.TryRead(out var changedExternally) &&
              changedExternally?.Mode == KaraokeStemMode.Instrumental,
            "Foobar's own context menu mode changes are visible in Melisma");

        view.Write(40, 0);
        view.Write(56, now - 10000);
        check(client.TryRead(out var disabled) && disabled is { DspActive: false },
            "Bridge exists but inactive DSP must not be reported as processing audio");

        view.Write(48, now - 10000);
        check(!client.TryRead(out _),
            "Stopping Foobar or a stale bridge cannot be mistaken for active karaoke audio");
    }
}
