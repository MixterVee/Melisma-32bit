using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelismaLyricsViewer;

// These names mirror the Android SettingsStore choices. The Windows renderer
// implements an initial desktop version of each style, rather than claiming
// the original Compose/OpenGL shader implementation has been ported.
internal enum MelismaViewMode { Lyrics, Cinema }
internal enum ArtworkPanelSide { Left, Right }
internal enum MelismaBackgroundStyle
{
    Living, LivingClassic, Auto, CoverArt, Artist, Colour, Black
}
internal enum MelismaLyricsFont { System, Sans, Condensed, Serif, Mono }

internal sealed class MelismaDisplaySettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string PathOnDisk = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MelismaUniversal", "WindowsLyricsViewer", "display-settings.json");

    public MelismaViewMode Layout { get; set; } = MelismaViewMode.Lyrics;
    public ArtworkPanelSide ArtworkSide { get; set; } = ArtworkPanelSide.Left;
    // Defaults intentionally match the previous Windows v0.4.2 presentation.
    public MelismaBackgroundStyle Background { get; set; } = MelismaBackgroundStyle.CoverArt;
    public int CoverBlur { get; set; } = 0;
    // After a meaningful pause or when no track is playing, temporarily
    // show Living without overwriting the user's saved background style.
    public bool IdleLivingEnabled { get; set; } = true;
    public int IdleLivingDelaySeconds { get; set; } = 12;
    public float IdleLivingFadeSeconds { get; set; } = 6f;
    public float FontScale { get; set; } = 1f;
    public MelismaLyricsFont Font { get; set; } = MelismaLyricsFont.System;
    public bool DepthBlur { get; set; } = false;
    public bool SimpleMode { get; set; } = false;
    public bool MinimalMode { get; set; } = false;

    public string FontFamily => Font switch
    {
        MelismaLyricsFont.Condensed => "Arial Narrow",
        MelismaLyricsFont.Serif => "Georgia",
        MelismaLyricsFont.Mono => "Consolas",
        MelismaLyricsFont.Sans => "Arial",
        _ => "Segoe UI"
    };

    public static MelismaDisplaySettings Load()
    {
        try
        {
            if (!File.Exists(PathOnDisk)) return new();
            var settings = JsonSerializer.Deserialize<MelismaDisplaySettings>(
                File.ReadAllText(PathOnDisk), Json) ?? new();
            settings.CoverBlur = Math.Clamp(settings.CoverBlur, 0, 67);
            settings.IdleLivingDelaySeconds = Math.Clamp(
                settings.IdleLivingDelaySeconds, 10, 15);
            settings.IdleLivingFadeSeconds = float.IsFinite(settings.IdleLivingFadeSeconds)
                ? Math.Clamp(settings.IdleLivingFadeSeconds, 2f, 12f) : 6f;
            settings.FontScale = float.IsFinite(settings.FontScale)
                ? Math.Clamp(settings.FontScale, 0.7f, 1.6f) : 1f;
            if (!Enum.IsDefined(settings.Layout)) settings.Layout = MelismaViewMode.Lyrics;
            if (!Enum.IsDefined(settings.ArtworkSide)) settings.ArtworkSide = ArtworkPanelSide.Left;
            if (!Enum.IsDefined(settings.Background)) settings.Background = MelismaBackgroundStyle.CoverArt;
            if (!Enum.IsDefined(settings.Font)) settings.Font = MelismaLyricsFont.System;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or JsonException or NotSupportedException)
        {
            // A corrupt or inaccessible optional settings file must never
            // interfere with the Foobar bridge or lyrics playback.
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
            var temp = PathOnDisk + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, PathOnDisk, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Continue using the updated preferences for this session.
        }
    }
}

internal sealed class MelismaDisplaySettingsForm : Form
{
    private readonly MelismaDisplaySettings _settings;
    private readonly AlbumArtworkState _artwork;
    private readonly Action _updated;

    public MelismaDisplaySettingsForm(
        MelismaDisplaySettings settings, AlbumArtworkState artwork, Action updated)
    {
        _settings = settings;
        _artwork = artwork;
        _updated = updated;
        Text = "Melisma Universal · Display settings";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(565, 530);
        MinimumSize = new Size(490, 455);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(17, 23, 39);
        ForeColor = Color.White;

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(16, 8),
            Font = new Font("Segoe UI", 10)
        };
        var view = Page("View");
        var background = Page("Background");
        var lyrics = Page("Lyrics display");
        tabs.TabPages.AddRange([view, background, lyrics]);
        Controls.Add(tabs);

        Choice(view, "Layout", ["Lyrics", "Cinema"],
            (int)settings.Layout, i => settings.Layout = (MelismaViewMode)i);
        Choice(view, "Artwork on the", ["Left", "Right"],
            (int)settings.ArtworkSide, i => settings.ArtworkSide = (ArtworkPanelSide)i);
        Toggle(view, "Show album art", artwork.Enabled,
            value => artwork.Enabled = value);
        Note(view, "Cinema shows an album-cover panel beside the words. " +
            "Lyrics uses the full screen. In landscape, Cinema reserves " +
            "42% of the width for artwork and 58% for lyrics.");

        Choice(background, "Style",
            ["Living", "Living (classic)", "Auto", "Cover art",
             "Artist (cover fallback)", "Colour", "Black"],
            (int)settings.Background,
            i => settings.Background = (MelismaBackgroundStyle)i);
        Number(background, "Cover blur", settings.CoverBlur, 0, 67, 1,
            0, value => settings.CoverBlur = (int)value, "0–67");
        Number(background, "Background transition", (decimal)artwork.FadeSeconds,
            1, 10, 0.2m, 1,
            value => artwork.FadeSeconds = (float)value, "seconds");
        Number(background, "Cover art transition", (decimal)artwork.CoverFadeSeconds,
            1, 10, 0.2m, 1,
            value => artwork.CoverFadeSeconds = (float)value, "seconds");
        Toggle(background, "Full-screen Living when idle", settings.IdleLivingEnabled,
            value => settings.IdleLivingEnabled = value);
        Number(background, "Idle Living delay", settings.IdleLivingDelaySeconds,
            10, 15, 1, 0,
            value => settings.IdleLivingDelaySeconds = (int)value, "seconds");
        Number(background, "Idle Living fade (in/out)", (decimal)settings.IdleLivingFadeSeconds,
            2, 12, 0.5m, 1,
            value => settings.IdleLivingFadeSeconds = (float)value, "seconds");
        Note(background,
            "Background transition changes the full-screen art; Cover art " +
            "transition changes the separate Cinema album cover, independently. " +
            "After 10–15 seconds without music, fluid Living covers the whole " +
            "lyrics display, including Cinema artwork and song information. " +
            "Lyrics remain synchronized underneath. Your one selected idle " +
            "fade duration applies both entering and leaving the screensaver. " +
            "Living (classic) remains a lightweight gradient effect; Living now " +
            "uses organically moving colour clouds. " +
            "based on the selected cover's colours. Auto currently selects Living. " +
            "Artist falls back to Cover art until a separate artist-image source is added. " +
            "Cover art may be unavailable if the player does not publish it.");

        Number(lyrics, "Text size", (decimal)settings.FontScale,
            0.7m, 1.6m, 0.1m, 1,
            value => settings.FontScale = (float)value, "×");
        Choice(lyrics, "Font", ["System", "Sans", "Condensed", "Serif", "Mono"],
            (int)settings.Font, i => settings.Font = (MelismaLyricsFont)i);
        Toggle(lyrics, "Depth effect on surrounding lines", settings.DepthBlur,
            value => settings.DepthBlur = value);
        Toggle(lyrics, "Simple mode (calmer emphasis)", settings.SimpleMode,
            value => settings.SimpleMode = value);
        Toggle(lyrics, "Minimal mode (hide sung lines)", settings.MinimalMode,
            value => settings.MinimalMode = value);
        Note(lyrics, "Text wrapping is always calculated before each lyric " +
            "becomes active. These options affect both the laptop preview and the TV.");

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 51,
            BackColor = Color.FromArgb(17, 23, 39) };
        var close = new Button
        {
            Text = "Close", Width = 112, Height = 33, Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat, ForeColor = Color.White,
            BackColor = Color.FromArgb(41, 60, 87),
            Margin = new Padding(8)
        };
        close.Click += (_, _) => Close();
        bottom.Padding = new Padding(0, 7, 16, 7);
        bottom.Controls.Add(close);
        Controls.Add(bottom);
        bottom.BringToFront();
    }

    private void Changed()
    {
        _settings.Save();
        _artwork.SaveOptions();
        _updated();
    }

    private static TabPage Page(string name)
    {
        var page = new TabPage(name)
        {
            BackColor = Color.FromArgb(23, 30, 47),
            ForeColor = Color.White,
            AutoScroll = true,
            Padding = new Padding(16)
        };
        var rows = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true,
            BackColor = page.BackColor
        };
        page.Controls.Add(rows);
        return page;
    }

    private static FlowLayoutPanel Rows(TabPage page) =>
        (FlowLayoutPanel)page.Controls[0];

    private static Label Caption(string text) => new()
    {
        Text = text, AutoSize = false, Width = 210, Height = 34,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.White,
        Margin = new Padding(0)
    };

    private static FlowLayoutPanel Row()
    {
        return new FlowLayoutPanel
        {
            Width = 478, Height = 45, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 3)
        };
    }

    private void Choice(TabPage page, string title, string[] values,
        int selected, Action<int> update)
    {
        var row = Row();
        row.Controls.Add(Caption(title));
        var combo = new ComboBox
        {
            Width = 245, Height = 34,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = Font, Margin = new Padding(4, 3, 0, 0)
        };
        combo.Items.AddRange(values);
        combo.SelectedIndex = Math.Clamp(selected, 0, values.Length - 1);
        combo.SelectedIndexChanged += (_, _) =>
        {
            update(combo.SelectedIndex);
            Changed();
        };
        row.Controls.Add(combo);
        Rows(page).Controls.Add(row);
    }

    private void Number(TabPage page, string title, decimal current,
        decimal minimum, decimal maximum, decimal increment, int decimals,
        Action<decimal> update, string units)
    {
        var row = Row();
        row.Controls.Add(Caption(title));
        var value = new NumericUpDown
        {
            Width = 115, Minimum = minimum, Maximum = maximum,
            DecimalPlaces = decimals, Increment = increment,
            Value = Math.Clamp(current, minimum, maximum),
            Font = Font, Margin = new Padding(4, 3, 8, 0)
        };
        value.ValueChanged += (_, _) =>
        {
            update(value.Value);
            Changed();
        };
        row.Controls.Add(value);
        var label = Caption(units);
        label.Width = 100;
        row.Controls.Add(label);
        Rows(page).Controls.Add(row);
    }

    private void Toggle(TabPage page, string title, bool checkedValue,
        Action<bool> update)
    {
        var check = new CheckBox
        {
            Text = title, Checked = checkedValue, Width = 470,
            Height = 38, ForeColor = Color.White,
            Margin = new Padding(0, 3, 0, 6)
        };
        check.CheckedChanged += (_, _) =>
        {
            update(check.Checked);
            Changed();
        };
        Rows(page).Controls.Add(check);
    }

    private static void Note(TabPage page, string description)
    {
        var label = new Label
        {
            Text = description, Width = 462, Height = 99,
            ForeColor = Color.FromArgb(177, 194, 214),
            Font = new Font("Segoe UI", 9),
            Margin = new Padding(0, 10, 0, 12)
        };
        Rows(page).Controls.Add(label);
    }
}
