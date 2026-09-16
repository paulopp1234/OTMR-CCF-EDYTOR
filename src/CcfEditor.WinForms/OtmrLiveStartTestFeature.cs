using System.IO.Ports;
using System.Text.Json;

namespace CcfEditor.WinForms;

internal static class OtmrLiveStartTestFeature
{
    public static void Install(Form mainForm)
    {
        mainForm.Shown += (_, _) =>
        {
            MenuStrip? menu = Find<MenuStrip>(mainForm).FirstOrDefault();
            if (menu is null || menu.Items.OfType<ToolStripMenuItem>().Any(x => x.Name == "otmrBenchMenuItem"))
                return;

            var root = new ToolStripMenuItem("OTMR Bench") { Name = "otmrBenchMenuItem" };
            var item = new ToolStripMenuItem("Connection / Live Start Test...");
            item.Click += (_, _) =>
            {
                using var form = new OtmrLiveStartTestForm();
                form.ShowDialog(mainForm);
            };
            root.DropDownItems.Add(item);
            menu.Items.Add(root);
        };
    }

    private static IEnumerable<T> Find<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;
            foreach (T nested in Find<T>(child))
                yield return nested;
        }
    }
}

internal sealed class OtmrLiveStartTestForm : Form
{
    private static readonly byte[] QueryFrame = { 0x01, 0x01, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x01, 0x03, 0x01, 0x04 };
    private static readonly byte[] CandidateFrame = { 0x01, 0x07, 0x00, 0x01, 0x01, 0x01, 0x02, 0x01, 0x02, 0x13, 0x03, 0x13, 0x04 };
    private static readonly byte[] IdentityPrefix = { 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x01, 0x03, 0x01, 0x04 };

    private readonly ComboBox _ports = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly Button _refresh = new() { Text = "Refresh COM", AutoSize = true };
    private readonly Button _open = new() { Text = "Open DTR LOW", AutoSize = true };
    private readonly Button _close = new() { Text = "Close / Stop", AutoSize = true };
    private readonly Button _query = new() { Text = "1  Query OTMR", AutoSize = true };
    private readonly Button _dtr = new() { Text = "2  Reopen DTR HIGH only", AutoSize = true };
    private readonly Button _candidate = new() { Text = "3  Candidate live start", AutoSize = true };
    private readonly Button _save = new() { Text = "Save capture...", AutoSize = true };
    private readonly Button _clear = new() { Text = "Clear", AutoSize = true };
    private readonly Label _serialStatus = new() { AutoSize = true, Text = "Closed" };
    private readonly Label _detect = new() { AutoSize = true, Text = "Detection: waiting", Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, BackColor = Color.White, Font = new Font("Consolas", 9f) };
    private readonly List<CaptureLine> _capture = new();
    private SerialPort? _port;
    private bool _previousFb;
    private bool _identitySeen;
    private bool _liveSeen;

    public OtmrLiveStartTestForm()
    {
        Text = "OTMR DEU Connection / Live Start Test — BENCH ONLY";
        Width = 1120;
        Height = 760;
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterParent;
        BuildUi();
        WireEvents();
        RefreshPorts();
    }

    private void BuildUi()
    {
        var warning = new Label
        {
            Dock = DockStyle.Top, Height = 58, Padding = new Padding(8), BackColor = Color.MistyRose,
            ForeColor = Color.DarkRed, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Text = "BENCH ONLY. A DEU/configuration connection can alter OTMR operating mode or halt a journey. " +
                   "This tool NEVER sends a CCF and NEVER sends the captured 0x10B configuration blocks."
        };
        var evidence = new Label
        {
            Dock = DockStyle.Top, Height = 58, Padding = new Padding(8, 4, 8, 4),
            Text = "Test in order: (1) proven 01 01 interrogation, (2) DTR HIGH with NO TX bytes, " +
                   "(3) experimental 01 07...13 mode frame then close/reopen at 38400/8N1, RTS LOW, DTR HIGH."
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(8), AutoScroll = true, WrapContents = true };
        buttons.Controls.Add(new Label { Text = "COM:", AutoSize = true, Margin = new Padding(3, 9, 3, 3) });
        foreach (Control c in new Control[] { _ports, _refresh, _open, _close, _query, _dtr, _candidate, _save, _clear })
            buttons.Controls.Add(c);
        var status = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 6, 8, 0), BackColor = Color.WhiteSmoke };
        status.Controls.Add(new Label { Text = "Serial: ", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) });
        status.Controls.Add(_serialStatus);
        status.Controls.Add(new Label { Text = "     ", AutoSize = true });
        status.Controls.Add(_detect);
        Controls.Add(_log);
        Controls.Add(status);
        Controls.Add(buttons);
        Controls.Add(evidence);
        Controls.Add(warning);
    }

    private void WireEvents()
    {
        _refresh.Click += (_, _) => RefreshPorts();
        _open.Click += (_, _) => TryAction(() => OpenPort(false));
        _close.Click += (_, _) => TryAction(ClosePort);
        _query.Click += async (_, _) => await TryActionAsync(QueryAsync);
        _dtr.Click += async (_, _) => await DtrOnlyAsync();
        _candidate.Click += async (_, _) => await CandidateAsync();
        _save.Click += (_, _) => SaveCapture();
        _clear.Click += (_, _) => ClearCapture();
    }

    private void RefreshPorts()
    {
        string? selected = _ports.SelectedItem?.ToString();
        string[] names = SerialPort.GetPortNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        _ports.Items.Clear();
        _ports.Items.AddRange(names);
        if (selected is not null && names.Contains(selected, StringComparer.OrdinalIgnoreCase))
            _ports.SelectedItem = names.First(x => x.Equals(selected, StringComparison.OrdinalIgnoreCase));
        else if (names.Length > 0)
            _ports.SelectedIndex = 0;
        UpdateButtons();
    }

    private string SelectedPort => _ports.SelectedItem?.ToString() ?? throw new InvalidOperationException("Select a COM port first.");

    private void OpenPort(bool dtrHigh)
    {
        ClosePort();
        var port = new SerialPort(SelectedPort, 38400, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None, DtrEnable = dtrHigh, RtsEnable = false,
            ReadTimeout = 500, WriteTimeout = 500, ReadBufferSize = 4096, WriteBufferSize = 4096
        };
        port.DataReceived += PortOnDataReceived;
        port.Open();
        _port = port;
        _serialStatus.Text = $"{port.PortName} OPEN 38400 8N1 RTS=LOW DTR={(dtrHigh ? "HIGH" : "LOW")}";
        Status($"Port opened, DTR {(dtrHigh ? "HIGH" : "LOW")}.");
        UpdateButtons();
    }

    private void ClosePort()
    {
        SerialPort? port = _port;
        _port = null;
        if (port is not null)
        {
            port.DataReceived -= PortOnDataReceived;
            if (port.IsOpen)
            {
                try { port.DtrEnable = false; } catch { }
                port.Close();
            }
            port.Dispose();
        }
        _serialStatus.Text = "Closed";
        UpdateButtons();
    }

    private async Task QueryAsync()
    {
        if (_port?.IsOpen != true)
            OpenPort(false);
        else if (_port.DtrEnable)
            _port.DtrEnable = false;
        Tx(QueryFrame, "Proven OTMR interrogation/query frame");
        await Task.Delay(50);
    }

    private async Task DtrOnlyAsync()
    {
        if (!Confirm("Close/reopen with DTR HIGH and send NO bytes?\r\n\r\nBench recorder only.", "DTR HIGH test"))
            return;
        await TryActionAsync(async () =>
        {
            ClosePort();
            await Task.Delay(150);
            _previousFb = false;
            OpenPort(true);
            Status("DTR-only test active. NO TX bytes sent; watching for FB FB.");
        });
    }

    private async Task CandidateAsync()
    {
        string hex = Hex(CandidateFrame);
        if (!Confirm("EXPERIMENTAL. Send only this 13-byte frame:\r\n\r\n" + hex +
                     "\r\n\r\nThen close/reopen 38400/8N1, RTS LOW, DTR HIGH.\r\n" +
                     "NO CCF and NO 0x10B config blocks are sent.\r\n\r\nBench recorder only. Continue?",
                     "Candidate live-start test"))
            return;
        await TryActionAsync(async () =>
        {
            if (_port?.IsOpen != true)
                OpenPort(false);
            else if (_port.DtrEnable)
                _port.DtrEnable = false;
            Tx(CandidateFrame, "EXPERIMENTAL candidate live-mode frame");
            await Task.Delay(120);
            ClosePort();
            await Task.Delay(150);
            _previousFb = false;
            OpenPort(true);
            Status("Candidate sequence complete; watching for FB FB live records.");
        });
    }

    private void Tx(byte[] bytes, string interpretation)
    {
        if (_port?.IsOpen != true)
            throw new InvalidOperationException("COM port is not open.");
        _port.Write(bytes, 0, bytes.Length);
        Log("TX", bytes, interpretation);
    }

    private void PortOnDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        SerialPort? port = _port;
        if (port?.IsOpen != true)
            return;
        try
        {
            int count = port.BytesToRead;
            if (count <= 0) return;
            byte[] bytes = new byte[count];
            int read = port.Read(bytes, 0, bytes.Length);
            if (read != bytes.Length) Array.Resize(ref bytes, read);
            bool identity = StartsWith(bytes, IdentityPrefix);
            bool live = DetectFbFb(bytes);
            BeginInvoke(new Action(() =>
            {
                _identitySeen |= identity;
                _liveSeen |= live;
                Log("RX", bytes, live ? "FB FB real-time marker detected" : identity ? "OTMR identity/config reply detected" : null);
                UpdateDetection();
            }));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (!IsDisposed)
                BeginInvoke(new Action(() => Status("SERIAL ERROR: " + ex.Message)));
        }
    }

    private bool DetectFbFb(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            if (_previousFb && b == 0xFB) { _previousFb = true; return true; }
            _previousFb = b == 0xFB;
        }
        return false;
    }

    private void UpdateDetection()
    {
        if (_liveSeen) { _detect.Text = "Detection: LIVE STREAM DETECTED (FB FB)"; _detect.ForeColor = Color.DarkGreen; }
        else if (_identitySeen) { _detect.Text = "Detection: OTMR REPLIED — no live stream yet"; _detect.ForeColor = Color.DarkOrange; }
        else { _detect.Text = "Detection: waiting"; _detect.ForeColor = SystemColors.ControlText; }
    }

    private void Log(string direction, ReadOnlySpan<byte> bytes, string? interpretation)
    {
        var line = new CaptureLine(DateTimeOffset.Now, direction, Hex(bytes), interpretation);
        _capture.Add(line);
        _log.AppendText($"{line.Timestamp:HH:mm:ss.fff} {direction,-2} {line.Hex}{(interpretation is null ? "" : "  [" + interpretation + "]")}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
        UpdateButtons();
    }

    private void Status(string message)
    {
        _capture.Add(new CaptureLine(DateTimeOffset.Now, "--", "", message));
        _log.AppendText($"{DateTimeOffset.Now:HH:mm:ss.fff} -- {message}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
        UpdateButtons();
    }

    private void SaveCapture()
    {
        if (_capture.Count == 0) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "JSON Lines (*.jsonl)|*.jsonl|All files (*.*)|*.*", DefaultExt = "jsonl", AddExtension = true,
            FileName = $"OTMR_LIVE_START_TEST_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl", OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        using var writer = new StreamWriter(dlg.FileName, false, new System.Text.UTF8Encoding(false));
        foreach (CaptureLine line in _capture) writer.WriteLine(JsonSerializer.Serialize(line));
        Status("Capture saved: " + dlg.FileName);
    }

    private void ClearCapture()
    {
        _capture.Clear(); _log.Clear(); _identitySeen = false; _liveSeen = false; _previousFb = false; UpdateDetection(); UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool hasPort = _ports.SelectedItem is not null;
        _open.Enabled = hasPort && _port?.IsOpen != true;
        _close.Enabled = _port?.IsOpen == true;
        _query.Enabled = hasPort; _dtr.Enabled = hasPort; _candidate.Enabled = hasPort; _save.Enabled = _capture.Count > 0;
    }

    private static string Hex(ReadOnlySpan<byte> bytes) => string.Join(" ", bytes.ToArray().Select(b => b.ToString("X2")));
    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) => data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);
    private static bool Confirm(string text, string title) => MessageBox.Show(text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private void TryAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { Status("ERROR: " + ex.Message); MessageBox.Show(this, ex.Message, "OTMR serial test", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task TryActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Status("ERROR: " + ex.Message); MessageBox.Show(this, ex.Message, "OTMR serial test", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { ClosePort(); base.OnFormClosed(e); }
    private sealed record CaptureLine(DateTimeOffset Timestamp, string Direction, string Hex, string? Interpretation);
}
