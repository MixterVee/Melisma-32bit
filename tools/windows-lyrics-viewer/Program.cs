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

// EndMs is optional because enhanced LRC often supplies word onset only.
// TTML supplies both start/end, enabling true continuous word fill.
internal sealed record LrcWord(long AtMs, string Text, long EndMs = -1);
internal sealed record LrcLine(long AtMs, string Text, IReadOnlyList<LrcWord>? Words = null);
internal sealed record LyricFrame(
    IReadOnlyList<LrcLine> Lines, int ActiveIndex,
    string Song, string Artist, string Message, long LyricPositionMs);

internal sealed class LyricsController : Form
{
    private readonly NativeTimelineReader _bridge = new();
    private readonly SongOffsetStore _offsets = new();
    private readonly AlbumArtworkState _artwork = new();
    private readonly MelismaDisplaySettings _display = MelismaDisplaySettings.Load();
    private readonly PlaybackIdleBackground _idleBackground = new();
    private MelismaDisplaySettingsForm? _settingsWindow;
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
        Text = "Melisma Universal · Windows v0.6";
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
        _statusLabel.Text = "Read-only Foobar2000 bridge · Searching lyric providers";
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

        var open = Button("Open lyrics file", 132);
        open.Click += OpenLrc;
        toolbar.Controls.Add(open);

        _minusButton.Click += (_, _) => ChangeOffset(-100);
        _plusButton.Click += (_, _) => ChangeOffset(100);
        _resetButton.Click += (_, _) => ChangeOffset(-_offsetMs);
        toolbar.Controls.AddRange([_minusButton, _plusButton, _resetButton]);

        _secondScreenButton.Click += ToggleSecondScreen;
        toolbar.Controls.Add(_secondScreenButton);
        toolbarArea.Controls.Add(toolbar, 0, 0);

        var extraControls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        var settingsButton = Button("Display settings…", 164);
        settingsButton.Click += OpenDisplaySettings;
        extraControls.Controls.Add(settingsButton);
        var openCover = Button("Open cover image", 162);
        openCover.Click += OpenCoverImage;
        extraControls.Controls.Add(openCover);
        var note = Label(9.5f, Color.FromArgb(175, 188, 204));
        note.Text = "Lyrics / Cinema · Background · Lyrics display";
        note.Width = 400;
        note.Height = 37;
        extraControls.Controls.Add(note);
        toolbarArea.Controls.Add(extraControls, 0, 1);
        grid.Controls.Add(toolbarArea, 0, 2);

        _preview = new LyricsCanvas(CurrentFrame, _artwork, _display, _idleBackground, controllerPreview: true) { Dock = DockStyle.Fill };
        grid.Controls.Add(_preview, 0, 3);

        // Keep the footer quiet: it only changes when the selected song or
        // the user's timing adjustment changes, never on the 55 ms paint tick.
        _infoLabel.Dock = DockStyle.Fill;
        _infoLabel.Text = "Lyrics delay: 0 ms";
        grid.Controls.Add(_infoLabel, 0, 4);

        _screenTimer.Tick += (_, _) =>
        {
            ReadBridge();
            _idleBackground.Update(_bridgeFound && _playerState == 1, _display);
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
            _settingsWindow?.Close();
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

    private void OpenDisplaySettings(object? sender, EventArgs e)
    {
        if (_settingsWindow != null && !_settingsWindow.IsDisposed)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new MelismaDisplaySettingsForm(_display, _artwork, () =>
        {
            _preview.Invalidate();
            _projection?.Redraw();
        });
        _settingsWindow.FormClosed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
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
        _projection = new KaraokeDisplay(screen, CurrentFrame, _artwork, _display, _idleBackground);
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
            Filter = "Timed lyric files (*.ttml;*.xml;*.lrc)|*.ttml;*.xml;*.lrc|TTML files (*.ttml;*.xml)|*.ttml;*.xml|Enhanced LRC (*.lrc)|*.lrc|All files (*.*)|*.*",
            Title = "Open genuine word-timed TTML or synchronized LRC lyrics"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            string text = File.ReadAllText(dialog.FileName);
            bool ttml = extension is ".ttml" or ".xml";
            var lines = ttml ? WordTimedLyrics.ParseTtml(text) : LrcParser.Parse(text);
            if (lines.Count == 0)
            {
                MessageBox.Show(this,
                    "No usable timed lyrics were found. Use genuine TTML or [mm:ss.xx] LRC timestamps.",
                    "Melisma", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _searchCancellation?.Cancel();
            _lines.Clear();
            _lines.AddRange(lines);
            bool wordTimed = WordTimedLyrics.HasWordTimings(lines);
            _source = "Local " + (ttml ? "TTML" : "LRC") + " · " + Path.GetFileName(dialog.FileName);
            _statusLabel.Text = wordTimed ? "Word-timed lyrics · Local file" :
                "Line-timed lyrics · Local file";
            _notice = wordTimed
                ? $"{lines.Count} lines with genuine word timestamps loaded."
                : $"{lines.Count} timestamped lines loaded. No word timing found.";
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
        _source = "Searching lyrics";
        _statusLabel.Text = "Searching genuine word timings · AMLL TTML";
        _notice = requested ? "Looking for improved word-synced lyrics…" :
            "Checking AMLL TTML for word-by-word lyrics…";
        try
        {
            // One account-free lookup; strict title/artist match. If the
            // volunteer service is unavailable or has no genuine word timing,
            // fall back to the existing reliable line-synced LRCLIB path.
            try
            {
                var wordLines = await AmllWordProvider.FindAsync(
                    Client, title, artist, token);
                if (token.IsCancellationRequested || searchFor != _loadedFor ||
                    IsDisposed) return;
                if (wordLines is { Count: > 0 })
                {
                    _lines.Clear();
                    _lines.AddRange(wordLines);
                    _source = "AMLL TTML DB · word timed";
                    _statusLabel.Text = "Word-synced · AMLL TTML DB";
                    _notice = $"{wordLines.Count} lines · real syllable/word timing. " +
                        "Pause and seek in Foobar2000 to test.";
                    return;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is HttpRequestException
                        or TaskCanceledException or JsonException
                        or System.Xml.XmlException or InvalidOperationException)
            {
                // Best effort: a single provider outage cannot stop lyrics.
            }

            _statusLabel.Text = "Line-synced fallback · LRCLIB";
            _notice = "No matching word-timed lyrics. Checking LRCLIB…";
            // The API recommends sequential requests and short delays to
            // respect rate limits.
            await Task.Delay(350, token);
            string? synced = await Lrclib.FindAsync(title, artist, album, duration, token);
            if (token.IsCancellationRequested || searchFor != _loadedFor || IsDisposed) return;
            var lines = synced == null ? new List<LrcLine>() : LrcParser.Parse(synced);
            if (lines.Count == 0)
            {
                _source = "No synchronized lyrics found";
                _statusLabel.Text = "No lyric match · Local TTML/LRC supported";
                _notice = "No timed lyrics found. Try Open lyrics file or another song.";
            }
            else
            {
                _lines.Clear();
                _lines.AddRange(lines);
                bool wordTimed = WordTimedLyrics.HasWordTimings(lines);
                _source = "LRCLIB · synchronized LRC";
                _statusLabel.Text = wordTimed ? "Word-synced · LRCLIB enhanced LRC" :
                    "Line-synced · LRCLIB (no word timestamps)";
                _notice = wordTimed ?
                    $"{lines.Count} lines with genuine word timestamps." :
                    $"{lines.Count} line-timed lyrics. Word highlight needs genuine word-timed source.";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                _source = "Lookup unavailable";
                _statusLabel.Text = "Lyrics lookup unavailable · Local files supported";
                _notice = "Lyrics lookup: " + ex.Message + " · Try Open lyrics file";
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "MelismaWindows/0.6 (Windows; https://github.com/MixterVee/Melisma-32bit)");
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

// Property initializers are important: existing v0.5 album-art.json files
// have only Enabled and FadeSeconds. CoverFadeSeconds defaults to the
// previously hard-coded 3.4s when that setting is missing.
internal sealed class AlbumArtOptions
{
    public bool Enabled { get; set; } = true;
    public float FadeSeconds { get; set; } = 4f;
    public float CoverFadeSeconds { get; set; } = 3.4f;
}

internal sealed class AlbumArtworkState : IDisposable
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MelismaUniversal", "WindowsLyricsViewer", "album-art.json");

    private Bitmap? _previous;
    private Bitmap? _current;
    private long _transitionTick = Environment.TickCount64;
    public bool Enabled { get; set; } = true;
    public float FadeSeconds { get; set; } = 4f;
    public float CoverFadeSeconds { get; set; } = 3.4f;
    public Image? Previous => _previous;
    public Image? Current => _current;
    public float Progress => ProgressFor(FadeSeconds);
    public float PanelProgress => ProgressFor(CoverFadeSeconds);
    private float ProgressFor(float seconds) => Math.Clamp(
        (Environment.TickCount64 - _transitionTick) /
            (Math.Max(1f, seconds) * 1000f), 0f, 1f);

    public AlbumArtworkState()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var saved = JsonSerializer.Deserialize<AlbumArtOptions>(File.ReadAllText(_path));
            if (saved == null) return;
            Enabled = saved.Enabled;
            FadeSeconds = float.IsFinite(saved.FadeSeconds)
                ? Math.Clamp(saved.FadeSeconds, 1f, 10f) : 4f;
            CoverFadeSeconds = float.IsFinite(saved.CoverFadeSeconds)
                ? Math.Clamp(saved.CoverFadeSeconds, 1f, 10f) : 3.4f;
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
                new AlbumArtOptions
                {
                    Enabled = Enabled,
                    FadeSeconds = FadeSeconds,
                    CoverFadeSeconds = CoverFadeSeconds
                }));
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

/// <summary>
/// Shared idle-state controller for the laptop preview and full-screen TV.
/// Brief track gaps do nothing. Long pauses/stops smoothly fade to Living,
/// and the saved background is restored when playback resumes.
/// </summary>
internal sealed class PlaybackIdleBackground
{
    private long _notPlayingSince = -1;
    private long _lastTick = -1;
    public float LivingMix { get; private set; }

    public void Update(bool isPlaying, MelismaDisplaySettings settings)
    {
        long now = Environment.TickCount64;
        if (_lastTick < 0) _lastTick = now;
        long deltaMs = Math.Clamp(now - _lastTick, 0, 250);
        _lastTick = now;

        if (isPlaying)
            _notPlayingSince = -1;
        else if (_notPlayingSince < 0)
            _notPlayingSince = now;

        bool idleLongEnough = !isPlaying && _notPlayingSince >= 0 &&
            now - _notPlayingSince >=
            Math.Clamp(settings.IdleLivingDelaySeconds, 10, 15) * 1000L;
        float target = settings.IdleLivingEnabled && idleLongEnough ? 1f : 0f;
        // One user-selected fade time applies in both directions. Underlying
        // lyrics and artwork keep updating even when fully covered, so the
        // correct live content is revealed smoothly when playback resumes.
        float transitionMs = Math.Clamp(settings.IdleLivingFadeSeconds,
            2f, 12f) * 1000f;
        float step = deltaMs / transitionMs;
        LivingMix = Math.Clamp(LivingMix +
            (target > LivingMix ? step : target < LivingMix ? -step : 0f),
            0f, 1f);
    }
}

internal sealed class LyricsCanvas : Control
{
    private readonly Func<LyricFrame> _frame;
    private readonly AlbumArtworkState _artwork;
    private readonly MelismaDisplaySettings _display;
    private readonly PlaybackIdleBackground _idleBackground;
    private readonly FluidLivingRenderer _fluidLiving = new();
    private readonly bool _controllerPreview;
    private readonly List<(Image Source, int Blur, Bitmap Reduced)> _blurCache = new();
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
    private string _plannedSong = "", _plannedArtist = "", _plannedFontFamily = "";

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
        MelismaDisplaySettings display, PlaybackIdleBackground idleBackground,
        bool controllerPreview = false)
    {
        _frame = frame;
        _artwork = artwork;
        _display = display;
        _idleBackground = idleBackground;
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

        DrawArtworkBackground(g);
        var frame = _frame();
        if (_display.Layout == MelismaViewMode.Cinema)
            DrawCinemaPanel(g, frame);
        var region = LyricsArea;
        var textClip = g.Save();
        g.SetClip(region, CombineMode.Intersect);

        if (frame.Lines.Count == 0)
        {
            ClearPlan();
            _lastActive = int.MinValue;
            _transitionFrom = -1;
            DrawMessage(g, "♪", Height * 0.33f, 76, Highlight, true);
            DrawMessage(g, "Waiting for synchronized lyrics", Height * 0.55f, 22, Color.White, true);
            DrawMessage(g, frame.Message, Height * 0.67f, 12, Other, false);
            g.Restore(textClip);
            DrawIdleTakeover(g);
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
            if (_display.MinimalMode && index < active) continue;
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
            var baseColor = index < active ? Color.FromArgb(158, 109, 133) : Other;
            var color = Blend(baseColor, Highlight, emphasis);
            var line = frame.Lines[index];
            float? playedCharacters = null;
            if (index == active && line.Words is { Count: > 0 } && progress >= 0.7f)
            {
                float count = 0f;
                foreach (var word in line.Words)
                {
                    if (frame.LyricPositionMs < word.AtMs) break;

                    // Smoothly fill ONLY when the source provides both onset
                    // and end timestamps. For onset-only enhanced LRC, snap
                    // the entire word on its genuine timestamp (no guessing).
                    if (word.EndMs > word.AtMs && _display.SmoothWordFill)
                    {
                        float wordProgress = Math.Clamp(
                            (frame.LyricPositionMs - word.AtMs) /
                            (float)(word.EndMs - word.AtMs), 0f, 1f);
                        count += word.Text.Length * wordProgress;
                        if (wordProgress < 1f) break;
                    }
                    else
                    {
                        count += word.Text.Length;
                    }
                }
                playedCharacters = Math.Clamp(count, 0f, line.Text.Length);
            }
            DrawLayout(g, layout, y, playedCharacters.HasValue ? Other : color,
                playedCharacters, emphasis, edgeOpacity);
        }
        g.Restore(textClip);
        DrawIdleTakeover(g);
    }

    // Drawn LAST, outside the lyric-column clip. This is a screensaver,
    // not a background overlay: at full opacity it hides the album panel,
    // song title, lyrics, and every other element of the TV canvas.
    // The playback clock and underlying lyric layout keep running.
    private void DrawIdleTakeover(Graphics g)
    {
        if (_idleBackground.LivingMix <= 0.001f) return;
        var art = _artwork.Enabled ? _artwork.Current ?? _artwork.Previous : null;
        _fluidLiving.Draw(g, ClientRectangle, art, _idleBackground.LivingMix);
    }

    private RectangleF LyricsArea
    {
        get
        {
            if (_display.Layout != MelismaViewMode.Cinema)
                return ClientRectangle;
            float panelWidth = Width * 0.42f;
            if (_display.ArtworkSide == ArtworkPanelSide.Left)
                return new RectangleF(panelWidth, 0, Width - panelWidth, Height);
            return new RectangleF(0, 0, Width - panelWidth, Height);
        }
    }

    private RectangleF MediaArea
    {
        get
        {
            float panelWidth = Width * 0.42f;
            return _display.ArtworkSide == ArtworkPanelSide.Left
                ? new RectangleF(0, 0, panelWidth, Height)
                : new RectangleF(Width - panelWidth, 0, panelWidth, Height);
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

    private static Color Sample(Image? image, float x, float y, Color fallback)
    {
        try
        {
            if (image is Bitmap bitmap && bitmap.Width > 0 && bitmap.Height > 0)
                return bitmap.GetPixel(
                    Math.Clamp((int)(bitmap.Width * x), 0, bitmap.Width - 1),
                    Math.Clamp((int)(bitmap.Height * y), 0, bitmap.Height - 1));
        }
        catch (Exception) { /* A missing/invalid image falls back to the palette. */ }
        return fallback;
    }

    private void DrawArtworkBackground(Graphics g)
    {
        var selected = _display.Background == MelismaBackgroundStyle.Auto
            ? MelismaBackgroundStyle.Living : _display.Background;
        DrawBackgroundStyle(g, selected, 1f);

        // The idle full-screen Living takeover is drawn *after* the Cinema
        // panel and the lyrics, not here. Changing the saved background is
        // never necessary.
    }

    private void DrawBackgroundStyle(Graphics g, MelismaBackgroundStyle style,
        float layerOpacity)
    {
        layerOpacity = Math.Clamp(layerOpacity, 0f, 1f);
        if (layerOpacity <= 0f) return;

        if (style == MelismaBackgroundStyle.Living)
        {
            var livingArt = _artwork.Enabled ? _artwork.Current ?? _artwork.Previous : null;
            _fluidLiving.Draw(g, ClientRectangle, livingArt, layerOpacity);
            // Darken the regular Living BACKGROUND for readable lyrics.
            // The idle screensaver is intentionally shown without this scrim.
            using var veil = new SolidBrush(Color.FromArgb(
                (int)(112f * layerOpacity), 8, 12, 25));
            g.FillRectangle(veil, ClientRectangle);
            return;
        }

        if (style == MelismaBackgroundStyle.Black)
        {
            if (layerOpacity >= 1f) g.Clear(Color.Black);
            else
            {
                using var black = new SolidBrush(Color.FromArgb(
                    (int)(255f * layerOpacity), Color.Black));
                g.FillRectangle(black, ClientRectangle);
            }
            return;
        }

        var art = _artwork.Enabled ? _artwork.Current : null;
        DrawPalette(g, art, style is MelismaBackgroundStyle.Living
                                or MelismaBackgroundStyle.LivingClassic,
            layerOpacity);

        if (style is MelismaBackgroundStyle.Colour
            or MelismaBackgroundStyle.LivingClassic) return;

        if (!_artwork.Enabled) return;
        var previous = _artwork.Previous;
        var current = _artwork.Current;
        float progress = _artwork.Progress;

        int blur = style == MelismaBackgroundStyle.Living
            ? 30 : _display.CoverBlur;
        float opacity = (style == MelismaBackgroundStyle.Living ? 0.56f : 0.76f)
            * layerOpacity;

        Rectangle target = ClientRectangle;
        if (style == MelismaBackgroundStyle.Living)
        {
            float t = Environment.TickCount64 / 1000f;
            int marginX = (int)(Width * 0.065f);
            int marginY = (int)(Height * 0.065f);
            target = new Rectangle(
                -marginX + (int)(MathF.Sin(t * 0.13f) * marginX * 0.45f),
                -marginY + (int)(MathF.Cos(t * 0.10f) * marginY * 0.45f),
                Width + marginX * 2, Height + marginY * 2);
        }
        if (previous != null && progress < 1f)
            DrawCover(g, ReducedCover(previous, blur),
                opacity * (1f - progress), target);
        if (current != null && progress > 0f)
            DrawCover(g, ReducedCover(current, blur), opacity * progress, target);

        if (previous != null || current != null)
        {
            using var scrim = new SolidBrush(Color.FromArgb(
                (int)(157f * layerOpacity), 8, 12, 25));
            g.FillRectangle(scrim, ClientRectangle);
        }
    }

    private void DrawPalette(Graphics g, Image? art, bool moving,
        float layerOpacity)
    {
        var one = Sample(art, 0.24f, 0.28f, Color.FromArgb(42, 73, 107));
        var two = Sample(art, 0.75f, 0.68f, Color.FromArgb(91, 36, 99));
        float motion = moving ? MathF.Sin(Environment.TickCount64 / 4800f) * 0.18f : 0f;
        float angle = 38f + motion * 115f;
        int alpha = Math.Clamp((int)(255f * layerOpacity), 0, 255);
        using var fill = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(alpha, Darken(one, 0.40f)),
            Color.FromArgb(alpha, Darken(two, 0.43f)), angle);
        g.FillRectangle(fill, ClientRectangle);
        using var veil = new SolidBrush(Color.FromArgb(
            (int)(75f * layerOpacity), 5, 11, 27));
        g.FillRectangle(veil, ClientRectangle);
    }

    private static Color Darken(Color c, float amount) =>
        Color.FromArgb((int)(c.R * amount),
                       (int)(c.G * amount), (int)(c.B * amount));

    private Image ReducedCover(Image source, int blur)
    {
        if (blur <= 0) return source;
        blur = Math.Clamp(blur, 0, 67);
        foreach (var item in _blurCache)
            if (ReferenceEquals(item.Source, source) && item.Blur == blur)
                return item.Reduced;

        int target = Math.Clamp((int)(320f / (1f + blur / 4f)), 12, 320);
        var reduced = new Bitmap(target, target);
        using (var g = Graphics.FromImage(reduced))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 0, target, target));
        }
        _blurCache.Add((source, blur, reduced));
        while (_blurCache.Count > 2)
        {
            _blurCache[0].Reduced.Dispose();
            _blurCache.RemoveAt(0);
        }
        return reduced;
    }

    private static void DrawCover(Graphics g, Image image, float opacity,
        Rectangle target)
    {
        if (target.Width <= 0 || target.Height <= 0 || opacity <= 0f ||
            image.Width <= 0 || image.Height <= 0) return;
        float imageRatio = image.Width / (float)image.Height;
        float viewRatio = target.Width / (float)target.Height;
        float croppedWidth = image.Width;
        float croppedHeight = image.Height;
        if (imageRatio > viewRatio) croppedWidth = image.Height * viewRatio;
        else croppedHeight = image.Width / viewRatio;

        float x = (image.Width - croppedWidth) * 0.5f;
        float y = (image.Height - croppedHeight) * 0.5f;
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix
        {
            Matrix33 = Math.Clamp(opacity, 0f, 1f)
        });
        g.DrawImage(image, target, x, y, croppedWidth, croppedHeight,
            GraphicsUnit.Pixel, attributes);
    }

    private void DrawCinemaPanel(Graphics g, LyricFrame frame)
    {
        RectangleF area = MediaArea;
        // The media and lyric zones are layout bounds only, not visually
        // separate panels. The full-screen background flows continuously
        // underneath both columns, with no shade or divider line.
        float pad = Math.Clamp(area.Width * 0.065f, 13f, 36f);
        float maxCover = Math.Min(area.Width - pad * 2f, area.Height * 0.58f);
        maxCover = Math.Max(20f, maxCover);
        float top = Math.Max(10f, area.Height * 0.10f);
        var coverBox = new Rectangle(
            (int)(area.Left + (area.Width - maxCover) / 2f),
            (int)top, (int)maxCover, (int)maxCover);
        using (var backing = new SolidBrush(Color.FromArgb(180, 20, 25, 39)))
            g.FillRectangle(backing, coverBox);

        if (_artwork.Enabled)
        {
            float fade = _artwork.PanelProgress; // Independent, adjustable cover-panel fade.
            if (_artwork.Previous != null && fade < 1f)
                DrawCover(g, _artwork.Previous, 1f - fade, coverBox);
            if (_artwork.Current != null && fade > 0f)
                DrawCover(g, _artwork.Current, fade, coverBox);
        }
        if (!_artwork.Enabled || _artwork.Current == null && _artwork.Previous == null)
        {
            using var markFont = new Font("Segoe UI Symbol", Math.Clamp(maxCover / 5f, 14, 54));
            using var mark = new SolidBrush(Color.FromArgb(180, 198, 216, 230));
            using var center = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("♪", markFont, mark, coverBox, center);
        }

        float textTop = coverBox.Bottom + Math.Clamp(area.Height * 0.035f, 10f, 25f);
        float textHeight = Math.Max(48f, area.Bottom - textTop - 9f);
        using var titleFont = new Font("Segoe UI",
            Math.Clamp(area.Width / 21f, 13f, 23f), FontStyle.Bold);
        using var artistFont = new Font("Segoe UI",
            Math.Clamp(area.Width / 29f, 11f, 17f));
        using var titleBrush = new SolidBrush(Color.White);
        using var artistBrush = new SolidBrush(Color.FromArgb(205, 199, 212, 226));
        // A subtle shadow just under the text preserves readability over
        // light album artwork without tinting the entire Cinema column.
        using var textShadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        using var alignment = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisWord
        };
        var titleRect = new RectangleF(area.Left + pad, textTop,
            area.Width - pad * 2f, Math.Min(65f, textHeight * 0.58f));
        var titleShadowRect = titleRect;
        titleShadowRect.Offset(1.5f, 2f);
        g.DrawString(frame.Song, titleFont, textShadow, titleShadowRect, alignment);
        g.DrawString(frame.Song, titleFont, titleBrush, titleRect, alignment);
        if (frame.Artist.Length > 0)
        {
            var artistRect = new RectangleF(area.Left + pad,
                titleRect.Bottom + 5f, area.Width - pad * 2f,
                Math.Max(15f, textHeight - titleRect.Height - 8f));
            var artistShadowRect = artistRect;
            artistShadowRect.Offset(1f, 2f);
            g.DrawString(frame.Artist, artistFont, textShadow, artistShadowRect, alignment);
            g.DrawString(frame.Artist, artistFont, artistBrush, artistRect, alignment);
        }
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
        _plannedFontFamily = "";
    }

    private void EnsurePlan(Graphics g, LyricFrame frame)
    {
        float availableWidth = Math.Max(1f, LyricsArea.Width - 2f * SidePadding);
        float preferredPoints = Math.Clamp(LyricsArea.Width / 31f, 20f, 43f) *
            _display.FontScale;
        bool changed = _planned.Count != frame.Lines.Count ||
            _plannedWidth != availableWidth ||
            _plannedPoints != preferredPoints ||
            _plannedDpi != g.DpiY ||
            _plannedFontFamily != _display.FontFamily ||
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
        _plannedFontFamily = _display.FontFamily;
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
        if (disposing)
        {
            ClearPlan();
            foreach (var cached in _blurCache) cached.Reduced.Dispose();
            _blurCache.Clear();
            _fluidLiving.Dispose();
        }
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

    private TextLayout MakeLayout(Graphics g, string text,
        float preferredSize, bool bold, float width)
    {
        float size = Math.Max(10f, preferredSize);
        while (true)
        {
            var font = new Font(_display.FontFamily, size,
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
            Math.Max(1f, LyricsArea.Width - 2f * SidePadding));
        DrawLayout(g, layout, y, color, null, 1f);
    }

    private void DrawLayout(Graphics g, TextLayout layout,
        float centerY, Color color, float? playedCharacters, float emphasis,
        float opacity = 1f)
    {
        // The row breaks and reserved height remain fixed. Only the actual
        // font size/color animate, so upcoming lyrics are already wrapped.
        float scale = _display.SimpleMode
            ? 0.84f + 0.16f * Math.Clamp(emphasis, 0f, 1f)
            : 0.67f + 0.33f * Math.Clamp(emphasis, 0f, 1f);
        using var displayFont = new Font(_display.FontFamily,
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
        float width = Math.Max(1f, LyricsArea.Width - 2f * SidePadding);
        for (int i = 0; i < layout.Rows.Count; i++)
        {
            var row = layout.Rows[i];
            var rect = new RectangleF(LyricsArea.Left + SidePadding,
                top + i * layout.RowHeight, width, layout.RowHeight);
            if (_display.DepthBlur && emphasis < 0.5f)
            {
                var shadowRect = new RectangleF(rect.X + 1.5f, rect.Y + 1.5f,
                    rect.Width, rect.Height);
                using var shadow = new SolidBrush(Color.FromArgb(
                    (int)(45f * opacity), 172, 190, 216));
                g.DrawString(row.Text, displayFont, shadow, shadowRect, format);
            }
            g.DrawString(row.Text, displayFont, normalBrush, rect, format);

            if (!playedCharacters.HasValue) continue;
            float playedInRow = Math.Clamp(playedCharacters.Value - row.Start,
                0f, row.Text.Length);
            if (playedInRow <= 0f) continue;
            if (playedInRow >= row.Text.Length)
            {
                g.DrawString(row.Text, displayFont, playedBrush, rect, format);
                continue;
            }

            // The clip is relative to this wrapped row, not the original LRC
            // line, so multi-row enhanced lyrics highlight correctly.
            float totalWidth = TextWidth(g, row.Text, displayFont);
            int wholeCharacters = Math.Clamp((int)MathF.Floor(playedInRow),
                0, row.Text.Length);
            float fraction = playedInRow - wholeCharacters;
            float playedWidth = TextWidth(g, row.Text[..wholeCharacters], displayFont);
            if (fraction > 0f && wholeCharacters < row.Text.Length)
            {
                float next = TextWidth(g, row.Text[..(wholeCharacters + 1)],
                    displayFont);
                playedWidth += Math.Max(0f, next - playedWidth) * fraction;
            }
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

    public KaraokeDisplay(Screen screen, Func<LyricFrame> frame, AlbumArtworkState artwork,
        MelismaDisplaySettings display, PlaybackIdleBackground idleBackground)
    {
        Text = "Melisma Karaoke · TV display";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        BackColor = Color.Black;
        KeyPreview = true;
        _canvas = new LyricsCanvas(frame, artwork, display, idleBackground) { Dock = DockStyle.Fill };
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
