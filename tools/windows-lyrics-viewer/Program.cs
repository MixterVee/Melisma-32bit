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
    string Song, string Artist, string Message, long LyricPositionMs,
    string Source, string? PlainLyrics = null);

internal sealed class LyricsController : Form
{
    private readonly NativeTimelineReader _bridge = new();
    private readonly NativeTrackMetadataReader _nativeTags = new();
    private readonly SongOffsetStore _offsets = new();
    private readonly LyricsResultCache _lyricCache = new();
    private readonly AlbumArtworkState _artwork = new();
    private readonly MelismaDisplaySettings _display = MelismaDisplaySettings.Load();
    private readonly PlaybackIdleBackground _idleBackground = new();
    private MelismaDisplaySettingsForm? _settingsWindow;
    private readonly System.Windows.Forms.Timer _screenTimer = new() { Interval = 25 };
    private readonly System.Windows.Forms.Timer _mediaTimer = new() { Interval = 1200 };
    private readonly Label _trackLabel = Label(18, Color.White, true);
    private readonly Label _statusLabel = Label(10, Color.FromArgb(126, 225, 223));
    private readonly Label _infoLabel = Label(10, Color.FromArgb(170, 181, 203));
    private readonly Button _secondScreenButton = Button("Second screen", 146);
    private readonly Button _minusButton = Button("−100 ms", 108);
    private readonly Button _plusButton = Button("+100 ms", 108);
    private readonly Button _resetButton = Button("Reset", 84);
    private readonly LyricsCanvas _preview;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private KaraokeDisplay? _projection;
    private bool _mediaBusy;
    private readonly List<LrcLine> _lines = new();
    private string _plainLyrics = "";
    private readonly HashSet<string> _upgradedCacheThisSession = new(StringComparer.Ordinal);
    private string _song = "", _artist = "", _album = "";
    private string _source = "No lyrics loaded";
    private string _lookupReport = "No lookup has been run yet.";
    private string _notice = "Start playing an MP3 in Foobar2000.";
    private long _positionMs;
    private long _durationMs;
    private int _offsetMs;
    private bool _offsetSaved = true;
    private int _playerState;
    private bool _bridgeFound;
    private bool _bridgeWasStale;
    private long _bridgeAgeMs = -1;
    private string _clockSource = "No playback clock";
    private bool _clockUsable;
    private long _lastClockLabelTick;
    private ulong _lastFoobarTrackGeneration;
    private ulong _lastLoadedGeneration;

    // Secondary Windows media-session timeline, usable when Foobar's
    // native bridge is absent. Unlike track metadata, this may not be
    // published by every foobar2000 configuration.
    private bool _windowsTimelineValid;
    private int _windowsState;
    private long _windowsPositionMs;
    private long _windowsDurationMs;
    private long _windowsSampleTick;
    private string _loadedFor = "";
    private CancellationTokenSource? _searchCancellation;
    private int _artworkRequestId;
    private string _artworkStatus = "No track artwork loaded";
    private static readonly HttpClient Client = CreateHttpClient();

    public LyricsController()
    {
        Text = "Melisma Universal · Windows v0.6.11";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(825, 590);
        Size = new Size(1050, 780);
        ControllerWindowPlacement.Restore(this);
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
        var online = Button("Find lyrics", 120);
        online.Click += (_, _) => _ = LoadLyricsAsync(true);
        toolbar.Controls.Add(online);

        var open = Button("Open lyrics", 115);
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
        var settingsButton = Button("Display…", 119);
        settingsButton.Click += OpenDisplaySettings;
        extraControls.Controls.Add(settingsButton);
        var openCover = Button("Cover…", 106);
        openCover.Click += OpenCoverImage;
        extraControls.Controls.Add(openCover);
        var lookupDetails = Button("Sources…", 120);
        lookupDetails.Click += (_, _) => MessageBox.Show(this,
            BuildDiagnostics(),
            "Melisma · Lyrics and playback diagnostics",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        extraControls.Controls.Add(lookupDetails);
        var note = Label(9.5f, Color.FromArgb(175, 188, 204));
        note.Text = "Cinema · Artwork · Lyrics";
        note.Width = 400;
        note.Height = 37;
        extraControls.Controls.Add(note);
        toolbarArea.Controls.Add(extraControls, 0, 1);
        grid.Controls.Add(toolbarArea, 0, 2);

        _preview = new LyricsCanvas(CurrentFrame, _artwork, _display, _idleBackground, controllerPreview: true) { Dock = DockStyle.Fill };
        grid.Controls.Add(_preview, 0, 3);

        // Keep the footer quiet: it only changes when the selected song or
        // the user's timing adjustment changes, never on the animation paint tick.
        _infoLabel.Dock = DockStyle.Fill;
        _infoLabel.Text = "Lyrics delay: 0 ms";
        grid.Controls.Add(_infoLabel, 0, 4);

        _screenTimer.Tick += (_, _) =>
        {
            ReadBridge();
            _idleBackground.Update(_playerState == 1, _display);
            long now = Environment.TickCount64;
            if (now - _lastClockLabelTick >= 400)
            {
                _lastClockLabelTick = now;
                UpdateDelayLabel();
            }

            // Android uses a fast tick only while it has moving lyrics.
            // The older unconditional 25ms loop repainted full cover art
            // and Living effects even for silence, paused music and misses.
            var current = CurrentFrame();
            bool timedWord = _playerState == 1 && current.ActiveIndex >= 0 &&
                current.ActiveIndex < current.Lines.Count &&
                current.Lines[current.ActiveIndex].Words is { Count: > 0 } &&
                (_display.GrowActiveWord || _display.SmoothWordFill);
            int interval = timedWord ? 25 :
                _playerState == 1 && current.Lines.Count > 0 ? 65 :
                _playerState == 1 ? 75 : 120;
            if (_screenTimer.Interval != interval) _screenTimer.Interval = interval;

            if (WindowState != FormWindowState.Minimized && _preview.Visible)
                _preview.Invalidate();
            // Single shared paint clock: the TV must not paint twice.
            _projection?.Redraw();
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
        FormClosing += (_, _) => ControllerWindowPlacement.Save(this);
        FormClosed += (_, _) =>
        {
            _screenTimer.Stop();
            _mediaTimer.Stop();
            _bridge.Dispose();
            _nativeTags.Dispose();
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
        long nowTick = Environment.TickCount64;
        if (_bridge.TryRead(out var native))
        {
            long sampleAge = nowTick - native.TickMs;
            _bridgeAgeMs = sampleAge;
            // The reader holds the mapping open even if Foobar exits, so
            // samples can otherwise remain frozen forever. Release a stale
            // handle to permit a new Foobar bridge mapping to be published.
            if (PlaybackClockHealth.Fresh(nowTick, native.TickMs))
            {
                _bridgeFound = true;
                _bridgeWasStale = false;
                _clockUsable = true;
                // Android reacts to track-change events, not a fixed media
                // poll. Foobar's existing bridge publishes that event as a
                // generation change, so ask GSMTC for metadata immediately.
                bool newTrack = native.Generation != _lastFoobarTrackGeneration;
                _lastFoobarTrackGeneration = native.Generation;
                _clockSource = "Foobar bridge";
                _playerState = native.State;
                _durationMs = native.DurationMs > 0 ? (long)native.DurationMs : 0;
                // Duration must be updated before metadata triggers a search.
                if (newTrack && !_mediaBusy) _ = PollMediaAsync();
                long age = Math.Clamp(sampleAge, 0L, 350L);
                _positionMs = (long)native.PositionMs +
                    (native.State == 1 ? age : 0);
                if (_durationMs > 0) _positionMs = Math.Min(_positionMs, _durationMs);
                return;
            }
            _bridgeWasStale = true;
            _bridge.Dispose();
        }
        _bridgeFound = false;

        // Graceful fallback to the Windows session *only if* it has a
        // meaningful, dated position. Foobar may expose titles through GSMTC
        // without exposing ANY timeline; never simulate a position from
        // song start just because the session says "playing".
        _playerState = _windowsState;
        if (_windowsTimelineValid &&
            nowTick >= _windowsSampleTick &&
            nowTick - _windowsSampleTick <= 4000)
        {
            _clockUsable = true;
            _clockSource = "Windows media timeline";
            _durationMs = _windowsDurationMs;
            long age = Math.Clamp(nowTick - _windowsSampleTick, 0, 2500);
            _positionMs = _windowsPositionMs +
                (_windowsState == 1 ? age : 0);
            if (_durationMs > 0) _positionMs = Math.Min(_positionMs, _durationMs);
            return;
        }
        _clockUsable = false;
        _clockSource = _bridgeWasStale
            ? "Foobar bridge stale · restart foobar2000"
            : "No playback clock · check Foobar bridge";
        _positionMs = 0;
        _durationMs = 0;
    }

    private void CaptureWindowsTimeline(
        GlobalSystemMediaTransportControlsSession session)
    {
        var info = session.GetPlaybackInfo();
        _windowsState = info.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => 1,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => 2,
            _ => 0
        };
        var timeline = session.GetTimelineProperties();
        _windowsTimelineValid = false;
        if (timeline.LastUpdatedTime.Year < 2000) return;
        double ms = timeline.Position.TotalMilliseconds;
        double durationMs = timeline.EndTime > timeline.StartTime
            ? (timeline.EndTime - timeline.StartTime).TotalMilliseconds
            : timeline.EndTime.TotalMilliseconds;
        if (!double.IsFinite(ms) || ms < 0 ||
            !double.IsFinite(durationMs) || durationMs < 0 ||
            (durationMs <= 0 && ms <= 0)) return;
        double ageMs = Math.Max(0,
            (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalMilliseconds);
        if (!double.IsFinite(ageMs) || ageMs > 24 * 3600_000) return;
        _windowsDurationMs = (long)durationMs;
        _windowsPositionMs = (long)(ms +
            (_windowsState == 1 ? ageMs : 0));
        if (_windowsDurationMs > 0)
            _windowsPositionMs = Math.Min(_windowsPositionMs, _windowsDurationMs);
        _windowsSampleTick = Environment.TickCount64;
        _windowsTimelineValid = true;
    }

    private string BuildDiagnostics()
    {
        var frame = CurrentFrame();
        string active = frame.ActiveIndex >= 0 &&
            frame.ActiveIndex < frame.Lines.Count
            ? $"Line {frame.ActiveIndex + 1}/{frame.Lines.Count} " +
              $"(starts at {Clock(frame.Lines[frame.ActiveIndex].AtMs)})"
            : $"None active yet ({frame.Lines.Count} lines loaded)";
        string native = _bridgeFound
            ? $"Connected, sample age {_bridgeAgeMs} ms"
            : _bridgeWasStale
                ? $"STALE — latest sample age {_bridgeAgeMs} ms"
                : "Not connected / unreadable";
        return "PLAYBACK TIMING (live)\n" +
            $"Playback status: {StateText()}\n" +
            $"Clock: {_clockSource}\n" +
            $"Current position: {(_clockUsable ? Clock(_positionMs) : "UNAVAILABLE")}\n" +
            $"Duration: {(_clockUsable ? Clock(_durationMs) : "UNAVAILABLE")}\n" +
            $"Native Foobar bridge: {native}\n" +
            $"Windows media timeline: {(_windowsTimelineValid ? "Available" : "Not published")}\n" +
            $"Active lyric: {active}\n" +
            $"Artwork: {_artworkStatus}\n\n" +
            "If the position is unavailable, check Foobar2000 → " +
            "Preferences → Components for 'Melisma Foobar2000 Bridge' " +
            "and restart Foobar2000 if needed.\n\n" +
            "LYRIC SOURCE DETAILS\n" + _lookupReport;
    }

    private async Task PollMediaAsync()
    {
        if (_mediaBusy || _manager == null || IsDisposed) return;
        _mediaBusy = true;
        try
        {
            var session = _manager.GetSessions().FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains("foobar", StringComparison.OrdinalIgnoreCase));
            if (session != null)
            {
                try { CaptureWindowsTimeline(session); }
                catch (Exception ex) when (ex is InvalidOperationException or
                            System.Runtime.InteropServices.COMException)
                {
                    _windowsTimelineValid = false;
                }
            }
            else
            {
                _windowsState = 0;
                _windowsTimelineValid = false;
            }
            var props = session == null ? null : await session.TryGetMediaPropertiesAsync();

            // The native bridge identifies the actual Foobar track.
            // Windows GSMTC can publish an incomplete artist/album or lag
            // behind the Foobar track-change generation.
            bool nativeCurrent = _nativeTags.TryRead(out var tags) &&
                tags.Generation == _lastFoobarTrackGeneration &&
                !string.IsNullOrWhiteSpace(tags.Title);
            string title = nativeCurrent ? tags.Title : (props?.Title ?? "").Trim();
            string artist = nativeCurrent && tags.Artist.Length > 0
                ? tags.Artist : (props?.Artist ?? "").Trim();
            string album = nativeCurrent && tags.Album.Length > 0
                ? tags.Album : (props?.AlbumTitle ?? "").Trim();
            if (title.Length == 0)
            {
                _notice = "Waiting for Foobar2000 song metadata.";
                return;
            }

            // Read duration from Foobar directly before constructing the
            // provider request; zero-duration GSMTC is not authoritative.
            if (_bridge.TryRead(out var live) && live.DurationMs > 0 &&
                (!nativeCurrent || live.Generation == tags.Generation))
                _durationMs = (long)live.DurationMs;

            if (title == _song && artist == _artist && album == _album &&
                _lastLoadedGeneration == _lastFoobarTrackGeneration) return;
            _lastLoadedGeneration = _lastFoobarTrackGeneration;
            _song = title; _artist = artist; _album = album;
            _loadedFor = title + "|" + artist + "|" + album + "|" + _durationMs;
            // Keep the previous cover displayed until the replacement arrives.
            // Clearing here produced a blank panel followed by a new fade-IN,
            // instead of the selected old-cover-to-new-cover crossfade.
            int artworkRequest = ++_artworkRequestId;
            _artworkStatus = "Looking for artwork";
            // Keep the crossfade, but never show GSMTC's previous cover
            // against metadata from the next native Foobar track.
            var thumbnail = props != null &&
                string.Equals((props.Title ?? "").Trim(), title,
                    StringComparison.OrdinalIgnoreCase) ? props.Thumbnail : null;
            _ = LoadArtworkAsync(thumbnail, title, artist, album,
                _durationMs, _loadedFor, artworkRequest);
            _trackLabel.Text = title + "  —  " + artist;
            _offsetMs = _offsets.Read(title, artist, album);
            _offsetSaved = true;
            UpdateDelayLabel();
            _lines.Clear();
            _plainLyrics = "";
            _lookupReport = "Waiting for provider responses for " +
                title + " — " + artist + "…";
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
        string title, string artist, string album, long durationMs,
        string trackKey, int requestId)
    {
        bool StillCurrent() => !IsDisposed && requestId == _artworkRequestId &&
            trackKey == _loadedFor;
        try
        {
            // Always trust the player's own artwork first. If foobar2000
            // publishes no thumbnail, only then try a public catalog.
            if (thumbnail != null)
            {
                try
                {
                    using var random = await thumbnail.OpenReadAsync();
                    if (random.Size is > 128 and <= 8388608)
                    {
                        using var reader = new DataReader(random);
                        uint count = (uint)random.Size;
                        if (await reader.LoadAsync(count) == count)
                        {
                            byte[] data = new byte[count];
                            reader.ReadBytes(data);
                            if (!StillCurrent()) return;
                            _artwork.Replace(data);
                            _artworkStatus = "Player-supplied album artwork";
                            _preview.Invalidate();
                            _projection?.Redraw();
                            return;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or
                            InvalidOperationException or
                            System.Runtime.InteropServices.COMException)
                {
                    // A missing or unreadable GSMTC thumbnail can happen even
                    // for a song with embedded cover art; try online fallback.
                }
            }
            if (!StillCurrent()) return;
            if (!_display.AutoArtworkLookup)
            {
                _artwork.Replace(null); // Fade away only after this track is confirmed artless.
                _artworkStatus = "No player thumbnail (online fallback off)";
                return;
            }
            byte[]? matched = await OnlineArtworkProvider.FindAsync(
                title, artist, album, durationMs, CancellationToken.None);
            if (!StillCurrent()) return;
            if (matched == null)
            {
                _artwork.Replace(null); // No replacement exists: fade the old cover out.
                _artworkStatus = "No player thumbnail or matched catalog artwork";
                return;
            }
            _artwork.Replace(matched);
            _artworkStatus = "Matched iTunes album artwork (online fallback)";
            _preview.Invalidate();
            _projection?.Redraw();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                        or ArgumentException or InvalidOperationException
                        or System.Runtime.InteropServices.COMException
                        or OutOfMemoryException)
        {
            if (StillCurrent())
            {
                _artwork.Replace(null);
                _artworkStatus = "Cover image unavailable: " + ex.GetType().Name;
            }
            // Artwork is optional. Never interrupt lyrics/playback.
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
            _artworkStatus = "Manually selected cover image";
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
        // A WinForms Label redraws its complete surface on every Text
        // change. A moving mm:ss counter made the whole footer flash
        // approximately every 400ms. Precise current position remains
        // available in Sources... -> PLAYBACK TIMING (live), on demand.
        string text = $"Lyrics delay: {_offsetMs:+0;-0;0} ms" +
            (_song.Length > 0
                ? $" ({(_offsetSaved ? "saved" : "not saved")})"
                : "") +
            (_clockUsable
                ? $"   |   Playback: {StateText()} · {_clockSource}"
                : $"   |   ⚠ Playback position UNAVAILABLE · {_clockSource}");
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
            _artist, _notice, lyricMs, _source, _plainLyrics);
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
            if (!IsDisposed) _secondScreenButton.Text = "Second screen";
        };
        _projection.Show(this);
        _secondScreenButton.Text = "Close second";
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
        // Android's LyricsRequest.isUsable only requires a title.
        // A missing artist reduces confidence, but should not block all lookup.
        if (_song.Length == 0)
        {
            _lookupReport = "Foobar2000 has not published a track title.";
            _notice = "Waiting for a title from Foobar2000.";
            _statusLabel.Text = "Lyrics lookup unavailable · Missing title";
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
        _statusLabel.Text = "Checking lyric providers…";
        _notice = requested ? "Checking all lyric sources again…" :
            "Finding the best real word-timed lyrics…";
        _lookupReport = $"Song: {title}\nArtist: {artist}\nAlbum: {album}\n" +
            $"Foobar bridge duration: {duration / 1000d:F1}s\n" +
            "Providers: checking concurrently…";
        long searchStartedTick = Environment.TickCount64;
        long? firstDisplayMs = null;
        string? shownSource = null;
        string? shownName = null;
        int shownCount = -1;
        LyricsResultCache.Entry? cached = null;

        // Like Android's already-cached path, the same song can show lyrics
        // immediately when replayed, even if lyric providers are slow.
        // Continue searching so genuinely improved timing can replace an
        // outdated cached source. Manual "Find lyrics" always refreshes.
        if (!requested && _lyricCache.TryLoad(title, artist, album,
                duration, out cached) && cached != null)
        {
            _lines.Clear();
            _lines.AddRange(cached.Lines);
            _plainLyrics = cached.PlainLyrics ?? "";
            _source = cached.SourceId == "lrcred" ? "lrc.red" : cached.SourceName;
            firstDisplayMs = 0;
            shownSource = "cache:" + cached.SourceId;
            shownName = cached.SourceName;
            shownCount = cached.Lines.Count;
            _statusLabel.Text = cached.WordTimed ? "Word-synced · Cached " + cached.SourceName
                : cached.Lines.Count > 0 ? "Line-synced · Cached " + cached.SourceName
                : "Plain lyrics · Cached " + cached.SourceName;
            _notice = "Lyrics loaded immediately from disk";
            _lookupReport = "Immediately displayed stored lyrics from " + cached.SourceName +
                ".\nAndroid-style cache: no fresh search required for word timing.";
            _preview.Invalidate();
            _projection?.Redraw();
            // Android does not hammer every provider on every replay.
            // Background improvement for lower-tier results is attempted
            // just once per running session, never before showing the cache.
            if (cached.WordTimed || !_upgradedCacheThisSession.Add(searchFor)) return;
        }

        bool CurrentSearch() =>
            !token.IsCancellationRequested && !IsDisposed &&
            searchFor == _loadedFor;

        void Present(LyricsSourceOrchestrator.Lookup result, bool complete)
        {
            if (!CurrentSearch()) return;
            var best = result.Best;
            long elapsed = Math.Max(0, Environment.TickCount64 - searchStartedTick);
            bool hasLines = best is { Lines: { Count: > 0 } };
            // Never replace a good cached word-synced song with a slower
            // provider's incomplete/line-only first result. Only upgrade
            // when the fresh response is genuinely as good or better.
            bool accepted = hasLines &&
                (cached == null || LyricsResultCache.ShouldReplace(cached, best!));
            bool newResult = accepted && (
                shownSource != best!.Id || shownName != best.Name ||
                shownCount != best.Total || _lines.Count == 0);

            if (newResult)
            {
                // The first accessible provider can begin displaying lyrics
                // immediately. A later provider may upgrade line timing to
                // real word timing, or fill a missing first verse.
                _lines.Clear();
                _lines.AddRange(best!.Lines!);
                _plainLyrics = "";
                cached = null;
                shownSource = best.Id;
                shownName = best.Name;
                shownCount = best.Total;
                firstDisplayMs ??= elapsed;
                _preview.Invalidate();
                _projection?.Redraw();
            }

            _lookupReport = $"Foobar2000 metadata:\n" +
                $"Title: {title}\nArtist: {artist}\nAlbum: {album}\n" +
                $"Duration: {duration / 1000d:F1} seconds\n\n" +
                $"Lyric search elapsed: {elapsed / 1000d:F1} seconds\n" +
                $"First playable lyrics displayed after: " +
                (firstDisplayMs.HasValue ? $"{firstDisplayMs.Value / 1000d:F1} seconds"
                                         : "not available yet") + "\n" +
                $"Providers completed: {result.All.Count}/5\n\n" +
                "Sources checked:\n" + result.Report +
                "\n\nSelected: " + (best?.Name ?? "No timed lyrics yet") +
                (cached != null
                    ? "\nDisplayed immediately from cached source: " + cached.SourceName
                    : "") +
                "\n\nOnly genuine source timestamps are used.";

            if (!hasLines)
            {
                if (complete && cached == null && _lines.Count == 0)
                {
                    _source = _plainLyrics.Length > 0 ? "LRCLIB plain lyrics"
                        : "No synchronized lyrics found";
                    _statusLabel.Text = _plainLyrics.Length > 0
                        ? "Plain lyrics · No synchronized timestamps"
                        : "No synchronized lyrics found · Sources…";
                    _notice = _plainLyrics.Length > 0
                        ? "Plain lyrics are available (no word timing)"
                        : "No matching timed lyrics. Check Sources… for the reason.";
                }
                return;
            }
            if ((newResult || complete) && cached == null)
            {
                bool introAdded = best!.Name.Contains("+ LRCLIB intro",
                    StringComparison.Ordinal);
                _source = best.Id == "lrcred" ? (introAdded
                    ? "lrc.red + LRCLIB intro" : "lrc.red") : best.Name;
                string timing = introAdded
                    ? "Word-synced + line-timed intro"
                    : best.WordTimed ? "Word-synced" : "Line-synced";
                _statusLabel.Text = $"{timing} · {best.Name}" +
                    (complete ? "" : " · refining");
                _notice = !complete
                    ? $"{best.Total} synchronized lines ready · refining sources"
                    : introAdded
                        ? $"{best.Total} lines · Opening line-timed, later word-timed"
                        : best.WordTimed
                            ? $"{best.Timed} word-timed lines · Pause or seek to test"
                            : $"{best.Total} line-timed lines · Sources… for details";
            }
        }

        try
        {
            // Stream the first valid provider result instead of waiting for
            // every service to finish or hit its 12-second timeout.
            // This fixes the 15–20s delayed lyric appearance on track change
            // while still allowing an improved word-timed source to arrive.
            var lookup = await LyricsSourceOrchestrator.QueryAsync(Client,
                title, artist, album, duration,
                async ct =>
                {
                    var synced = await Lrclib.FindAsync(
                        title, artist, album, duration, ct, plain =>
                        {
                            if (!CurrentSearch() || _lines.Count > 0 ||
                                string.IsNullOrWhiteSpace(plain)) return;
                            _plainLyrics = plain;
                            _source = "LRCLIB plain lyrics";
                            _statusLabel.Text = "Plain lyrics · Seeking timed lyrics";
                            _notice = "Showing plain lyrics while other sources are checked";
                            _preview.Invalidate();
                            _projection?.Redraw();
                            if (!requested)
                                _lyricCache.SavePlain(title, artist, album, duration, plain);
                        });
                    return synced == null ? null : LrcParser.Parse(synced);
                }, token, partial => Present(partial, complete: false));
            Present(lookup, complete: true);
            // Store only the final selected successful result, never
            // provisional incomplete data or failed/network-timeout states.
            if (CurrentSearch() && cached == null &&
                lookup.Best is { Lines: { Count: > 0 } } final)
                _lyricCache.Save(title, artist, album, duration, final.Id,
                    final.Name, final.Lines);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                _source = "Lyrics lookup unavailable";
                _statusLabel.Text = "Lyrics lookup failed · Source details…";
                _notice = "A source lookup failed: " + ex.Message;
                _lookupReport += "\n\nLookup error: " +
                    ex.GetType().Name + ": " + ex.Message;
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "MelismaWindows/0.6.10 (Windows; https://github.com/MixterVee/Melisma-32bit)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static class Lrclib
    {
        private static string Arg(string value) => Uri.EscapeDataString(value);
        private static string? Field(JsonElement item, string name) =>
            item.TryGetProperty(name, out var field) &&
            field.ValueKind == JsonValueKind.String ? field.GetString() : null;

        // Android tries exact tags, cleaned title + primary artist, then
        // a scored search. This avoids silently discarding valid lyrics
        // because Foobar tags contain a featured artist or different album.
        public static async Task<string?> FindAsync(
            string title, string artist, string album, long durationMs,
            CancellationToken token, Action<string>? onPlain = null)
        {
            string clean = AndroidLyricsMatch.CleanTitle(title);
            string primary = AndroidLyricsMatch.SplitArtists(artist).FirstOrDefault() ?? artist;
            string? plainCandidate = null;

            var exacts = new List<(string Title, string Artist, string Album, bool Duration)>
            {
                (title, artist, album, true)
            };
            if (clean != title || primary != artist || album.Length > 0 || durationMs > 0)
                exacts.Add((clean, primary, "", false));

            foreach (var exact in exacts)
            {
                if (string.IsNullOrWhiteSpace(exact.Artist)) continue;
                string url = "https://lrclib.net/api/get?track_name=" +
                    Arg(exact.Title) + "&artist_name=" + Arg(exact.Artist);
                if (exact.Album.Length > 0)
                    url += "&album_name=" + Arg(exact.Album);
                if (exact.Duration && durationMs > 0)
                    url += "&duration=" + Math.Round(durationMs / 1000d)
                        .ToString(CultureInfo.InvariantCulture);
                using var response = await Client.GetAsync(url, token);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new HttpRequestException("LRCLIB rate limited the request");
                if (response.StatusCode == HttpStatusCode.NotFound) continue;
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync(token));
                var item = doc.RootElement;
                if (item.TryGetProperty("instrumental", out var instrumental) &&
                    instrumental.ValueKind == JsonValueKind.True) continue;
                var synced = Field(item, "syncedLyrics");
                if (!string.IsNullOrWhiteSpace(synced)) return synced;
                var plain = Field(item, "plainLyrics");
                if (plain is { Length: > 4 and <= 30000 }) plainCandidate ??= plain;
            }

            string searchUrl = "https://lrclib.net/api/search?track_name=" + Arg(clean);
            if (!string.IsNullOrWhiteSpace(primary))
                searchUrl += "&artist_name=" + Arg(primary);
            using var search = await Client.GetAsync(searchUrl, token);
            if (search.StatusCode == HttpStatusCode.TooManyRequests)
                throw new HttpRequestException("LRCLIB rate limited search");
            search.EnsureSuccessStatusCode();
            using var results = JsonDocument.Parse(
                await search.Content.ReadAsStringAsync(token));
            string? bestTimed = null, bestPlain = null;
            float bestTimedScore = -1f, bestPlainScore = -1f;
            if (results.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in results.RootElement.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    if (row.TryGetProperty("instrumental", out var instrumental) &&
                        instrumental.ValueKind == JsonValueKind.True) continue;
                    string candidateTitle = Field(row, "trackName") ?? "";
                    string candidateArtist = Field(row, "artistName") ?? "";
                    long candidateDuration = row.TryGetProperty("duration", out var d) &&
                        d.ValueKind == JsonValueKind.Number
                            ? (long)(d.GetDouble() * 1000d) : 0L;
                    if (!Accepts(title, artist, durationMs, candidateTitle,
                        candidateArtist, candidateDuration)) continue;
                    float score = AndroidLyricsMatch.Score(title, artist, durationMs,
                        candidateTitle, candidateArtist, candidateDuration);

                    var synced = Field(row, "syncedLyrics");
                    if (!string.IsNullOrWhiteSpace(synced) && score > bestTimedScore)
                    {
                        bestTimed = synced;
                        bestTimedScore = score;
                    }
                    var plain = Field(row, "plainLyrics");
                    if (plain is { Length: > 4 and <= 30000 } && score > bestPlainScore)
                    {
                        bestPlain = plain;
                        bestPlainScore = score;
                    }
                }
            }
            if (bestTimed != null) return bestTimed;
            onPlain?.Invoke(bestPlain ?? plainCandidate ?? "");
            return null;
        }

        internal static bool Accepts(string title, string artist, long durationMs,
            string candidateTitle, string candidateArtist, long candidateDurationMs)
        {
            if (title.Length == 0 || candidateTitle.Length == 0) return false;
            if (durationMs > 0 && candidateDurationMs > 0 &&
                Math.Abs(durationMs - candidateDurationMs) > 10000) return false;
            // Title only is ambiguous. With no artist, insist on a matching
            // recording length and canonical title before showing lyrics.
            if (string.IsNullOrWhiteSpace(artist))
                return durationMs > 0 && candidateDurationMs > 0 &&
                    Math.Abs(durationMs - candidateDurationMs) <= 5000 &&
                    string.Equals(AndroidLyricsMatch.CleanTitle(title),
                        AndroidLyricsMatch.CleanTitle(candidateTitle),
                        StringComparison.OrdinalIgnoreCase);
            return AndroidLyricsMatch.Score(title, artist, durationMs,
                candidateTitle, candidateArtist, candidateDurationMs) >= 0.62f;
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

// Restore the controller only, not the borderless second-screen projection.
// A monitor may be disconnected or its resolution/DPI may have changed since
// the last run, so saved coordinates are always clamped to a visible work area.
internal sealed class SavedControllerPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

internal static class ControllerWindowPlacement
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MelismaUniversal", "WindowsLyricsViewer", "window-placement.json");

    public static void Restore(Form window)
    {
        try
        {
            if (!File.Exists(PathName)) return;
            var saved = JsonSerializer.Deserialize<SavedControllerPlacement>(File.ReadAllText(PathName));
            if (saved == null || saved.Width < 825 || saved.Height < 590 ||
                saved.Width > 16000 || saved.Height > 16000) return;
            var wanted = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height);
            var screens = Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
            if (screens.Length == 0) return;
            Rectangle best = screens[0];
            long bestArea = 0;
            foreach (var area in screens)
            {
                var overlap = Rectangle.Intersect(wanted, area);
                long visible = Math.Max(0, overlap.Width) * (long)Math.Max(0, overlap.Height);
                if (visible <= bestArea) continue;
                bestArea = visible;
                best = area;
            }
            if (bestArea < 100 * 100)
                best = Screen.PrimaryScreen?.WorkingArea ?? screens[0];
            int width = Math.Min(Math.Max(saved.Width, window.MinimumSize.Width), best.Width);
            int height = Math.Min(Math.Max(saved.Height, window.MinimumSize.Height), best.Height);
            int x = bestArea < 100 * 100 ? best.Left + (best.Width - width) / 2
                : Math.Clamp(saved.X, best.Left, best.Right - width);
            int y = bestArea < 100 * 100 ? best.Top + (best.Height - height) / 2
                : Math.Clamp(saved.Y, best.Top, best.Bottom - height);
            window.StartPosition = FormStartPosition.Manual;
            window.Bounds = new Rectangle(x, y, width, height);
            if (saved.Maximized) window.WindowState = FormWindowState.Maximized;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or OverflowException)
        {
            // A corrupt or inaccessible optional profile must not block startup.
        }
    }

    public static void Save(Form window)
    {
        try
        {
            var bounds = window.WindowState == FormWindowState.Normal
                ? window.Bounds : window.RestoreBounds;
            if (bounds.Width < 825 || bounds.Height < 590) return;
            var value = new SavedControllerPlacement
            {
                X = bounds.X, Y = bounds.Y,
                Width = bounds.Width, Height = bounds.Height,
                Maximized = window.WindowState == FormWindowState.Maximized
            };
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            string temp = PathName + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value));
            File.Move(temp, PathName, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Continue closing normally even on a read-only Windows profile.
        }
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
    private string _lastPlainLyrics = "";
    private int _plainScrollLines;

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
        MouseWheel += (_, e) =>
        {
            _plainScrollLines = Math.Max(0, _plainScrollLines - Math.Sign(e.Delta) * 3);
            Invalidate();
        };
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
            if (!string.IsNullOrWhiteSpace(frame.PlainLyrics))
                DrawPlainLyrics(g, frame.PlainLyrics);
            else
            {
                DrawBouncingDots(g, Environment.TickCount64);
                string display = frame.Message.Contains("No matching", StringComparison.OrdinalIgnoreCase)
                    ? "No synchronized lyrics found" : "Looking for lyrics…";
                DrawMessage(g, display, Height * 0.58f, 20, Color.White, true);
                DrawMessage(g, frame.Message, Height * 0.67f, 12, Other, false);
            }
            g.Restore(textClip);
            DrawIdleTakeover(g);
            return;
        }

        // Android Melisma inserts animated three-dot interludes in the intro
        // and between vocal lines when there are at least 3000ms of silence.
        // These are computed from lyric timestamps (and hidden 500ms early),
        // so a seek or pause immediately lands on the correct visual state.
        if (LyricsInterlude.At(frame.Lines, frame.LyricPositionMs) != null)
        {
            DrawBouncingDots(g, frame.LyricPositionMs);
            g.Restore(textClip);
            DrawSourceCredit(g, frame);
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
            // Paint the animated word ONCE. v0.6.6 first painted the
            // complete line and then painted the larger word over itself,
            // creating the visibly doubled/overlapping letters in Edamame.
            // Exclude the active word from the normal row paint, then
            // render that one word at its measured position.
            // Do not delay the beginning of word growth until 70% of the
            // line scroll animation: that gate suddenly switched from the
            // base font to the enlarged overlay mid-word (a visible pop).
            AnimatedWord? animated = _display.GrowActiveWord &&
                index == active
                    ? LocateAnimatedWord(layout, line, frame.LyricPositionMs)
                    : null;
            DrawLayout(g, layout, y, playedCharacters.HasValue ? Other : color,
                playedCharacters, emphasis, edgeOpacity, animated);
            if (animated.HasValue)
                DrawActiveWordOverlay(g, layout, y, line,
                    animated.Value, emphasis, edgeOpacity);
        }
        g.Restore(textClip);
        DrawSourceCredit(g, frame);
        DrawIdleTakeover(g);
    }

    private void DrawPlainLyrics(Graphics g, string plain)
    {
        // A real Android fallback: static text is displayed without
        // inventing any lyric timing. Mouse wheel scrolls through lines.
        if (_lastPlainLyrics != plain)
        {
            _lastPlainLyrics = plain;
            _plainScrollLines = 0;
        }
        var area = LyricsArea;
        var rect = RectangleF.Inflate(area, -Math.Min(38f, area.Width * 0.07f), -12f);
        float points = Math.Clamp(rect.Width / 37f, 14f, 24f) * _display.FontScale;
        using var heading = new Font("Segoe UI", Math.Clamp(points * 0.65f, 10f, 15f),
            FontStyle.Bold);
        using var font = new Font(_display.FontFamily, points, FontStyle.Regular);
        using var textBrush = new SolidBrush(Color.FromArgb(233, 226, 240, 245));
        using var headingBrush = new SolidBrush(Highlight);
        g.DrawString("PLAIN LYRICS · NOT SYNCHRONIZED · SCROLL WITH MOUSE WHEEL",
            heading, headingBrush, new RectangleF(rect.Left, rect.Top, rect.Width, 35f));
        string[] rows = plain.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        float top = rect.Top + 37f;
        float lineHeight = Math.Max(23f, font.GetHeight(g) * 1.27f);
        int showing = Math.Max(1, (int)((rect.Bottom - top) / lineHeight));
        _plainScrollLines = Math.Clamp(_plainScrollLines, 0, Math.Max(0, rows.Length - showing));
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        for (int i = _plainScrollLines;
            i < Math.Min(rows.Length, _plainScrollLines + showing); i++)
            g.DrawString(rows[i].Trim(), font, textBrush,
                new RectangleF(rect.Left, top + (i - _plainScrollLines) * lineHeight,
                    rect.Width, lineHeight), format);
    }

    private void DrawBouncingDots(Graphics g, long clockMs)
    {
        // Three round dots rise in sequence. The animation is subtle enough
        // for a TV, yet legible in the controller's smaller preview.
        var area = LyricsArea;
        float radius = Math.Clamp(area.Width / 90f, 4.5f, 10.5f);
        float separation = radius * 3.5f;
        float left = area.Left + area.Width * 0.5f - separation;
        float middle = area.Top + area.Height * 0.5f;
        float phase = (float)((clockMs % 1800 + 1800) % 1800) / 1800f;
        for (int i = 0; i < 3; i++)
        {
            float wave = (1f - MathF.Cos(2f * MathF.PI * (phase - i * 0.18f))) * 0.5f;
            wave *= wave * (3f - 2f * wave);
            float size = radius * (0.83f + 0.23f * wave);
            float y = middle - radius * 1.35f * wave;
            int alpha = (int)(115f + 140f * wave);
            using var brush = new SolidBrush(Color.FromArgb(alpha, Highlight));
            g.FillEllipse(brush, left + separation * i - size, y - size,
                size * 2f, size * 2f);
        }
    }

    private void DrawSourceCredit(Graphics g, LyricFrame frame)
    {
        // lrc.red requires visible attribution and a link to its source.
        if (!frame.Source.StartsWith("lrc.red", StringComparison.OrdinalIgnoreCase)) return;
        using var font = new Font("Segoe UI", 9.5f);
        using var brush = new SolidBrush(Color.FromArgb(210, 216, 225, 236));
        using var align = new StringFormat { Alignment = StringAlignment.Center };
        var area = LyricsArea;
        g.DrawString("Lyrics: lrc.red · https://lrc.red",
            font, brush, new RectangleF(area.Left, Height - 27f,
                area.Width, 22f), align);
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

        var current = _artwork.Enabled ? _artwork.Current : null;
        var previous = _artwork.Enabled ? _artwork.Previous : null;
        float progress = _artwork.Progress;
        // The palette must transition too: sampling only the incoming cover
        // switched colours abruptly on the very first frame.
        DrawPalette(g, previous, current, progress,
            style is MelismaBackgroundStyle.Living
                or MelismaBackgroundStyle.LivingClassic, layerOpacity);

        if (style is MelismaBackgroundStyle.Colour
            or MelismaBackgroundStyle.LivingClassic) return;

        if (!_artwork.Enabled) return;

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

    private void DrawPalette(Graphics g, Image? previous, Image? current,
        float progress, bool moving, float layerOpacity)
    {
        var one = Blend(
            Sample(previous, 0.24f, 0.28f, Color.FromArgb(42, 73, 107)),
            Sample(current, 0.24f, 0.28f, Color.FromArgb(42, 73, 107)),
            previous == null && current == null ? 1f : progress);
        var two = Blend(
            Sample(previous, 0.75f, 0.68f, Color.FromArgb(91, 36, 99)),
            Sample(current, 0.75f, 0.68f, Color.FromArgb(91, 36, 99)),
            previous == null && current == null ? 1f : progress);
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
            float fade = _artwork.PanelProgress; // User-selected cover transition.
            // Keep the outgoing cover fully opaque underneath the incoming
            // one. Fading both against a dark backing made the old cover
            // appear to disappear before the replacement was visible.
            if (_artwork.Previous != null && fade < 1f)
                DrawCover(g, _artwork.Previous,
                    CoverFade.OldOpacity(fade, _artwork.Current != null), coverBox);
            if (_artwork.Current != null && fade > 0f)
                DrawCover(g, _artwork.Current, CoverFade.NewOpacity(fade), coverBox);
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

    private readonly record struct AnimatedWord(
        int RowIndex, int Start, int End, int WordIndex,
        ActiveWordPulse.Focus Pulse);

    private AnimatedWord? LocateAnimatedWord(
        TextLayout layout, LrcLine line, long playbackMs)
    {
        var focus = ActiveWordPulse.At(line.Words, playbackMs,
            _display.SimpleMode);
        if (!focus.HasValue ||
            line.Words is not { Count: > 0 } words ||
            string.Concat(words.Select(w => w.Text)) != line.Text)
            return null;

        int index = focus.Value.WordIndex;
        string rawWord = words[index].Text;
        string visible = rawWord.Trim();
        if (visible.Length == 0) return null;

        int prefix = 0;
        for (int i = 0; i < index; i++) prefix += words[i].Text.Length;
        int start = prefix + (rawWord.Length - rawWord.TrimStart().Length);
        int end = start + visible.Length;
        for (int row = 0; row < layout.Rows.Count; row++)
        {
            var visual = layout.Rows[row];
            if (start >= visual.Start && end <= visual.Start + visual.Text.Length)
                return new AnimatedWord(row, start, end, index, focus.Value);
        }
        return null;
    }

    private void DrawActiveWordOverlay(Graphics g, TextLayout layout,
        float centerY, LrcLine line, AnimatedWord active,
        float emphasis, float opacity)
    {
        if (line.Words is not { Count: > 0 } words) return;
        string wordText = words[active.WordIndex].Text.Trim();
        if (wordText.Length == 0) return;
        var visualRow = layout.Rows[active.RowIndex];

        float fontFactor = _display.SimpleMode
            ? 0.84f + 0.16f * Math.Clamp(emphasis, 0f, 1f)
            : 0.67f + 0.33f * Math.Clamp(emphasis, 0f, 1f);
        using var baseFont = new Font(_display.FontFamily,
            layout.Font.SizeInPoints * fontFactor,
            emphasis >= 0.5f ? FontStyle.Bold : FontStyle.Regular,
            GraphicsUnit.Point);
        using var grownFont = new Font(_display.FontFamily,
            baseFont.SizeInPoints * active.Pulse.Scale,
            baseFont.Style, GraphicsUnit.Point);

        float top = centerY - layout.TotalHeight / 2f;
        float width = Math.Max(1f, LyricsArea.Width - 2f * SidePadding);
        var rect = new RectangleF(LyricsArea.Left + SidePadding,
            top + active.RowIndex * layout.RowHeight, width, layout.RowHeight);

        // Compute the word boundaries from the SAME measured full row that
        // the normal lyric renderer uses, not from an independently measured
        // word's width; otherwise kerning errors make the word drift.
        int begin = active.Start - visualRow.Start;
        int finish = active.End - visualRow.Start;
        float fullWidth = TextWidth(g, visualRow.Text, baseFont);
        float beforeWidth = TextWidth(g, visualRow.Text[..begin], baseFont);
        float throughWidth = TextWidth(g, visualRow.Text[..finish], baseFont);
        float originalLeft = rect.Left + (rect.Width - fullWidth) / 2f;
        float wordCenterX = originalLeft + (beforeWidth + throughWidth) / 2f;
        float grownWidth = TextWidth(g, wordText, grownFont);
        float lifted = active.Pulse.LiftEm * baseFont.GetHeight(g);
        var grownRect = new RectangleF(
            wordCenterX - grownWidth / 2f, rect.Top - lifted,
            grownWidth + 5f, rect.Height + 2f);

        using var typographic = new StringFormat(
            StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        opacity = Math.Clamp(opacity, 0f, 1f);
        using var shadow = new SolidBrush(Color.FromArgb(
            (int)(90f * opacity), 5, 9, 18));
        using var resting = new SolidBrush(Color.FromArgb(
            (int)(230f * opacity), Other));
        using var singing = new SolidBrush(Color.FromArgb(
            (int)(255f * opacity), Highlight));

        // The full baseline word has been masked out in DrawLayout, so
        // there is exactly one rendered copy of the active word.
        var shadowRect = grownRect;
        shadowRect.Offset(0.5f, 0.8f);
        g.DrawString(wordText, grownFont, shadow, shadowRect, typographic);
        g.DrawString(wordText, grownFont, resting, grownRect, typographic);

        float litFraction = _display.SmoothWordFill &&
            words[active.WordIndex].EndMs > words[active.WordIndex].AtMs
                ? active.Pulse.Progress : 1f;
        if (litFraction > 0f)
        {
            var saved = g.Save();
            g.SetClip(new RectangleF(grownRect.Left - 1f,
                grownRect.Top - 2f,
                grownWidth * litFraction + 2f,
                grownRect.Height + 4f), CombineMode.Intersect);
            g.DrawString(wordText, grownFont, singing, grownRect, typographic);
            g.Restore(saved);
        }
    }

    private void DrawLayout(Graphics g, TextLayout layout,
        float centerY, Color color, float? playedCharacters, float emphasis,
        float opacity = 1f, AnimatedWord? omitWord = null)
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

            // With animated word emphasis, blank its original ink area in
            // both base and cyan layers BEFORE painting the enlarged word.
            // The actual backdrop stays intact; only the row's text paint
            // is clipped. This prevents the v0.6.6 double-glyph bug.
            GraphicsState? originalWordMask = null;
            if (omitWord is { } omitted && omitted.RowIndex == i)
            {
                int startChar = omitted.Start - row.Start;
                int endChar = omitted.End - row.Start;
                if (startChar >= 0 && endChar <= row.Text.Length &&
                    endChar > startChar)
                {
                    float rowWidth = TextWidth(g, row.Text, displayFont);
                    float rowLeft = rect.Left + (rect.Width - rowWidth) / 2f;
                    float xStart = TextWidth(g, row.Text[..startChar], displayFont);
                    float xEnd = TextWidth(g, row.Text[..endChar], displayFont);
                    originalWordMask = g.Save();
                    g.SetClip(new RectangleF(rowLeft + xStart - 1.5f,
                        rect.Top - 5f,
                        Math.Max(2f, xEnd - xStart + 3f),
                        rect.Height + 10f), CombineMode.Exclude);
                }
            }
            try
            {
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
            finally
            {
                if (originalWordMask != null)
                    g.Restore(originalWordMask);
            }
        }
    }
}

internal sealed class KaraokeDisplay : Form
{
    private readonly LyricsCanvas _canvas;
    // Driven by the controller's adaptive render clock, not a second timer.

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
        // One refresh per controller tick, avoiding duplicate TV paints.
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
