using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MelismaLyricsViewer;

/// <summary>
/// A local-only Google Cast tab source. Chrome casts the live browser page
/// to an Android TV/Chromecast without exposing MP3 files, audio or passwords.
/// The stream contains rendered JPEG frames from the existing lyrics canvas,
/// preserving native Android-style lyrics, dots, fades and album artwork.
/// </summary>
internal sealed class CastTabServer : IDisposable
{
    private TcpListener? _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private byte[]? _latestFrame;
    private int _frameSequence;

    internal int Port { get; private set; }
    internal string Address => $"http://127.0.0.1:{Port}/";
    internal bool IsRunning => _listener != null;
    internal int FrameSequence => Volatile.Read(ref _frameSequence);

    internal void Start()
    {
        if (_listener != null) return;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(8);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync(_listener, _shutdown.Token);
    }

    internal void Publish(byte[] jpeg)
    {
        if (_listener == null || jpeg.Length < 4 ||
            jpeg[0] != 0xff || jpeg[1] != 0xd8 ||
            jpeg[^2] != 0xff || jpeg[^1] != 0xd9) return;
        Volatile.Write(ref _latestFrame, jpeg);
        Interlocked.Increment(ref _frameSequence);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(token);
                // No UI work occurs on these network threads. They only
                // send the most recently published, already encoded image.
                _ = ServeAsync(client, token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (token.IsCancellationRequested) { break; }
            catch (SocketException) { break; }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using var stream = client.GetStream();
                byte[] buffer = new byte[4096];
                int received = await stream.ReadAsync(buffer, token);
                if (received <= 0) return;
                string firstLine = Encoding.ASCII.GetString(buffer, 0, received)
                    .Split('\n')[0].TrimEnd('\r');
                if (!firstLine.StartsWith("GET ", StringComparison.Ordinal))
                {
                    await SendAsync(stream, 405, "Method Not Allowed",
                        "text/plain", "GET only"u8.ToArray(), token);
                    return;
                }
                string target = firstLine.Split(' ', 3)[1].Split('?')[0];
                switch (target)
                {
                    case "/":
                    case "/index.html":
                        await SendAsync(stream, 200, "OK",
                            "text/html; charset=utf-8",
                            Encoding.UTF8.GetBytes(PageHtml), token);
                        return;
                    case "/frame.jpg":
                        var frame = Volatile.Read(ref _latestFrame);
                        if (frame == null)
                            await SendAsync(stream, 503, "Service Unavailable",
                                "text/plain", "Waiting for lyrics frame"u8.ToArray(), token);
                        else
                            await SendAsync(stream, 200, "OK", "image/jpeg", frame, token);
                        return;
                    case "/stream.mjpeg":
                        await SendStreamAsync(stream, token);
                        return;
                    case "/favicon.ico":
                        await SendAsync(stream, 204, "No Content",
                            "image/x-icon", Array.Empty<byte>(), token);
                        return;
                    default:
                        await SendAsync(stream, 404, "Not Found",
                            "text/plain", "Not found"u8.ToArray(), token);
                        return;
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or
                OperationCanceledException or ObjectDisposedException)
            {
                // Chrome may cancel/recreate a stream during tab casting.
            }
        }
    }

    private static async Task SendAsync(NetworkStream stream, int code,
        string reason, string contentType, byte[] bytes, CancellationToken token)
    {
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {code} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(header, token);
        if (bytes.Length > 0) await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private async Task SendStreamAsync(NetworkStream stream, CancellationToken token)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: multipart/x-mixed-replace; boundary=melisma-frame\r\n" +
            "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Connection: close\r\n\r\n"), token);

        int sentSequence = -1;
        while (!token.IsCancellationRequested)
        {
            var jpeg = Volatile.Read(ref _latestFrame);
            int sequence = Volatile.Read(ref _frameSequence);
            if (jpeg != null && sequence != sentSequence)
            {
                byte[] header = Encoding.ASCII.GetBytes(
                    "--melisma-frame\r\nContent-Type: image/jpeg\r\n" +
                    $"Content-Length: {jpeg.Length}\r\n\r\n");
                await stream.WriteAsync(header, token);
                await stream.WriteAsync(jpeg, token);
                await stream.WriteAsync("\r\n"u8.ToArray(), token);
                await stream.FlushAsync(token);
                sentSequence = sequence;
            }
            await Task.Delay(60, token);
        }
    }

    internal const string PageHtml = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<meta name="theme-color" content="#000000">
<title>Melisma Karaoke — Cast TV</title>
<style>
html, body { margin:0; width:100%; height:100%; overflow:hidden;
  background:#000; color:white; font:15px Segoe UI,Arial,sans-serif; }
#karaoke { display:block; position:absolute; inset:0; width:100%; height:100%;
  object-fit:contain; background:#000; }
#hint { position:absolute; bottom:12px; left:50%; transform:translateX(-50%);
  padding:7px 13px; border-radius:14px; background:#151a22b8;
  color:#dbe9ec; font-size:12px; pointer-events:none; opacity:0.8;
  transition:opacity 1s; white-space:nowrap; }
body.playing #hint { opacity:0; }
</style>
</head>
<body>
<img id="karaoke" src="/stream.mjpeg" alt="Melisma synced karaoke lyrics">
<div id="hint">Melisma Karaoke · Chrome menu → Cast → choose TV · Double-click for full screen</div>
<script>
document.addEventListener('dblclick', () => {
  if (!document.fullscreenElement) document.documentElement.requestFullscreen?.();
  else document.exitFullscreen?.();
});
const img = document.getElementById('karaoke');
img.onload = () => document.body.classList.add('playing');
img.onerror = () => {
  document.body.classList.remove('playing');
  setTimeout(() => { img.src = '/stream.mjpeg?t=' + Date.now(); }, 1500);
};
</script>
</body>
</html>
""";

    public void Dispose()
    {
        _shutdown.Cancel();
        _listener?.Stop();
        _listener = null;
        Volatile.Write(ref _latestFrame, null);
        _shutdown.Dispose();
    }
}
