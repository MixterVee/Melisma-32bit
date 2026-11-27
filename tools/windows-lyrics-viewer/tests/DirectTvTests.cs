using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MelismaLyricsViewer;

internal static class DirectTvTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(TvReceiverDiscovery.Probe == "MELISMA_DISCOVER_V1" &&
              TvReceiverDiscovery.Reply == "MELISMA_TV_V1|" &&
              TvReceiverDiscovery.ReceiverPort == 49831 &&
              TvReceiverDiscovery.DiscoveryPort == 47916,
            "Windows direct-cast protocol matches native Android TV receiver ports");

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var target = new TvReceiverDevice("Test Android TV", IPAddress.Loopback, port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        // Simulate an Android TV receiver and verify its 14-byte initial
        // password exchange BEFORE any lyric frame can be sent.
        var connecting = DirectTvSender.ConnectAsync(target, "123456", timeout.Token);
        using var accepted = listener.AcceptTcpClientAsync(timeout.Token)
            .GetAwaiter().GetResult();
        using var server = accepted.GetStream();
        byte[] hello = new byte[14];
        server.ReadExactly(hello);
        check(Encoding.ASCII.GetString(hello) == "MELISMA1123456",
            "Native TV receives exact fixed-size 6-digit pairing handshake");

        server.WriteByte(1); // TV approves this six-digit code.
        using var sender = connecting.GetAwaiter().GetResult();
        check(sender.IsConnected,
            "Browser-free Windows → Android TV paired connection is established");

        byte[] frame = [0xff, 0xd8, 0x14, 0x25, 0x36, 0xff, 0xd9];
        check(sender.Publish(frame), "Karaoke frame accepted by one-frame send queue");
        byte[] length = new byte[4];
        server.ReadExactly(length);
        int size = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(length);
        byte[] received = new byte[size];
        server.ReadExactly(received);
        check(size == frame.Length && received.SequenceEqual(frame),
            "Native Android TV receives entire correctly length-prefixed JPEG frame");
        check(sender.SentFrames >= 1,
            "Direct-TV stream confirms at least one karaoke frame delivered");

        check(!sender.Publish([1, 2, 3]),
            "Malformed frame is never transmitted to native TV receiver");
        sender.Dispose();

        var wrongPair = DirectTvSender.ConnectAsync(target, "000001", timeout.Token);
        using var rejected = listener.AcceptTcpClientAsync(timeout.Token)
            .GetAwaiter().GetResult();
        using var rejectionStream = rejected.GetStream();
        byte[] wrongHello = new byte[14];
        rejectionStream.ReadExactly(wrongHello);
        rejectionStream.WriteByte(0);
        bool denied = false;
        try { wrongPair.GetAwaiter().GetResult(); }
        catch (UnauthorizedAccessException) { denied = true; }
        check(denied, "Incorrect TV pairing code rejects stream connection");

        check(!string.IsNullOrWhiteSpace(target.ToString()) &&
            target.ToString().Contains("Test Android TV", StringComparison.Ordinal),
            "Device picker displays actual receiver name and address");
    }
}
