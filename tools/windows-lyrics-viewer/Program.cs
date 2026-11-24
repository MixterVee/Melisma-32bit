using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Media.Control;

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
    private static readonly HttpClient Client = CreateHttpClient();

    public LyricsController()
    {
        Text = "Melisma Universal · Windows Lyrics Preview v0.3";
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
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
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
        grid.Controls.Add(toolbar, 0, 2);

        _preview = new LyricsCanvas(CurrentFrame) { Dock = DockStyle.Fill };
        grid.Controls.Add(_preview, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _infoLabel.Dock = DockStyle.Fill;
        var source = Label(10, Color.FromArgb(146, 170, 191));
        source.Dock = DockStyle.Fill;
        source.Text = "Lyrics: —";
        footer.Controls.Add(_infoLabel, 0, 0);
        footer.Controls.Add(source, 0, 1);
        grid.Controls.Add(footer, 0, 4);

        _screenTimer.Tick += (_, _) =>
        {
            ReadBridge();
            _preview.Invalidate();
            _projection?.Redraw();
            _infoLabel.Text =
                $"{Clock(_positionMs)} / {(_durationMs > 0 ? Clock(_durationMs) : "unknown")}   ·   " +
                $"{(_bridgeFound ? "Direct Foobar bridge" : "Bridge not connected")}   ·   " +
                $"{StateText()}   ·   Lyrics delay: {_offsetMs:+0;-0;0} ms " +
                $"({(_offsetSaved ? "saved for this song" : "not saved")})";
            source.Text = "Lyrics: " + _source;
            hint.Text = _notice;
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
            _trackLabel.Text = title + "  —  " + artist;
            _offsetMs = _offsets.Read(title, artist, album);
            _offsetSaved = true;
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

    private void ChangeOffset(int delta)
    {
        _offsetMs = Math.Clamp(_offsetMs + delta, -20000, 20000);
        if (_song.Length > 0)
        {
            _offsetSaved = _offsets.TryWrite(_song, _artist, _album, _offsetMs);
            if (!_offsetSaved)
                _notice = "Timing changed, but Windows could not save it to your profile.";
        }
        _preview.Invalidate();
        _projection?.Redraw();
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
        _projection = new KaraokeDisplay(screen, CurrentFrame);
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MelismaWindowsLyricsPreview/0.3.0");
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

internal sealed class LyricsCanvas : Control
{
    private readonly Func<LyricFrame> _frame;
    private static readonly Color Highlight = Color.FromArgb(113, 236, 227);
    private static readonly Color Other = Color.FromArgb(142, 157, 181);
    private int _lastActive = int.MinValue;
    private int _transitionFrom = -1;
    private long _transitionTick;

    public LyricsCanvas(Func<LyricFrame> frame)
    {
        _frame = frame;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(17, 23, 39);
        Dock = DockStyle.Fill;
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
        var frame = _frame();

        if (frame.Lines.Count == 0)
        {
            _lastActive = int.MinValue;
            Draw(g, "♪", Height * 0.33f, 76, Highlight, true, 120);
            Draw(g, "Waiting for synchronized lyrics", Height * 0.55f, 22, Color.White, true, 76);
            Draw(g, frame.Message, Height * 0.67f, 12, Other, false, 65);
            return;
        }

        int active = frame.ActiveIndex;
        if (active != _lastActive)
        {
            _transitionFrom = _lastActive;
            _lastActive = active;
            _transitionTick = Environment.TickCount64;
        }

        int center = active < 0 ? 0 : active;
        float fontSize = Math.Clamp(Width / 31f, 20, 43);
        float centerY = Height * 0.50f;
        float step = Math.Clamp(Height / 6.5f, 52, 105);

        // Animate adjacent line changes; jump directly on seeks or track changes.
        bool animate = active >= 0 && _transitionFrom >= 0 &&
                       Math.Abs(active - _transitionFrom) == 1;
        float progress = animate
            ? Math.Clamp((Environment.TickCount64 - _transitionTick) / 220f, 0f, 1f)
            : 1f;
        float eased = 1f - (1f - progress) * (1f - progress) * (1f - progress);
        float slide = animate ? Math.Sign(active - _transitionFrom) * step * (1f - eased) : 0f;

        for (int i = -3; i <= 3; i++)
        {
            int index = center + i;
            if (index < 0 || index >= frame.Lines.Count) continue;

            float emphasis = index == active ? (animate ? eased : 1f)
                : index == _transitionFrom && animate ? 1f - eased : 0f;
            float size = fontSize * (0.67f + 0.33f * emphasis);
            float lineY = centerY + i * step + slide;
            var tint = Blend(Other, Highlight, emphasis);
            var line = frame.Lines[index];

            if (index == active && line.Words is { Count: > 0 } && progress >= 0.7f)
                DrawEnhanced(g, line, frame.LyricPositionMs, lineY, size, step * 1.10f);
            else
                Draw(g, line.Text, lineY, size, tint, emphasis >= 0.5f, step * 1.10f);
        }
    }

    private static Color Blend(Color left, Color right, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(left.R + (right.R - left.R) * amount),
            (int)(left.G + (right.G - left.G) * amount),
            (int)(left.B + (right.B - left.B) * amount));
    }

    private Font FitFont(Graphics g, string text, float points, bool bold, float availableWidth)
    {
        var style = bold ? FontStyle.Bold : FontStyle.Regular;
        float size = Math.Max(12f, points);
        using var measure = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap
        };
        while (true)
        {
            var font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            if (size <= 12.01f ||
                g.MeasureString(text, font, 10000, measure).Width <= availableWidth)
                return font;
            font.Dispose();
            size = Math.Max(12f, size * 0.88f);
        }
    }

    private void Draw(Graphics g, string text, float centerY,
        float points, Color color, bool bold, float boxHeight)
    {
        var bounds = new RectangleF(26, centerY - boxHeight / 2,
            Math.Max(1, Width - 52), boxHeight);
        using var font = FitFont(g, text, points, bold, bounds.Width);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        g.DrawString(text, font, brush, bounds, format);
    }

    private void DrawEnhanced(Graphics g, LrcLine line, long lyricTime,
        float centerY, float points, float boxHeight)
    {
        var bounds = new RectangleF(26, centerY - boxHeight / 2,
            Math.Max(1, Width - 52), boxHeight);
        using var font = FitFont(g, line.Text, points, true, bounds.Width);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };
        using var unplayed = new SolidBrush(Other);
        using var played = new SolidBrush(Highlight);
        g.DrawString(line.Text, font, unplayed, bounds, format);

        int playedCharacters = 0;
        foreach (var word in line.Words!)
        {
            if (lyricTime < word.AtMs) break;
            playedCharacters += word.Text.Length;
        }
        if (playedCharacters <= 0) return;
        if (playedCharacters >= line.Text.Length)
        {
            g.DrawString(line.Text, font, played, bounds, format);
            return;
        }

        // Reveal the completed words from left to right, keeping the whole line centered.
        using var measure = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap
        };
        float fullWidth = g.MeasureString(line.Text, font, 10000, measure).Width;
        float prefixWidth = g.MeasureString(line.Text[..playedCharacters], font, 10000, measure).Width;
        float left = bounds.Left + (bounds.Width - fullWidth) / 2f;
        var saved = g.Save();
        g.SetClip(new RectangleF(left - 1, bounds.Top,
            Math.Min(bounds.Right - left + 1, prefixWidth + 2), bounds.Height),
            CombineMode.Intersect);
        g.DrawString(line.Text, font, played, bounds, format);
        g.Restore(saved);
    }
}

internal sealed class KaraokeDisplay : Form
{
    private readonly LyricsCanvas _canvas;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 55 };

    public KaraokeDisplay(Screen screen, Func<LyricFrame> frame)
    {
        Text = "Melisma Karaoke · TV display";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        BackColor = Color.Black;
        KeyPreview = true;
        _canvas = new LyricsCanvas(frame) { Dock = DockStyle.Fill };
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
