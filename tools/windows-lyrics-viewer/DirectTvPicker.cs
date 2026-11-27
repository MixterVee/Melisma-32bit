using System.Net;
using System.Net.Sockets;

namespace MelismaLyricsViewer;

/// <summary>
/// TV receiver chooser. Keep labels, code field, scan status and ALL action
/// buttons visible at Windows 100–200% DPI: a tiny fixed dialog with an
/// unwrapped FlowLayoutPanel used to cut off the buttons and error message.
/// </summary>
internal sealed class DirectTvPicker : Form
{
    private readonly ListBox _list = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 11),
        BackColor = Color.FromArgb(27, 36, 56),
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        IntegralHeight = false,
        Margin = new Padding(0, 3, 0, 9)
    };
    private readonly TextBox _ip = new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(6, 6, 12, 6),
        AccessibleName = "TV IP address"
    };
    private readonly TextBox _code = new()
    {
        Dock = DockStyle.Fill,
        MaxLength = 6,
        TextAlign = HorizontalAlignment.Center,
        Margin = new Padding(6, 6, 0, 6),
        AccessibleName = "Six-digit TV pairing code"
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = false,
        ForeColor = Color.LightSkyBlue,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 7, 0, 7)
    };
    private readonly Button _connect = ActionButton("Connect", 112);
    private readonly Button _search = ActionButton("Scan again", 128);
    private readonly List<TvReceiverDevice> _devices = new();

    internal TvReceiverDevice? SelectedDevice { get; private set; }
    internal string PairingCode { get; private set; } = "";

    private static Button ActionButton(string text, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 38,
        Margin = new Padding(7, 5, 0, 5),
        FlatStyle = FlatStyle.Standard
    };

    internal DirectTvPicker()
    {
        Text = "Melisma · Connect directly to Android TV";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        MinimumSize = new Size(640, 465);
        ClientSize = new Size(690, 468);
        BackColor = Color.FromArgb(17, 23, 39);
        ForeColor = Color.White;

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            AutoScroll = true,
            Padding = new Padding(19, 17, 19, 16),
            BackColor = BackColor
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(main);

        var intro = new Label
        {
            Text = "Open Melisma Receiver on your TV first.\n" +
                "Choose a TV below or enter its IP address, then enter the six-digit code.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(218, 227, 239),
            Margin = new Padding(0, 0, 0, 7)
        };
        main.Controls.Add(intro, 0, 0);
        main.Controls.Add(_list, 0, 1);

        // Unlike the old 540px horizontal FlowLayoutPanel, these percentage
        // columns always allocate a real width to BOTH textboxes. Labels
        // cannot push the pairing code off the right edge at high DPI.
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0, 3, 0, 3)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        fields.Controls.Add(new Label
        {
            Text = "TV IP (optional)",
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        }, 0, 0);
        fields.Controls.Add(_ip, 1, 0);
        fields.Controls.Add(new Label
        {
            Text = "Pairing code",
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        }, 2, 0);
        fields.Controls.Add(_code, 3, 0);
        main.Controls.Add(fields, 0, 2);
        main.Controls.Add(_status, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoScroll = true,
            Margin = new Padding(0)
        };
        var cancel = ActionButton("Cancel", 110);
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
            if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _devices.Count)
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
                _status.Text = $"{found.Count} TV receiver(s) found. Enter its pairing code.";
            }
            else
            {
                _status.Text = "No TV found automatically. If the receiver is running, " +
                    "enter the IP address shown on the TV and its six-digit code.";
            }
        }
        catch (Exception ex) when (ex is SocketException or
            UnauthorizedAccessException or IOException)
        {
            _status.Text = "Automatic discovery unavailable (" + ex.Message +
                "). You can still connect using the TV's IP and pairing code.";
        }
        finally { if (!IsDisposed) _search.Enabled = true; }
    }

    private void Choose()
    {
        if (!IPAddress.TryParse(_ip.Text.Trim(), out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            _status.Text = "Select a TV or enter the IPv4 address displayed by Melisma Receiver.";
            return;
        }
        string code = _code.Text.Trim();
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            _status.Text = "Enter the complete six-digit code displayed on the TV.";
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
