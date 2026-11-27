using System.Net;
using System.Net.Sockets;

namespace MelismaLyricsViewer;

/// <summary>
/// Choose an installed Melisma Receiver on Shield / Onn / Google TV.
/// Broadcast discovery is best-effort; a TV IP fallback works when Wi-Fi
/// multicast is disabled. Pairing code is always shown on the TV itself.
/// </summary>
internal sealed class DirectTvPicker : Form
{
    private readonly ListBox _list = new()
    {
        Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11),
        BackColor = Color.FromArgb(27, 36, 56),
        ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly TextBox _ip = new() { Width = 185 };
    private readonly TextBox _code = new()
        { Width = 112, MaxLength = 6, TextAlign = HorizontalAlignment.Center };
    private readonly Label _status = new()
        { Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.LightSkyBlue };
    private readonly Button _connect = new() { Text = "Connect", Width = 100 };
    private readonly Button _search = new() { Text = "Scan again", Width = 120 };
    private readonly List<TvReceiverDevice> _devices = new();

    internal TvReceiverDevice? SelectedDevice { get; private set; }
    internal string PairingCode { get; private set; } = "";

    internal DirectTvPicker()
    {
        Text = "Melisma · Connect directly to Android TV";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(540, 360);
        BackColor = Color.FromArgb(17, 23, 39);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(18, 14, 18, 12)
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(main);

        var intro = new Label
        {
            Text = "Open Melisma Receiver on your TV first.\n" +
                "Choose its name below, then enter its six-digit pairing code.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(218, 227, 239)
        };
        main.Controls.Add(intro, 0, 0);
        main.Controls.Add(_list, 0, 1);

        var manual = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        manual.Controls.Add(new Label
        {
            Text = "TV IP (optional):", Width = 135, Height = 30,
            TextAlign = ContentAlignment.MiddleLeft
        });
        manual.Controls.Add(_ip);
        manual.Controls.Add(new Label
        {
            Text = "Code:", Width = 54, Height = 30,
            TextAlign = ContentAlignment.MiddleCenter
        });
        manual.Controls.Add(_code);
        main.Controls.Add(manual, 0, 2);
        main.Controls.Add(_status, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft
        };
        var cancel = new Button { Text = "Cancel", Width = 100 };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _connect.Click += (_, _) => Choose();
        _search.Click += async (_, _) => await ScanAsync();
        actions.Controls.Add(_connect);
        actions.Controls.Add(cancel);
        actions.Controls.Add(_search);
        main.Controls.Add(actions, 0, 4);
        AcceptButton = _connect;
        CancelButton = cancel;
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_list.SelectedIndex >= 0)
                _ip.Text = _devices[_list.SelectedIndex].Address.ToString();
        };
        Shown += async (_, _) => await ScanAsync();
    }

    private async Task ScanAsync()
    {
        _search.Enabled = false;
        _status.Text = "Searching your home network for Melisma Receiver…";
        try
        {
            var found = await TvReceiverDiscovery.ScanAsync(TimeSpan.FromSeconds(2.5));
            if (IsDisposed) return;
            _devices.Clear();
            _devices.AddRange(found);
            _list.Items.Clear();
            foreach (var device in found) _list.Items.Add(device.ToString());
            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
                _status.Text = $"{found.Count} TV receiver(s) found. Enter pairing code.";
            }
            else
            {
                _status.Text = "No receiver discovered. Enter the TV's IP manually below.";
            }
        }
        catch (Exception ex) when (ex is SocketException or
            UnauthorizedAccessException or IOException)
        {
            _status.Text = "Discovery unavailable: " + ex.Message +
                " — use TV IP below.";
        }
        finally { if (!IsDisposed) _search.Enabled = true; }
    }

    private void Choose()
    {
        if (!IPAddress.TryParse(_ip.Text.Trim(), out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            _status.Text = "Choose a detected TV or enter its IPv4 address.";
            return;
        }
        string code = _code.Text.Trim();
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            _status.Text = "Enter the six-digit code displayed on the TV.";
            return;
        }
        SelectedDevice = _devices.FirstOrDefault(x => x.Address.Equals(address)) ??
            new TvReceiverDevice("Melisma Receiver", address,
                TvReceiverDiscovery.ReceiverPort);
        PairingCode = code;
        DialogResult = DialogResult.OK;
        Close();
    }
}
