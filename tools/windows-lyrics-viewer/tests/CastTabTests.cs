using System.Net;
using System.Net.Http;

namespace MelismaLyricsViewer;

internal static class CastTabTests
{
    internal static void Run(Action<bool, string> check)
    {
        using var server = new CastTabServer();
        server.Start();
        check(server.Port > 0 && new Uri(server.Address).Host == "127.0.0.1" &&
            server.IsRunning,
            "Cast tab source binds ONLY to Windows loopback, never exposes music on the LAN");

        using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        var page = client.GetStringAsync(server.Address).GetAwaiter().GetResult();
        check(page.Contains("/stream.mjpeg", StringComparison.Ordinal) &&
              page.Contains("Melisma Karaoke", StringComparison.Ordinal) &&
              page.Contains("Double-click for full screen", StringComparison.Ordinal),
            "Chrome Cast tab opens fullscreen-ready live MJPEG karaoke page");
        var unavailable = client.GetAsync(server.Address + "frame.jpg")
            .GetAwaiter().GetResult();
        check(unavailable.StatusCode == HttpStatusCode.ServiceUnavailable,
            "Cast tab never claims a valid image before one is available");

        byte[] jpeg = [0xff, 0xd8, 0x01, 0x05, 0x23, 0x45, 0xff, 0xd9];
        server.Publish(jpeg);
        var response = client.GetAsync(server.Address + "frame.jpg")
            .GetAwaiter().GetResult();
        var payload = response.Content.ReadAsByteArrayAsync()
            .GetAwaiter().GetResult();
        check(response.StatusCode == HttpStatusCode.OK &&
              response.Content.Headers.ContentType?.MediaType == "image/jpeg" &&
              payload.SequenceEqual(jpeg),
            "Chrome receives the latest already-rendered frame without rereading MP3s");
        check(server.FrameSequence == 1,
            "Published image advances sequence for streaming viewers");

        server.Publish([1, 2, 3, 4]);
        check(server.FrameSequence == 1,
            "Broken JPEG frames never replace the visible cast image");

        // Verify the long-lived MJPEG response starts with actual image
        // bytes, using headers-read mode so the test does not wait for the
        // infinite streaming response to complete.
        using var streamResponse = client.GetAsync(server.Address + "stream.mjpeg",
            HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        check(streamResponse.IsSuccessStatusCode &&
              streamResponse.Content.Headers.ContentType?.MediaType ==
                  "multipart/x-mixed-replace",
            "Google Cast source streams images without background-tab JS timers");
        using var body = streamResponse.Content.ReadAsStreamAsync()
            .GetAwaiter().GetResult();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] first = new byte[256];
        int read = body.ReadAsync(first, timeout.Token).AsTask()
            .GetAwaiter().GetResult();
        string data = System.Text.Encoding.ASCII.GetString(first, 0, read);
        check(data.Contains("--melisma-frame", StringComparison.Ordinal) ||
              data.Contains("Content-Type: image/jpeg", StringComparison.Ordinal),
            "MJPEG frame boundaries reach the Chrome viewer");
    }
}
