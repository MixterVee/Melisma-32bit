using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Media.Control;
using Windows.Storage.Streams;
using System.Drawing.Imaging;

namespace MelismaLyricsViewer;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LyricsController());
    }
}

internal sealed record LrcWord(long AtMs, string Text);
internal sealed record LrcLine(long AtMs, string Text, IReadOnlyList<LrcWord>? Words = null);
internal sealed record LyricFrame(
    IReadOnlyList<LrcLine> Lines, int ActiveIndex,
    string Song, string Artist, string Message, long LyricPositionMs);

internal sealed class LyricsController : Form
{
    private readonly NativeTimelineReader _bridge = new();
    private readonly SongOffsetStore _offsets = new();
    private readonly AlbumArtworkState _artwork = new();
    private readonly System.Windows.Forms.Timer _screenTimer = new() { Interval = 55 };
    private readonly System.Windows.Forms.Timer _mediaTimer = new() { Interval = 1200 };
    private readonly Label _trackLabel = Label(18, Color.White, true);
    private readonly Label _statusLabel = Label(10, Color.FromArgb(126, 225, 223));
    private readonly Label _infoLabel = Label(10, Color.FromArgb(170, 181, 203));
    private readonly Button _secondScreenButton = Button("Show on second monitor", 190);
    private readonly Button _minusButton = Button("−100 ms", 108);
    private readonly Button _plusButton = Button("+100 ms", 108);
    private readonly Button _resetButton = Button("Reset", 84);
    private readonly LyricsCanvas _preview;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private KaraokeDisplay? _projection;
    private bool _mediaBusy;
    private readonly List<LrcLine> _lines = new();
    private string _song = "", _artist = "", _album = "";
    private string _source = "No lyrics loaded";
    private string _notice = "Start playing an MP3 in Foobar2000.";
    private long _positionMs;
    private long _durationMs;
    private int _offsetMs;
    private bool _offsetSaved = true;
    private int _playerState;
    private bool _bridgeFound;
    private string _loadedFor = "";
    private CancellationTokenSource? _searchCancellation;
    private int _artworkRequestId;
    private static readonly HttpClient Client = CreateHttpClient();

    public LyricsController()
    {
        Text = "Melisma Universal · Windows Lyrics Preview v0.4.2";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(825, 590);
        Size = new Size(1050, 780);
        BackColor = Color.FromArgb(11, 15, 28);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(18, 14, 18, 13),
            BackColor = BackColor
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        Controls.Add(grid);

        var headings = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        headings.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        headings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _trackLabel.Text = "Melisma / Windows Karaoke Preview";
        _trackLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "Read-only Foobar2000 bridge · Line-synchronized lyrics";
        _statusLabel.Dock = DockStyle.Fill;
        headings.Controls.Add(_trackLabel, 0, 0);
        headings.Controls.Add(_statusLabel, 0, 1);
        grid.Controls.Add(headings, 0, 0);

        var hint = Label(10, Color.FromArgb(175, 188, 204));
        hint.Dock = DockStyle.Fill;
        hint.Text = _notice;
        grid.Controls.Add(hint, 0, 1);

        var toolbarArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        toolbarArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        toolbarArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = true
        };
        var online = Button("Find synced lyrics", 147);
        online.Click += (_, _) => _ = LoadLyricsAsync(true);
        toolbar.Controls.Add(online);

        var open = Button("Open .lrc", 105);
        open.Click += OpenLrc;
        toolbar.Controls.Add(open);

        _minusButton.Click += (_, _) => ChangeOffset(-100);
        _plusButton.Click += (_, _) => ChangeOffset(100);
        _resetButton.Click += (_, _) => ChangeOffset(-_offsetMs);
        toolbar.Controls.AddRange([_minusButton, _plusButton, _resetButton]);

        _secondScreenButton.Click += ToggleSecondScreen;
        toolbar.Controls.Add(_secondScreenButton);
        toolbarArea.Controls.Add(toolbar, 0, 0);

        var artControls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        var artEnabled = new CheckBox
        {
            Text = "Album art background", AutoSize = true,
            Checked = _artwork.Enabled, ForeColor = Color.White,
            Margin = new Padding(0, 11, 12, 0)
        };
        artEnabled.CheckedChanged += (_, _) =>
        {
            _artwork.Enabled = artEnabled.Checked;
            _artwork.SaveOptions();
            _preview.Invalidate();
            _projection?.Redraw();
        };
        artControls.Controls.Add(artEnabled);

        var fadeLabel = Label(9.5f, Color.FromArgb(175, 188, 204));
        fadeLabel.Text = "Fade (seconds):";
        fadeLabel.AutoSize = true;
        fadeLabel.Margin = new Padding(0, 11, 7, 0);
        artControls.Controls.Add(fadeLabel);
        var fadeSeconds = new NumericUpDown
        {
            Minimum = 1, Maximum = 10, Value = _artwork.FadeSeconds,
            Width = 52, Height = 32, Margin = new Padding(0, 5, 12, 0),
            Font = new Font("Segoe UI", 10)
        };
        fadeSeconds.ValueChanged += (_, _) =>
        {
            _artwork.FadeSeconds = (int)fadeSeconds.Value;
            _artwork.SaveOptions();
        };
        artControls.Controls.Add(fadeSeconds);

        var openCover = Button("Open cover image", 152);
        openCover.Click += OpenCoverImage;
        artControls.Controls.Add(openCover);
        toolbarArea.Controls.Add(artControls, 0, 1);
        grid.Controls.Add(toolbarArea, 0, 2);

        _preview = new LyricsCanvas(CurrentFrame, _artwork, controllerPreview: true) { Dock = DockStyle.Fill };
        grid.Controls.Add(_preview, 0, 3);

        // Keep the footer quiet: it only changes when the selected song or
        // the user's timing adjustment changes, never on the 55 ms paint tick.
        _infoLabel.Dock = DockStyle.Fill;
        _infoLabel.Text = "Lyrics delay: 0 ms";
        grid.Controls.Add(_infoLabel, 0, 4);

        _screenTimer.Tick += (_, _) =>
        {
            ReadBridge();
            _preview.Invalidate();
            _projection?.Redraw();
            // Avoid invalidating labels with identical text every frame.
            if (hint.Text != _notice) hint.Text = _notice;
        };
        _mediaTimer.Tick += async (_, _) => await PollMediaAsync();
        Shown += async (_, _) =>
        {
            _screenTimer.Start();
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mediaTimer.Start();
                await PollMediaAsync();
            }
            catch (Exception ex) { _notice = "Windows media access failed: " + ex.Message; }
        };
        FormClosed += (_, _) =>
        {
            _screenTimer.Stop();
            _mediaTimer.Stop();
            _bridge.Dispose();
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _projection?.Close();
            _artwork.Dispose();
        };
    }

    private static Label Label(float pts, Color color, bool strong = false) => new()
    {
        Font = new Font("Segoe UI", pts, strong ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color, BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    private static Button Button(string text, int width) => new()
    {
        Text = text, Width = width, Height = 39,
        FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        BackColor = Color.FromArgb(41, 60, 87), ForeColor = Color.White,
        Font = new Font("Segoe UI", 9.5f),
        Margin = new Padding(0, 2, 8, 2)
    };
    private static string Clock(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }
    private string StateText() => _playerState switch
    {
        1 => "Playing", 2 => "Paused", _ => "Stopped"
    };

    private void ReadBridge()
    {
        if (!_bridge.TryRead(out var now))
        {
            _bridgeFound = false;
            return;
        }
        // The Foobar bridge publishes a direct sample ~every 100ms.
        // Extrapolate only between samples; paused time is held constant.
        _bridgeFound = true;
        _playerState = now.State;
        _durationMs = now.DurationMs > 0 ? (long)now.DurationMs : 0;
        long age = Math.Clamp(Environment.TickCount64 - now.TickMs, 0, 350);
        _positionMs = (long)now.PositionMs + (now.State == 1 ? age : 0);
        if (_durationMs > 0) _positionMs = Math.Min(_positionMs, _durationMs);
    }

    private async Task PollMediaAsync()
    {
        if (_mediaBusy || _manager == null || IsDisposed) return;
        _mediaBusy = true;
        try
        {
            var session = _manager.GetSessions().FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains("foobar", StringComparison.OrdinalIgnoreCase));
            if (session == null)
            {
                _notice = "Waiting for Foobar2000. Start playing a song.";
                return;
            }
            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null) return;
            var title = (props.Title ?? "").Trim();
            var artist = (props.Artist ?? "").Trim();
            if (title.Length == 0) return;
            var album = (props.AlbumTitle ?? "").Trim();
            if (title == _song && artist == _artist && album == _album) return;
            _song = title; _artist = artist; _album = album;
            _loadedFor = title + "|" + artist + "|" + album;
            // Invalidate any delayed artwork read from the preceding song.
            int artworkRequest = ++_artworkRequestId;
            _artwork.Replace(null);
            _ = LoadArtworkAsync(props.Thumbnail, _loadedFor, artworkRequest);
            _trackLabel.Text = title + "  —  " + artist;
            _offsetMs = _offsets.Read(title, artist, album);
            _offsetSaved = true;
            UpdateDelayLabel();
            _lines.Clear();
            _source = "Looking up time-synced lyrics";
            _notice = "Fetching synced lyrics for " + title + "…";
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
            _searchCancellation = null;
            _ = LoadLyricsAsync(false);
        }
        catch (Exception ex) { _notice = "Media metadata: " + ex.Message; }
        finally { _mediaBusy = false; }
    }

    private async Task LoadArtworkAsync(IRandomAccessStreamReference? thumbnail,
        string trackKey, int requestId)
    {
        if (thumbnail == null) return;
        try
        {
            using var random = await thumbnail.OpenReadAsync();
            if (random.Size is 0 or > 8388608) return;
            using var reader = new DataReader(random);
            uint byteCount = (uint)random.Size;
            uint loaded = await reader.LoadAsync(byteCount);
            if (loaded != byteCount) return;
            var imageBytes = new byte[byteCount];
            reader.ReadBytes(imageBytes);
            if (IsDisposed || requestId != _artworkRequestId ||
                trackKey != _loadedFor) return;
            _artwork.Replace(imageBytes);
            _preview.Invalidate();
            _projection?.Redraw();
        }
        catch (Exception)
        {
            // Some playback sessions expose metadata but no usable art.
            // Keep the dark gradient and working lyric timeline.
        }
    }

    private void OpenCoverImage(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Cover images (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files (*.*)|*.*",
            Title = "Choose a cover image for the current display"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imageBytes = File.ReadAllBytes(dialog.FileName);
            if (imageBytes.Length > 8388608)
                throw new InvalidOperationException("Maximum cover size is 8 MB.");
            ++_artworkRequestId;
            _artwork.Replace(imageBytes);
            _preview.Invalidate();
            _projection?.Redraw();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Cannot open cover image: " + ex.Message,
                "Melisma", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ChangeOffset(int delta)
    {
        _offsetMs = Math.Clamp(_offsetMs + delta, -20000, 20000);
        if (_song.Length > 0)
        {
            _offsetSaved = _offsets.TryWrite(_song, _artist, _album, _offsetMs);
            if (!_offsetSaved)
                _notice = "Timing changed, but Windows could not save it to your profile.";
        }
        UpdateDelayLabel();
        _preview.Invalidate();
        _projection?.Redraw();
    }

    private void UpdateDelayLabel()
    {
        string text = $"Lyrics delay: {_offsetMs:+0;-0;0} ms" +
            (_song.Length > 0
                ? $" ({(_offsetSaved ? "saved for this song" : "not saved")})"
                : "");
        if (_infoLabel.Text != text) _infoLabel.Text = text;
    }

    private LyricFrame CurrentFrame()
    {
        int current = -1;
        long lyricMs = _positionMs - _offsetMs;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].AtMs > lyricMs) break;
            current = i;
        }
        return new LyricFrame(_lines, current,
            _song.Length > 0 ? _song : "Melisma Karaoke",
            _artist, _notice, lyricMs);
    }

    private void ToggleSecondScreen(object? sender, EventArgs e)
    {
        if (_projection != null)
        {
            _projection.Close();
            return;
        }
        var screen = Screen.AllScreens.FirstOrDefault(s => !s.Primary);
        if (screen == null)
        {
            MessageBox.Show(this, "No second monitor detected. Connect your TV with HDMI and choose Win + P → Extend.",
                "Melisma — second display", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _projection = new KaraokeDisplay(screen, CurrentFrame, _artwork);
        _projection.FormClosed += (_, _) =>
        {
            _projection = null;
            if (!IsDisposed) _secondScreenButton.Text = "Show on second monitor";
        };
        _projection.Show(this);
        _secondScreenButton.Text = "Close second monitor";
    }

    private void OpenLrc(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Synchronized lyrics (*.lrc)|*.lrc|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Open synchronized .lrc lyrics for the current song"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var lines = LrcParser.Parse(File.ReadAllText(dialog.FileName));
            if (lines.Count == 0)
            {
                MessageBox.Show(this, "No valid [mm:ss.xx] timed lyric lines were found in this file.",
                    "Melisma", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _searchCancellation?.Cancel();
            _lines.Clear();
            _lines.AddRange(lines);
            _source = "Local LRC · " + Path.GetFileName(dialog.FileName);
            _notice = $"{lines.Count} synchronized lines loaded. Position follows Foobar2000.";
        }
        catch (Exception ex) { _notice = "Cannot open LRC: " + ex.Message; }
    }

    private async Task LoadLyricsAsync(bool requested)
    {
        if (_song.Length == 0 || _artist.Length == 0)
        {
            _notice = "Play a tagged MP3 in Foobar2000 first (title and artist required).";
            return;
        }
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var token = cancellation.Token;
        var searchFor = _loadedFor;
        var title = _song;
        var artist = _artist;
        var album = _album;
        var duration = _durationMs;
        _source = "LRCLIB lookup";
        _notice = requested ? "Searching LRCLIB again…" : "Searching LRCLIB for timestamped lyrics…";
        try
        {
            // The API recommends sequential requests and short delays to respect limits.
            await Task.Delay(350, token);
            string? synced = await Lrclib.FindAsync(title, artist, album, duration, token);
            if (token.IsCancellationRequested || searchFor != _loadedFor || IsDisposed) return;
            var lines = synced == null ? new List<LrcLine>() : LrcParser.Parse(synced);
            if (lines.Count == 0)
            {
                _source = "No synchronized lyrics found";
                _notice = "No timed lyrics found. Try Open .lrc or choose another song.";
            }
            else
            {
                _lines.Clear();
                _lines.AddRange(lines);
                _source = "LRCLIB · synchronized LRC";
                _notice = $"{lines.Count} synchronized lyric lines matched. Pause or seek in Foobar2000 to test.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                _source = "Lookup unavailable";
                _notice = "Lyrics lookup: " + ex.Message + " · Try Open .lrc";
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MelismaWindowsLyricsPreview/0.4.2");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static class Lrclib
    {
        private static string Arg(string value) => Uri.EscapeDataString(value);
        private static string? Field(JsonElement item, string name) =>
            item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;

        public static async Task<string?> FindAsync(
            string title, string artist, string album, long durationMs, CancellationToken token)
        {
            var url = "https://lrclib.net/api/get?track_name=" + Arg(title) +
                "&artist_name=" + Arg(artist);
            if (!string.IsNullOrWhiteSpace(album)) url += "&album_name=" + Arg(album);
            if (durationMs > 0) url += "&duration=" +
                Math.Round(durationMs / 1000.0).ToString(CultureInfo.InvariantCulture);

            using (var response = await Client.GetAsync(url, token))
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new InvalidOperationException("LRCLIB rate limited the request. Try again later.");
                if (response.IsSuccessStatusCode)
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    var direct = Field(json.RootElement, "syncedLyrics");
                    if (!string.IsNullOrWhiteSpace(direct)) return direct;
                }
                else if (response.StatusCode != HttpStatusCode.NotFound)
                    response.EnsureSuccessStatusCode();
            }
            await Task.Delay(350, token);
            // Best-effort search can find a nearby candidate with synchronized
            // lines when album or metadata differ. Favor close durations.
            url = "https://lrclib.net/api/search?track_name=" + Arg(title) +
                "&artist_name=" + Arg(artist);
            using var search = await Client.GetAsync(url, token);
            if (search.StatusCode == HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException("LRCLIB rate limited the request. Try again later.");
            search.EnsureSuccessStatusCode();
            using var results = JsonDocument.Parse(await search.Content.ReadAsStringAsync(token));
            if (results.RootElement.ValueKind != JsonValueKind.Array) return null;

            string? best = null;
            double bestScore = double.MaxValue;
            foreach (var row in results.RootElement.EnumerateArray())
            {
                string? synced = Field(row, "syncedLyrics");
                if (string.IsNullOrWhiteSpace(synced)) continue;
                string titleMatch = Field(row, "trackName") ?? "";
                string artistMatch = Field(row, "artistName") ?? "";
                if (!titleMatch.Equals(title, StringComparison.OrdinalIgnoreCase) ||
                    !artistMatch.Equals(artist, StringComparison.OrdinalIgnoreCase)) continue;
                double reported = row.TryGetProperty("duration", out var d) &&
                    d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
                double delta = durationMs <= 0 ? 0 : Math.Abs(reported * 1000.0 - durationMs);
                if (durationMs > 0 && delta > 3000) continue;
                if (delta < bestScore)
                {
                    best = synced;
                    bestScore = delta;
                }
            }
            return best;
        }
    }
}

internal sealed class SongOffsetStore
{
    // Timing is local to this Windows profile; no login or Firebase needed.
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MelismaUniversal", "WindowsLyricsViewer", "timing-offsets.json");
    private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal);

    public SongOffsetStore()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_path));
            if (saved == null) return;
            foreach (var entry in saved)
                _values[entry.Key] = Math.Clamp(entry.Value, -20000, 20000);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or inaccessible settings file must not prevent playback.
        }
    }

    private static string Key(string title, string artist, string album) =>
        string.Join("\u001f", title.Trim().ToUpperInvariant(),
            artist.Trim().ToUpperInvariant(), album.Trim().ToUpperInvariant());

    public int Read(string title, string artist, string album) =>
        _values.TryGetValue(Key(title, artist, album), out var value) ? value : 0;

    public bool TryWrite(string title, string artist, string album, int value)
    {
        var key = Key(title, artist, album);
        if (value == 0) _values.Remove(key);
        else _values[key] = Math.Clamp(value, -20000, 20000);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_values));
            File.Move(temp, _path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal static class LrcParser
{
    private static readonly Regex Timestamp =
        new(@"\[(?<m>\d{1,3}):(?<s>\d{1,2})(?:\.(?<f>\d{1,3}))?\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // Enhanced LRC permits optional word timestamps, e.g. <00:12.34>Hello <00:12.78>world.
    private static readonly Regex WordTimestamp =
        new(@"<(?<m>\d{1,3}):(?<s>\d{1,2})(?:\.(?<f>\d{1,3}))?>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static long Ms(Match match)
    {
        int minute = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        int second = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        var frac = match.Groups["f"].Value;
        int ms = frac.Length == 0 ? 0 :
            int.Parse(frac.PadRight(3, '0'), CultureInfo.InvariantCulture);
        return (minute * 60L + second) * 1000 + ms;
    }

    public static List<LrcLine> Parse(string value)
    {
        var entries = new List<LrcLine>();
        foreach (var line in value.Replace("\r", "").Split('\n'))
        {
            var matches = Timestamp.Matches(line);
            if (matches.Count == 0) continue;

            var rawText = Timestamp.Replace(line, "").Trim();
            var wordMatches = WordTimestamp.Matches(rawText);
            string text = WordTimestamp.Replace(rawText, "").Trim();
            if (text.Length == 0) text = "♪";
            List<LrcWord>? words = null;

            if (wordMatches.Count > 0)
            {
                var parsedWords = new List<LrcWord>();
                for (int i = 0; i < wordMatches.Count; i++)
                {
                    var match = wordMatches[i];
                    int start = match.Index + match.Length;
                    int finish = i + 1 < wordMatches.Count
                        ? wordMatches[i + 1].Index : rawText.Length;
                    var part = rawText.Substring(start, finish - start);
                    if (i == 0) part = part.TrimStart();
                    if (i == wordMatches.Count - 1) part = part.TrimEnd();
                    if (part.Length > 0)
                        parsedWords.Add(new LrcWord(Ms(match), part));
                }
                // If leading un-timed content exists, retain plain line mode.
                if (parsedWords.Count > 0 &&
                    string.Concat(parsedWords.Select(w => w.Text)) == text)
                    words = parsedWords;
            }

            foreach (Match m in matches)
                entries.Add(new LrcLine(Ms(m), text, words));
        }
        entries.Sort((a, b) => a.AtMs.CompareTo(b.AtMs));
        return entries;
    }
}

internal sealed record AlbumArtOptions(bool Enabled, int FadeSeconds);

internal sealed class AlbumArtworkState : IDisposable
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MelismaUniversal", "WindowsLyricsViewer", "album-art.json");

    private Bitmap? _previous;
    private Bitmap? _current;
    private long _transitionTick = Environment.TickCount64;
    public bool Enabled { get; set; } = true;
    public int FadeSeconds { get; set; } = 4;
    public Image? Previous => _previous;
    public Image? Current => _current;
    public float Progress => Math.Clamp(
        (Environment.TickCount64 - _transitionTick) / (FadeSeconds * 1000f),
        0f, 1f);

    public AlbumArtworkState()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var saved = JsonSerializer.Deserialize<AlbumArtOptions>(File.ReadAllText(_path));
            if (saved == null) return;
            Enabled = saved.Enabled;
            FadeSeconds = Math.Clamp(saved.FadeSeconds, 1, 10);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Optional display settings should never prevent lyric playback.
        }
    }

    public void SaveOptions()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(
                new AlbumArtOptions(Enabled, FadeSeconds)));
            File.Move(temp, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings can be adjusted for this session even on read-only profiles.
        }
    }

    public void Replace(byte[]? imageBytes)
    {
        Bitmap? next = null;
        if (imageBytes is { Length: > 0 })
        {
            using var memory = new MemoryStream(imageBytes);
            using var decoded = Image.FromStream(memory, true, true);
            // Detach bitmap from stream and keep only decoded pixels.
            next = new Bitmap(decoded);
        }

        _previous?.Dispose();
        _previous = _current;
        _current = next;
        _transitionTick = Environment.TickCount64;
    }

    public void Dispose()
    {
        _previous?.Dispose();
        _current?.Dispose();
        _previous = null;
        _current = null;
    }
}

internal sealed class LyricsCanvas : Control
{
    private readonly Func<LyricFrame> _frame;
    private readonly AlbumArtworkState _artwork;
    private readonly bool _controllerPreview;
    private static readonly Color Highlight = Color.FromArgb(113, 236, 227);
    private static readonly Color Other = Color.FromArgb(142, 157, 181);
    private const float SidePadding = 26f;
    private int _lastActive = int.MinValue;
    private int _transitionFrom = -1;
    private long _transitionTick;
    // Layout belongs to the song and viewport, never to the highlighted
    // line. Pre-layout all lines at full-size before they scroll into view.
    private readonly List<TextLayout> _planned = new();
    private readonly List<string> _plannedTexts = new();
    private float[] _lineCenters = [];
    private float _plannedWidth = -1f, _plannedPoints = -1f, _plannedDpi = -1f;
    private string _plannedSong = "", _plannedArtist = "";

    // A visual row remembers its original character offset so enhanced-LRC
    // word highlighting still follows the right words after a line wraps.
    private sealed record VisualRow(string Text, int Start);
    private sealed class TextLayout : IDisposable
    {
        public required Font Font { get; init; }
        public required List<VisualRow> Rows { get; init; }
        public required float RowHeight { get; init; }
        public float TotalHeight => Rows.Count * RowHeight;
        public void Dispose() => Font.Dispose();
    }

    public LyricsCanvas(Func<LyricFrame> frame, AlbumArtworkState artwork,
        bool controllerPreview = false)
    {
        _frame = frame;
        _artwork = artwork;
        _controllerPreview = controllerPreview;
        // Paint background, art and lyrics together in a single buffered
        // frame. Separate background erases can cause a brief visible flash,
        // especially in the smaller controller preview.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(17, 23, 39);
        Dock = DockStyle.Fill;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // OnPaint always fills the entire canvas with the gradient and optional
        // cover art, so a separate erase would only make flicker more likely.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var background = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(14, 32, 47), Color.FromArgb(37, 21, 55),
            LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(background, ClientRectangle);
        DrawArtworkBackground(g);
        var frame = _frame();

        if (frame.Lines.Count == 0)
        {
            ClearPlan();
            _lastActive = int.MinValue;
            _transitionFrom = -1;
            DrawMessage(g, "♪", Height * 0.33f, 76, Highlight, true);
            DrawMessage(g, "Waiting for synchronized lyrics", Height * 0.55f, 22, Color.White, true);
            DrawMessage(g, frame.Message, Height * 0.67f, 12, Other, false);
            return;
        }

        EnsurePlan(g, frame);
        int active = frame.ActiveIndex;
        if (active != _lastActive)
        {
            _transitionFrom = _lastActive;
            _lastActive = active;
            _transitionTick = Environment.TickCount64;
        }

        int center = active < 0 ? 0 : active;
        center = Math.Clamp(center, 0, _planned.Count - 1);
        bool animate = active >= 0 && _transitionFrom >= 0 &&
                       Math.Abs(active - _transitionFrom) == 1;
        float progress = animate
            ? Math.Clamp((Environment.TickCount64 - _transitionTick) / 340f, 0f, 1f)
            : 1f;
        float eased = progress * progress * (3f - 2f * progress);

        // All lines have a stable, precomputed vertical position. Interpolate
        // the viewport between those positions; never recalculate positions
        // when a line becomes highlighted or its rendered font grows.
        float anchor = _lineCenters[center];
        if (animate)
            anchor = _lineCenters[_transitionFrom] +
                (_lineCenters[center] - _lineCenters[_transitionFrom]) * eased;

        for (int index = 0; index < _planned.Count; index++)
        {
            var layout = _planned[index];
            float y = Height * 0.50f + _lineCenters[index] - anchor;
            if (y + layout.TotalHeight / 2f < 0 ||
                y - layout.TotalHeight / 2f > Height) continue;

            // The controller has a much shorter lyric viewport than the TV.
            // Make partially exposed top/bottom lines ease into visibility
            // rather than flash as glyphs enter/leave the clipped edge.
            float edgeOpacity = _controllerPreview
                ? PreviewEdgeOpacity(y, layout.TotalHeight) : 1f;
            if (edgeOpacity <= 0f) continue;
            if (_controllerPreview) y = (float)Math.Round(y);

            float emphasis = index == active ? eased :
                animate && index == _transitionFrom ? 1f - eased : 0f;
            var color = Blend(Other, Highlight, emphasis);
            var line = frame.Lines[index];
            int? playedCharacters = null;
            if (index == active && line.Words is { Count: > 0 } && progress >= 0.7f)
            {
                int count = 0;
                foreach (var word in line.Words)
                {
                    if (frame.LyricPositionMs < word.AtMs) break;
                    count += word.Text.Length;
                }
                playedCharacters = Math.Clamp(count, 0, line.Text.Length);
            }
            DrawLayout(g, layout, y, playedCharacters.HasValue ? Other : color,
                playedCharacters, emphasis, edgeOpacity);
        }
    }

    private float PreviewEdgeOpacity(float y, float lineHeight)
    {
        float transition = Math.Clamp(Height * 0.16f, 40f, 85f);
        float topGap = y - lineHeight * 0.5f;
        float bottomGap = Height - (y + lineHeight * 0.5f);
        // Allow a little text to show while crossing the boundary, but
        // smoothly suppress half-clipped characters near both edges.
        float top = Math.Clamp((topGap + lineHeight * 0.25f) / transition, 0f, 1f);
        float bottom = Math.Clamp((bottomGap + lineHeight * 0.25f) / transition, 0f, 1f);
        float strength = Math.Min(top, bottom);
        return strength * strength * (3f - 2f * strength);
    }

    private void DrawArtworkBackground(Graphics g)
    {
        if (!_artwork.Enabled) return;
        float fraction = _artwork.Progress;
        if (_artwork.Previous != null && fraction < 1f)
            DrawCover(g, _artwork.Previous, 0.72f * (1f - fraction));
        if (_artwork.Current != null && fraction > 0f)
            DrawCover(g, _artwork.Current, 0.72f * fraction);

        // Dark scrim keeps both the highlighted and upcoming lyrics legible,
        // regardless of bright or busy cover art.
        if (_artwork.Previous != null || _artwork.Current != null)
        {
            using var scrim = new SolidBrush(Color.FromArgb(143, 8, 12, 25));
            g.FillRectangle(scrim, ClientRectangle);
        }
    }

    private void DrawCover(Graphics g, Image image, float opacity)
    {
        if (Width <= 0 || Height <= 0 || opacity <= 0f) return;
        float sourceRatio = image.Width / (float)image.Height;
        float viewRatio = Width / (float)Height;
        float cropWidth = image.Width;
        float cropHeight = image.Height;
        if (sourceRatio > viewRatio) cropWidth = image.Height * viewRatio;
        else cropHeight = image.Width / viewRatio;

        float cropX = (image.Width - cropWidth) * 0.5f;
        float cropY = (image.Height - cropHeight) * 0.5f;
        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = Math.Clamp(opacity, 0f, 1f) };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.DrawImage(image, ClientRectangle,
            cropX, cropY, cropWidth, cropHeight, GraphicsUnit.Pixel, attributes);
    }

    private void ClearPlan()
    {
        foreach (var layout in _planned) layout.Dispose();
        _planned.Clear();
        _plannedTexts.Clear();
        _lineCenters = [];
        _plannedWidth = -1;
        _plannedPoints = -1;
        _plannedDpi = -1;
        _plannedSong = "";
        _plannedArtist = "";
    }

    private void EnsurePlan(Graphics g, LyricFrame frame)
    {
        float availableWidth = Math.Max(1f, Width - 2f * SidePadding);
        float preferredPoints = Math.Clamp(Width / 31f, 20f, 43f);
        bool changed = _planned.Count != frame.Lines.Count ||
            _plannedWidth != availableWidth ||
            _plannedPoints != preferredPoints ||
            _plannedDpi != g.DpiY ||
            _plannedSong != frame.Song || _plannedArtist != frame.Artist;
        if (!changed)
        {
            for (int i = 0; i < frame.Lines.Count; i++)
            {
                if (_plannedTexts[i] == frame.Lines[i].Text) continue;
                changed = true;
                break;
            }
        }
        if (!changed) return;

        ClearPlan();
        _plannedWidth = availableWidth;
        _plannedPoints = preferredPoints;
        _plannedDpi = g.DpiY;
        _plannedSong = frame.Song;
        _plannedArtist = frame.Artist;
        _lineCenters = new float[frame.Lines.Count];

        for (int i = 0; i < frame.Lines.Count; i++)
        {
            var lyric = frame.Lines[i];
            // Use the full highlighted size AND bold weight to decide the
            // wrap now, not when the line is selected. A small safety margin
            // prevents glyph overhang at the screen boundary.
            _planned.Add(MakeLayout(g, lyric.Text, preferredPoints,
                true, availableWidth * 0.96f));
            _plannedTexts.Add(lyric.Text);
            if (i > 0)
                _lineCenters[i] = _lineCenters[i - 1] +
                    Separation(_planned[i - 1], _planned[i]);
        }
        _lastActive = int.MinValue;
        _transitionFrom = -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ClearPlan();
        base.Dispose(disposing);
    }

    private float Separation(TextLayout left, TextLayout right)
    {
        float originalSpacing = Math.Clamp(Height / 6.5f, 52f, 105f);
        float breathingRoom = Math.Clamp(Height / 55f, 10f, 21f);
        return Math.Max(originalSpacing,
            (left.TotalHeight + right.TotalHeight) / 2f + breathingRoom);
    }

    private static Color Blend(Color a, Color b, float fraction)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * fraction),
            (int)(a.G + (b.G - a.G) * fraction),
            (int)(a.B + (b.B - a.B) * fraction));
    }

    private static float TextWidth(Graphics g, string text, Font font)
    {
        if (text.Length == 0) return 0f;
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap
        };
        return g.MeasureString(text, font, 10000, format).Width;
    }

    private static List<VisualRow> Wrap(Graphics g, string text, Font font, float width)
    {
        if (TextWidth(g, text, font) <= width || text.Length < 2)
            return [new VisualRow(text, 0)];

        // Prefer a word boundary near the middle, balancing both visible rows.
        // Fall back to a character boundary only for a long unbroken token.
        var breaks = Enumerable.Range(1, text.Length - 1)
            .Where(i => char.IsWhiteSpace(text[i])).ToList();
        if (breaks.Count == 0)
            breaks = Enumerable.Range(1, text.Length - 1).ToList();

        float bestPenalty = float.PositiveInfinity;
        int bestBreak = -1, bestNextStart = -1;
        foreach (int split in breaks)
        {
            var a = text[..split].TrimEnd();
            int next = split;
            while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
            if (a.Length == 0 || next >= text.Length) continue;
            var b = text[next..];
            float wa = TextWidth(g, a, font);
            float wb = TextWidth(g, b, font);
            float max = Math.Max(wa, wb);
            // Width overflow is heavily penalized, while natural balance wins
            // among split points that both fit.
            float penalty = Math.Max(0, max - width) * 1000f + Math.Abs(wa - wb);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestBreak = a.Length;
                bestNextStart = next;
            }
        }

        if (bestBreak < 0)
            return [new VisualRow(text, 0)];

        return [new VisualRow(text[..bestBreak], 0),
                new VisualRow(text[bestNextStart..], bestNextStart)];
    }

    private static TextLayout MakeLayout(Graphics g, string text,
        float preferredSize, bool bold, float width)
    {
        float size = Math.Max(10f, preferredSize);
        while (true)
        {
            var font = new Font("Segoe UI", size,
                bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            var rows = Wrap(g, text, font, width);
            bool allFit = rows.All(row => TextWidth(g, row.Text, font) <= width);
            if (allFit || size <= 10.01f)
            {
                return new TextLayout
                {
                    Font = font,
                    Rows = rows,
                    RowHeight = font.GetHeight(g) * 1.10f
                };
            }
            font.Dispose();
            size = Math.Max(10f, size * 0.90f);
        }
    }

    private void DrawMessage(Graphics g, string text, float y,
        float points, Color color, bool bold)
    {
        using var layout = MakeLayout(g, text, points, bold,
            Math.Max(1f, Width - 2f * SidePadding));
        DrawLayout(g, layout, y, color, null, 1f);
    }

    private void DrawLayout(Graphics g, TextLayout layout,
        float centerY, Color color, int? playedCharacters, float emphasis,
        float opacity = 1f)
    {
        // The row breaks and reserved height remain fixed. Only the actual
        // font size/color animate, so upcoming lyrics are already wrapped.
        float scale = 0.67f + 0.33f * Math.Clamp(emphasis, 0f, 1f);
        using var displayFont = new Font("Segoe UI",
            layout.Font.SizeInPoints * scale,
            emphasis >= 0.5f ? FontStyle.Bold : FontStyle.Regular,
            GraphicsUnit.Point);
        opacity = Math.Clamp(opacity, 0f, 1f);
        using var normalBrush = new SolidBrush(Color.FromArgb(
            (int)Math.Round(color.A * opacity), color));
        using var playedBrush = new SolidBrush(Color.FromArgb(
            (int)Math.Round(Highlight.A * opacity), Highlight));
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        float top = centerY - layout.TotalHeight / 2f;
        float width = Math.Max(1f, Width - 2f * SidePadding);
        for (int i = 0; i < layout.Rows.Count; i++)
        {
            var row = layout.Rows[i];
            var rect = new RectangleF(SidePadding, top + i * layout.RowHeight,
                width, layout.RowHeight);
            g.DrawString(row.Text, displayFont, normalBrush, rect, format);

            if (!playedCharacters.HasValue) continue;
            int playedInRow = Math.Clamp(playedCharacters.Value - row.Start,
                0, row.Text.Length);
            if (playedInRow == 0) continue;
            if (playedInRow == row.Text.Length)
            {
                g.DrawString(row.Text, displayFont, playedBrush, rect, format);
                continue;
            }

            // The clip is relative to this wrapped row, not the original LRC
            // line, so multi-row enhanced lyrics highlight correctly.
            float totalWidth = TextWidth(g, row.Text, displayFont);
            float playedWidth = TextWidth(g, row.Text[..playedInRow], displayFont);
            float left = rect.Left + (rect.Width - totalWidth) / 2f;
            var saved = g.Save();
            g.SetClip(new RectangleF(left - 1f, rect.Top,
                playedWidth + 2f, rect.Height), CombineMode.Intersect);
            g.DrawString(row.Text, displayFont, playedBrush, rect, format);
            g.Restore(saved);
        }
    }
}

internal sealed class KaraokeDisplay : Form
{
    private readonly LyricsCanvas _canvas;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 55 };

    public KaraokeDisplay(Screen screen, Func<LyricFrame> frame, AlbumArtworkState artwork)
    {
        Text = "Melisma Karaoke · TV display";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        BackColor = Color.Black;
        KeyPreview = true;
        _canvas = new LyricsCanvas(frame, artwork) { Dock = DockStyle.Fill };
        Controls.Add(_canvas);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        _timer.Tick += (_, _) => _canvas.Invalidate();
        Shown += (_, _) => _timer.Start();
        FormClosed += (_, _) => { _timer.Stop(); _timer.Dispose(); };
    }
    public void Redraw() { if (!IsDisposed) _canvas.Invalidate(); }
}

internal readonly record struct DirectTimeline(
    int State, double PositionMs, double DurationMs, long TickMs, ulong Generation);

internal sealed class NativeTimelineReader : IDisposable
{
    private const string Name = @"Local\MelismaFoobarBridge_v1";
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;

    public bool TryRead(out DirectTimeline sample)
    {
        sample = default;
        try
        {
            if (_view == null)
            {
                _mapping = MemoryMappedFile.OpenExisting(Name, MemoryMappedFileRights.Read);
                _view = _mapping.CreateViewAccessor(0, 48, MemoryMappedFileAccess.Read);
            }
            if (_view.ReadUInt32(0) != 0x4D454C49 || _view.ReadUInt32(4) != 1) return false;
            for (int i = 0; i < 3; i++)
            {
                int before = _view.ReadInt32(8);
                if ((before & 1) != 0) continue;
                int state = _view.ReadInt32(12);
                long pos = _view.ReadInt64(16);
                long dur = _view.ReadInt64(24);
                long tick = _view.ReadInt64(32);
                ulong generation = _view.ReadUInt64(40);
                int after = _view.ReadInt32(8);
                if (before != after || (after & 1) != 0) continue;
                if (state is < 0 or > 2 || pos < 0 || tick <= 0) return false;
                sample = new DirectTimeline(state, pos / 1000.0, dur / 1000.0, tick, generation);
                return true;
            }
            return false;
        }
        catch (IOException) { Dispose(); return false; }
        catch (UnauthorizedAccessException) { Dispose(); return false; }
        catch (ObjectDisposedException) { Dispose(); return false; }
    }
    public void Dispose()
    {
        _view?.Dispose();
        _mapping?.Dispose();
        _view = null;
        _mapping = null;
    }
}
