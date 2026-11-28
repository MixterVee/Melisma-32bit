namespace MelismaLyricsViewer;

/// <summary>
/// Foobar remains the audio engine. This form asks a real installed
/// Stem Separator v2.8.3 for its current mode/levels and never invents an
/// audio change. It never reads/modifies MP3s, track time or lyric offsets.
/// </summary>
internal sealed class KaraokeControlPanel : Form
{
    private readonly StemKaraokeBridge _stem = new();
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 250 };
    private readonly Func<(string Song, string Artist, bool Playing)> _playing;
    private readonly Label _songLabel = TextLabel(12.5f, Color.White, true);
    private readonly Label _statusLabel = TextLabel(10f, Color.LightSkyBlue);
    private readonly Label _vocalLabel = TextLabel(11f, Color.White);
    private readonly Label _musicLabel = TextLabel(11f, Color.White);
    private readonly Label _hintLabel = TextLabel(9.4f,
        Color.FromArgb(179, 192, 210));
    private readonly TrackBar _vocals = LevelSlider();
    private readonly TrackBar _instrumental = LevelSlider();
    private readonly Dictionary<KaraokeStemMode, Button> _buttons = new();
    private StemKaraokeSnapshot? _snapshot;
    private bool _updating;
    private int _pendingSequence;
    private long _requestTick;
    private bool _showRequestError;

    internal KaraokeControlPanel(
        Func<(string Song, string Artist, bool Playing)> playing)
    {
        _playing = playing;
        Text = "Melisma Universal · Karaoke Studio (test)";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);
        ForeColor = Color.White;
        BackColor = Color.FromArgb(14, 21, 35);
        MinimumSize = new Size(645, 530);
        ClientSize = new Size(730, 565);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7,
            Padding = new Padding(22, 20, 22, 18)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 87));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 87));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(root);

        var heading = TextLabel(20f, Color.FromArgb(113, 236, 227), true);
        heading.Text = "Karaoke Studio";
        root.Controls.Add(heading, 0, 0);

        var identity = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = new Padding(0)
        };
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _songLabel.Text = "Foobar2000 · No song playing";
        identity.Controls.Add(_songLabel, 0, 0);
        _statusLabel.Text = "Checking Stem Separator…";
        identity.Controls.Add(_statusLabel, 0, 1);
        root.Controls.Add(identity, 0, 1);

        var modeButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true
        };
        AddModeButton(modeButtons, "Original", KaraokeStemMode.Original);
        AddModeButton(modeButtons, "Instrumental", KaraokeStemMode.Instrumental);
        AddModeButton(modeButtons, "Vocals only", KaraokeStemMode.Vocals);
        AddModeButton(modeButtons, "Custom blend", KaraokeStemMode.Blend);
        root.Controls.Add(modeButtons, 0, 2);

        root.Controls.Add(CreateSliderRow("Original singer", _vocals,
            _vocalLabel), 0, 3);
        root.Controls.Add(CreateSliderRow("Backing music", _instrumental,
            _musicLabel), 0, 4);

        _hintLabel.Text =
            "The vocal slider controls Stem Separator's separated vocal stem, " +
            "not the microphone volume. Selecting a slider activates Blend.\n\n" +
            "You must have Stem Separator (ONNX) in Foobar2000's active DSP " +
            "chain: Preferences → Playback → DSP Manager. " +
            "Instrumental preparation can take a moment on a new track.\n\n" +
            "No MP3 edits, audio capture or microphone routing. " +
            "Foobar Bridge v0.4 and your current TV receiver stay unchanged.";
        _hintLabel.AutoEllipsis = false;
        root.Controls.Add(_hintLabel, 0, 5);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft
        };
        var close = new Button
        {
            Text = "Close", Width = 105, Height = 39,
            ForeColor = Color.White, BackColor = Color.FromArgb(42, 60, 84)
        };
        close.Click += (_, _) => Close();
        footer.Controls.Add(close);
        root.Controls.Add(footer, 0, 6);

        _vocals.ValueChanged += (_, _) => UpdateValues();
        _instrumental.ValueChanged += (_, _) => UpdateValues();
        _vocals.MouseUp += (_, _) => CommitBlend();
        _instrumental.MouseUp += (_, _) => CommitBlend();
        _vocals.KeyUp += (_, _) => CommitBlend();
        _instrumental.KeyUp += (_, _) => CommitBlend();

        _refresh.Tick += (_, _) => RefreshStatus();
        Shown += (_, _) => { RefreshStatus(); _refresh.Start(); };
        FormClosed += (_, _) => { _refresh.Stop(); _refresh.Dispose(); _stem.Dispose(); };
    }

    private static TrackBar LevelSlider() => new()
    {
        Minimum = 0, Maximum = 100, Value = 100,
        SmallChange = 5, LargeChange = 10, TickFrequency = 10,
        Dock = DockStyle.Fill,
        TickStyle = TickStyle.BottomRight,
        Margin = new Padding(0, 0, 10, 0)
    };

    private static Label TextLabel(float pt, Color color, bool bold = false) =>
        new()
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", pt,
                bold ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = color
        };

    private static Control CreateSliderRow(string caption, TrackBar slider,
        Label percentage)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Margin = new Padding(0)
        };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 85));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        var captionLabel = TextLabel(11f,
            Color.FromArgb(218, 229, 244), true);
        captionLabel.Text = caption;
        row.Controls.Add(captionLabel, 0, 0);
        percentage.TextAlign = ContentAlignment.MiddleRight;
        row.Controls.Add(percentage, 1, 0);
        row.Controls.Add(slider, 0, 1);
        row.SetColumnSpan(slider, 2);
        return row;
    }

    private void AddModeButton(Control container, string text,
        KaraokeStemMode mode)
    {
        var button = new Button
        {
            Text = text,
            Width = mode == KaraokeStemMode.Instrumental ? 143 : 124,
            Height = 42,
            Margin = new Padding(0, 3, 9, 3),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(41, 60, 87),
            FlatStyle = FlatStyle.Flat
        };
        button.Click += (_, _) => RequestMode(mode);
        _buttons[mode] = button;
        container.Controls.Add(button);
    }

    private void UpdateValues()
    {
        _vocalLabel.Text = $"{_vocals.Value}%";
        _musicLabel.Text = $"{_instrumental.Value}%";
    }

    private void RequestMode(KaraokeStemMode mode)
    {
        if (_updating) return;
        var (song, _, playing) = _playing();
        if (!_stem.TryRead(out var status) || status == null ||
            (playing && !status.DspActive))
        {
            _statusLabel.Text =
                "Stem Separator DSP is not available. Check Active DSPs in Foobar.";
            return;
        }

        int vocals = _vocals.Value;
        int music = _instrumental.Value;
        // Full instrumental mode does NOT permanently erase the user's
        // chosen vocal balance; moving the vocal slider restores Blend.
        bool sent = _stem.Request(mode, vocals, music, out int sequence);
        if (!sent)
        {
            _showRequestError = true;
            _statusLabel.Text = "Command not delivered. Check Foobar is running.";
            return;
        }
        _showRequestError = false;
        _pendingSequence = sequence;
        _requestTick = Environment.TickCount64;
        _statusLabel.Text = $"Applying {mode} to Foobar2000…";
    }

    private void CommitBlend()
    {
        if (!_updating) RequestMode(KaraokeStemMode.Blend);
    }

    private void RefreshStatus()
    {
        var (song, artist, playing) = _playing();
        _songLabel.Text = string.IsNullOrWhiteSpace(song)
            ? "Foobar2000 · No current song"
            : song + (string.IsNullOrWhiteSpace(artist) ? "" : "  ·  " + artist);

        if (!_stem.TryRead(out var status) || status == null)
        {
            _snapshot = null;
            EnableModes(false);
            _vocals.Enabled = _instrumental.Enabled = false;
            _statusLabel.Text = "Stem Separator karaoke bridge unavailable. " +
                "Install test component v2.8.3 in Foobar2000 and restart.";
            return;
        }
        _snapshot = status;
        bool dspReady = !playing || status.DspActive;
        bool pending = _pendingSequence > 0 &&
            status.AppliedSequence != _pendingSequence &&
            Environment.TickCount64 - _requestTick < 2200;
        if (_pendingSequence > 0 &&
            status.AppliedSequence == _pendingSequence)
            _pendingSequence = 0;

        EnableModes(dspReady && !pending);
        _vocals.Enabled = _instrumental.Enabled = dspReady && !pending;
        foreach (var item in _buttons)
            item.Value.BackColor = item.Key == status.Mode
                ? Color.FromArgb(27, 124, 126)
                : Color.FromArgb(41, 60, 87);

        if (!pending)
        {
            _updating = true;
            try
            {
                if (!_vocals.Capture && !_vocals.Focused)
                    _vocals.Value = status.VocalPercent;
                if (!_instrumental.Capture && !_instrumental.Focused)
                    _instrumental.Value = status.InstrumentalPercent;
                UpdateValues();
            }
            finally { _updating = false; }
        }

        if (_showRequestError)
        {
            _statusLabel.Text = "Command was not delivered · Check Foobar.";
        }
        else if (pending)
        {
            _statusLabel.Text = "Applying audio change…";
        }
        else if (_pendingSequence > 0 &&
            status.AppliedSequence != _pendingSequence)
        {
            _statusLabel.Text = "Foobar did not acknowledge the audio change.";
            _pendingSequence = 0;
        }
        else if (playing && !status.DspActive)
        {
            _statusLabel.Text = "Stem Separator loaded, but DSP is not processing! " +
                "Enable it under Playback → DSP Manager → Active DSPs.";
        }
        else
        {
            _statusLabel.Text = $"{status.Description} · " +
                $"Vocals {status.VocalPercent}% / Music {status.InstrumentalPercent}% " +
                (status.DspActive ? "· DSP active" :
                    "· DSP activity not yet confirmed (play a song)");
        }
    }

    private void EnableModes(bool enabled)
    {
        foreach (var button in _buttons.Values) button.Enabled = enabled;
    }
}
