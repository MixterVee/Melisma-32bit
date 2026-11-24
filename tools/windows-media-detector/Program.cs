using System.Diagnostics;
using System.Globalization;
using System.Text;
using Windows.Media.Control;

namespace MelismaMediaDetector;

/// <summary>
/// Read-only proof of concept: inspects Foobar2000 media metadata and timeline
/// using Windows Global System Media Transport Controls (GSMTC).
/// No player commands, plugins, registry writes or audio interception.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DetectorForm());
    }
}

internal sealed class DetectorForm : Form
{
    private static readonly Color Background = Color.FromArgb(15, 20, 32);
    private static readonly Color Card = Color.FromArgb(25, 34, 50);
    private static readonly Color Ink = Color.FromArgb(237, 244, 251);
    private static readonly Color Dim = Color.FromArgb(160, 177, 194);
    private static readonly Color Accent = Color.FromArgb(80, 220, 219);

    private readonly Label _status = TextLabel(11, Accent);
    private readonly Label _track = TextLabel(20, Ink, true);
    private readonly Label _artist = TextLabel(12, Dim);
    private readonly Label _app = TextLabel(10, Dim);
    private readonly Label _raw = TextLabel(21, Ink, true);
    private readonly Label _estimated = TextLabel(21, Accent, true);
    private readonly Label _duration = TextLabel(10, Dim);
    private readonly Label _freshness = TextLabel(10, Dim);
    private readonly Label _samples = TextLabel(10, Dim);
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 9, Maximum = 1000, Style = ProgressBarStyle.Continuous };
    private readonly TextBox _events = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Card, ForeColor = Ink,
        BorderStyle = BorderStyle.None,
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9.5f),
    };
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 250 };
    private readonly List<Sample> _recorded = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string _activeApp = "";
    private string _lastTitle = "";
    private string _lastArtist = "";
    private DateTimeOffset _lastMediaRead = DateTimeOffset.MinValue;
    private TimeSpan _lastRaw = TimeSpan.MinValue;
    private int _rawChanges;
    private bool _busy;
    private string _lastError = "";

    public DetectorForm()
    {
        Text = "Melisma • Windows Media Detector — Foobar2000";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(880, 700);
        MinimumSize = new Size(740, 590);
        BackColor = Background;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 17, 20, 14),
            ColumnCount = 1,
            RowCount = 6,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 167));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var titleBlock = new Panel { Dock = DockStyle.Fill };
        var header = TextLabel(21, Ink, true);
        header.Text = "Melisma  /  Media Detector";
        header.SetBounds(0, 0, 760, 36);
        var sub = TextLabel(10, Dim);
        sub.Text = "Windows karaoke proof of concept · Read-only monitoring · Foobar2000";
        sub.SetBounds(0, 41, 770, 22);
        titleBlock.Controls.AddRange([header, sub]);
        root.Controls.Add(titleBlock, 0, 0);
        
        _status.Text = "Connecting to Windows media sessions...";
        _status.Dock = DockStyle.Fill;
        root.Controls.Add(_status, 0, 1);
        
        var songCard = PanelCard();
        var songGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        songGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        songGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        songGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _track.Text = "Waiting for Foobar2000...";
        _artist.Text = "Play an MP3 in Foobar2000";
        _app.Text = "Source application: —";
        _track.Dock = _artist.Dock = _app.Dock = DockStyle.Fill;
        songGrid.Controls.Add(_track, 0, 0);
        songGrid.Controls.Add(_artist, 0, 1);
        songGrid.Controls.Add(_app, 0, 2);
        songCard.Controls.Add(songGrid);
        root.Controls.Add(songCard, 0, 2);

        var timelineCard = PanelCard();
        var timelineGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        timelineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        timelineGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        timelineGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        timelineGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        timelineGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        timelineGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        timelineGrid.Controls.Add(Cell("POSITION REPORTED BY WINDOWS"), 0, 0);
        timelineGrid.Controls.Add(Cell("ESTIMATED LIVE POSITION"), 1, 0);
        _raw.Dock = _estimated.Dock = DockStyle.Fill;
        _raw.Text = _estimated.Text = "00:00.000";
        timelineGrid.Controls.Add(_raw, 0, 1);
        timelineGrid.Controls.Add(_estimated, 1, 1);
        _duration.Text = "Track duration: —";
        _freshness.Text = "Timeline update age: —";
        _duration.Dock = _freshness.Dock = DockStyle.Fill;
        timelineGrid.Controls.Add(_duration, 0, 2);
        timelineGrid.Controls.Add(_freshness, 1, 2);
        var barSpace = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 10, 9, 3) };
        barSpace.Controls.Add(_progress);
        timelineGrid.Controls.Add(barSpace, 0, 3);
        timelineGrid.SetColumnSpan(barSpace, 2);
        timelineCard.Controls.Add(timelineGrid);
        root.Controls.Add(timelineCard, 0, 3);

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        _samples.Dock = DockStyle.Fill;
        _samples.Text = "Position samples: 0";
        actionRow.Controls.Add(_samples, 0, 0);
        var save = Button("Save CSV", Color.FromArgb(40, 83, 97));
        save.Click += SaveCsv;
        actionRow.Controls.Add(save, 1, 0);
        var copy = Button("Copy status", Color.FromArgb(45, 65, 87));
        copy.Click += (_, _) => Clipboard.SetText(
            $"{_track.Text}\r\n{_artist.Text}\r\n{_app.Text}\r\n{_status.Text}\r\n{_raw.Text} (reported)\r\n{_estimated.Text} (estimated)\r\n{_freshness.Text}\r\n{_samples.Text}");
        actionRow.Controls.Add(copy, 2, 0);
        root.Controls.Add(actionRow, 0, 4);

        var logCard = PanelCard();
        logCard.Padding = new Padding(12);
        logCard.Controls.Add(_events);
        root.Controls.Add(logCard, 0, 5);
        AddLog("Ready. Monitoring Windows media sessions at 250 ms intervals.");
        AddLog("Play, pause, seek and skip in Foobar2000. Then save a CSV if timing looks irregular.");
        AddLog("The estimated clock is extrapolated; it is NOT proof of millisecond accuracy.");

        Shown += async (_, _) => await StartAsync();
        FormClosed += (_, _) => _pollTimer.Dispose();
        _pollTimer.Tick += async (_, _) => await PollAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _status.Text = "Windows media-session access available. Looking for Foobar2000...";
            AddLog("Windows GSMTC manager initialized.");
            _pollTimer.Start();
            await PollAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "Unable to read Windows media sessions";
            AddLog("ERROR: " + ex.Message);
        }
    }

    private async Task PollAsync()
    {
        if (_busy || _manager == null || IsDisposed) return;
        _busy = true;
        try
        {
            // Prefer Foobar2000 even if Windows considers another application current.
            var session = _manager.GetSessions().FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains("foobar", StringComparison.OrdinalIgnoreCase));

            if (session == null)
            {
                if (_activeApp != "")
                {
                    AddLog("Foobar2000 media session disconnected.");
                    _activeApp = "";
                    _lastTitle = "";
                    _lastMediaRead = DateTimeOffset.MinValue;
                    _lastRaw = TimeSpan.MinValue;
                }
                _status.Text = "No Foobar2000 media session — start playing an MP3";
                _track.Text = "Waiting for Foobar2000...";
                _artist.Text = "Play an MP3 and verify it appears in Windows Quick Settings";
                _app.Text = "Source application: —";
                _duration.Text = "Track duration: —";
                _freshness.Text = "Timeline update age: —";
                _raw.Text = _estimated.Text = "00:00.000";
                _progress.Value = 0;
                return;
            }

            var appId = session.SourceAppUserModelId;
            if (_activeApp != appId)
            {
                _activeApp = appId;
                _lastMediaRead = DateTimeOffset.MinValue;
                _lastRaw = TimeSpan.MinValue;
                AddLog("Detected Foobar2000 session: " + appId);
            }

            var info = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var playing = info.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var age = timeline.LastUpdatedTime.Year > 2000
                ? DateTimeOffset.UtcNow - timeline.LastUpdatedTime
                : TimeSpan.Zero;
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            var position = timeline.Position;
            var length = timeline.EndTime > timeline.StartTime
                ? timeline.EndTime - timeline.StartTime
                : timeline.EndTime;
            if (length < TimeSpan.Zero) length = TimeSpan.Zero;
            var predicted = playing ? position + age : position;
            if (length > TimeSpan.Zero && predicted > length) predicted = length;

            _status.Text = playing ? "● Foobar2000 PLAYING" : $"● Foobar2000 {info.PlaybackStatus.ToString().ToUpperInvariant()}";
            _raw.Text = Clock(position);
            _estimated.Text = Clock(predicted);
            _duration.Text = "Track duration: " + (length > TimeSpan.Zero ? Clock(length) : "not reported");
            _freshness.Text = timeline.LastUpdatedTime.Year > 2000
                ? "Timeline age: " + age.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s"
                : "Timeline age: not reported";
            _progress.Value = length > TimeSpan.Zero
                ? Math.Clamp((int)(1000 * Math.Clamp(predicted.TotalMilliseconds / length.TotalMilliseconds, 0, 1)), 0, 1000)
                : 0;
            _app.Text = "Source application: " + appId;

            if (_lastRaw != position)
            {
                if (_lastRaw != TimeSpan.MinValue)
                {
                    _rawChanges++;
                    var diff = (position - _lastRaw).TotalMilliseconds;
                    if (diff < -250 || diff > 2500)
                        AddLog($"Timeline jump {diff:+0;-0;0} ms (seek/track change or delayed report)");
                }
                _lastRaw = position;
            }

            if (DateTimeOffset.UtcNow - _lastMediaRead >= TimeSpan.FromMilliseconds(1250))
            {
                _lastMediaRead = DateTimeOffset.UtcNow;
                var properties = await session.TryGetMediaPropertiesAsync();
                var title = properties?.Title ?? "";
                var artist = properties?.Artist ?? "";
                if (title != _lastTitle || artist != _lastArtist)
                {
                    _lastTitle = title;
                    _lastArtist = artist;
                    AddLog($"Track: {title}  —  {artist}");
                }
                _track.Text = string.IsNullOrWhiteSpace(title) ? "(Title unavailable)" : title;
                _artist.Text = string.IsNullOrWhiteSpace(artist) ? "(Artist unavailable)" : artist;
            }

            _recorded.Add(new Sample(
                DateTimeOffset.Now, appId, _lastTitle, _lastArtist,
                info.PlaybackStatus.ToString(),
                position.TotalMilliseconds, predicted.TotalMilliseconds,
                length.TotalMilliseconds, age.TotalMilliseconds));
            if (_recorded.Count > 20000) _recorded.RemoveRange(0, 5000);
            _samples.Text = $"Position samples: {_recorded.Count:N0}  ·  Reported position changes: {_rawChanges:N0}";
            _lastError = "";
        }
        catch (Exception ex)
        {
            // Media session can disappear during a song switch; retry next poll.
            _status.Text = "Media session temporarily unavailable (retrying)";
            if (ex.Message != _lastError)
            {
                _lastError = ex.Message;
                AddLog("Session read: " + ex.Message);
            }
        }
        finally { _busy = false; }
    }

    private void SaveCsv(object? sender, EventArgs e)
    {
        if (_recorded.Count == 0)
        {
            MessageBox.Show(this, "Play a song first so there are timing samples to export.", "Melisma", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "Save Foobar2000 timing diagnostics",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "Melisma-Foobar2000-timing.csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
        writer.WriteLine("LocalTime,AppId,Title,Artist,PlaybackStatus,ReportedPositionMs,EstimatedPositionMs,DurationMs,TimelineAgeMs");
        foreach (var x in _recorded)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                Csv(x.At.ToString("O")), Csv(x.AppId), Csv(x.Title), Csv(x.Artist),
                Csv(x.State), Num(x.ReportedMs), Num(x.EstimatedMs),
                Num(x.DurationMs), Num(x.AgeMs),
            }));
        }
        AddLog("Timing CSV saved: " + dialog.FileName);
    }

    private static string Num(double value) => value.ToString("F0", CultureInfo.InvariantCulture);
    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    private static string Clock(TimeSpan value) =>
        $"{(int)value.TotalMinutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";

    private void AddLog(string text)
    {
        _events.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }

    private static Panel PanelCard() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Card,
        Padding = new Padding(16, 9, 16, 7),
        Margin = new Padding(0, 4, 0, 7),
    };
    private static Label TextLabel(int pt, Color color, bool bold = false) => new()
    {
        Font = new Font("Segoe UI", pt, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color, BackColor = Color.Transparent,
        AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
    };
    private static Label Cell(string text)
    {
        var label = TextLabel(9, Dim, true);
        label.Text = text;
        label.Dock = DockStyle.Fill;
        return label;
    }
    private static Button Button(string text, Color bg) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        BackColor = bg,
        ForeColor = Ink,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(5, 3, 0, 3),
        Cursor = Cursors.Hand,
    };

    private record Sample(
        DateTimeOffset At, string AppId, string Title, string Artist, string State,
        double ReportedMs, double EstimatedMs, double DurationMs, double AgeMs);
}
