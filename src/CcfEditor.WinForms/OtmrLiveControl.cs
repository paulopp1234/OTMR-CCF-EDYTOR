using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.WinForms;

public partial class OtmrLiveControl : UserControl
{
    private readonly SerialOtmrTransport _transport;
    private readonly OtmrLiveService _liveService;
    private bool _closing;
    private int _assembledFrameCount;

    public OtmrLiveControl()
    {
        InitializeComponent();

        _transport = new SerialOtmrTransport();
        _liveService = new OtmrLiveService(_transport);
        _liveService.CaptureAdded += LiveService_CaptureAdded;
        _liveService.FrameReceived += LiveService_FrameReceived;
        _liveService.ConnectionChanged += LiveService_ConnectionChanged;
        _liveService.ErrorOccurred += LiveService_ErrorOccurred;

        RefreshPorts();
        UpdateConnectionUi(false, null);
        RefreshCaptureGrid();
    }

    private void RefreshPortsButton_Click(object? sender, EventArgs e) => RefreshPorts();

    private void RefreshPorts()
    {
        string? previous = portComboBox.SelectedItem as string;
        string[] ports;

        try
        {
            ports = SerialOtmrTransport.GetAvailablePorts();
        }
        catch (Exception ex)
        {
            ports = Array.Empty<string>();
            statusLabel.Text = $"COM enumeration failed: {ex.Message}";
        }

        portComboBox.BeginUpdate();
        try
        {
            portComboBox.Items.Clear();
            portComboBox.Items.AddRange(ports);

            if (previous is not null && ports.Contains(previous, StringComparer.OrdinalIgnoreCase))
                portComboBox.SelectedItem = ports.First(p => string.Equals(p, previous, StringComparison.OrdinalIgnoreCase));
            else if (ports.Length > 0)
                portComboBox.SelectedIndex = 0;
        }
        finally
        {
            portComboBox.EndUpdate();
        }
    }

    private async void ConnectButton_Click(object? sender, EventArgs e)
    {
        if (portComboBox.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
        {
            MessageBox.Show(this, "Select a COM port first.", "OTMR Live", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        try
        {
            OtmrSerialSettings settings = OtmrSerialSettings.Class171Bench(portName);
            await _liveService.ConnectAsync(settings);
        }
        catch (Exception ex)
        {
            UpdateConnectionUi(false, null);
            MessageBox.Show(this, ex.Message, "Unable to connect OTMR serial port", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DisconnectButton_Click(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            await _liveService.DisconnectAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to disconnect OTMR serial port", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ClearCaptureButton_Click(object? sender, EventArgs e)
    {
        _liveService.ClearCapture();
        RefreshCaptureGrid();
        statusLabel.Text = "Capture cleared.";
    }

    private async void SaveCaptureButton_Click(object? sender, EventArgs e)
    {
        IReadOnlyList<OtmrCaptureEntry> snapshot = _liveService.GetCaptureSnapshot();
        if (snapshot.Count == 0)
        {
            MessageBox.Show(this, "There is no captured traffic to save.", "OTMR Capture", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "OTMR capture JSON Lines (*.jsonl)|*.jsonl|OTMR capture text (*.txt)|*.txt",
            FilterIndex = 1,
            DefaultExt = "jsonl",
            AddExtension = true,
            FileName = $"OTMR_CAPTURE_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl",
            Title = "Save raw OTMR capture"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            if (dialog.FilterIndex == 2)
            {
                string outputPath = Path.ChangeExtension(dialog.FileName, ".txt");
                await OtmrCaptureWriter.WriteTextAsync(outputPath, snapshot);
                statusLabel.Text = $"Saved all {snapshot.Count} raw capture entries as text.";
            }
            else
            {
                string outputPath = Path.ChangeExtension(dialog.FileName, ".jsonl");
                await OtmrCaptureWriter.WriteJsonLinesAsync(outputPath, snapshot);
                statusLabel.Text = $"Saved all {snapshot.Count} raw capture entries as JSON Lines.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to save OTMR capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopyHexButton_Click(object? sender, EventArgs e)
    {
        if (captureGrid.CurrentRow is null)
        {
            MessageBox.Show(this, "Select a capture row first.", "Copy Hex", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string hex = Convert.ToString(captureGrid.CurrentRow.Cells[bytesColumn.Index].Value) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(hex))
        {
            MessageBox.Show(this, "The selected capture row has no raw bytes.", "Copy Hex", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Clipboard.SetText(hex);
            statusLabel.Text = $"Copied {hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length} raw byte(s) as hex.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to copy hex", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CaptureFilterComboBox_SelectedIndexChanged(object? sender, EventArgs e) => RefreshCaptureGrid();

    private void LiveService_CaptureAdded(object? sender, OtmrCaptureEntryEventArgs e)
    {
        if (_closing || IsDisposed)
            return;

        void AddRow()
        {
            if (EntryMatchesCurrentFilter(e.Entry))
            {
                AddCaptureRow(e.Entry);
                if (captureGrid.Rows.Count > 0)
                    captureGrid.FirstDisplayedScrollingRowIndex = captureGrid.Rows.Count - 1;
            }

            UpdateCaptureCount(_liveService.GetCaptureSnapshot().Count);
        }

        if (InvokeRequired)
            BeginInvoke((Action)AddRow);
        else
            AddRow();
    }

    private void LiveService_ConnectionChanged(object? sender, OtmrConnectionChangedEventArgs e)
    {
        if (_closing || IsDisposed)
            return;

        void Update() => UpdateConnectionUi(e.IsConnected, e.PortName);
        if (InvokeRequired)
            BeginInvoke((Action)Update);
        else
            Update();
    }

    private void LiveService_FrameReceived(object? sender, OtmrLiveFrameEventArgs e)
    {
        if (_closing || IsDisposed)
            return;

        void ReportFrame()
        {
            _assembledFrameCount++;
            statusLabel.Text =
                $"Complete RX frame #{_assembledFrameCount}: {e.Frame.Length} bytes | {e.Frame.Hex}";
            (FindForm() as MainForm)?.ReportOtmrLiveFrame(e.Timestamp, e.Frame);
        }

        if (InvokeRequired)
            BeginInvoke((Action)ReportFrame);
        else
            ReportFrame();
    }

    private void LiveService_ErrorOccurred(object? sender, OtmrLiveErrorEventArgs e)
    {
        if (_closing || IsDisposed)
            return;

        void Update() => statusLabel.Text = $"Serial error: {e.Exception.Message}";
        if (InvokeRequired)
            BeginInvoke((Action)Update);
        else
            Update();
    }

    private void RefreshCaptureGrid()
    {
        if (_closing || IsDisposed)
            return;

        IReadOnlyList<OtmrCaptureEntry> snapshot = _liveService.GetCaptureSnapshot();

        captureGrid.SuspendLayout();
        try
        {
            captureGrid.Rows.Clear();
            foreach (OtmrCaptureEntry entry in snapshot)
            {
                if (EntryMatchesCurrentFilter(entry))
                    AddCaptureRow(entry);
            }
        }
        finally
        {
            captureGrid.ResumeLayout();
        }

        UpdateCaptureCount(snapshot.Count);

        if (captureGrid.Rows.Count > 0)
            captureGrid.FirstDisplayedScrollingRowIndex = captureGrid.Rows.Count - 1;
    }

    private void AddCaptureRow(OtmrCaptureEntry entry)
    {
        int index = captureGrid.Rows.Add(
            entry.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"),
            entry.Direction.ToString().ToUpperInvariant(),
            entry.Hex,
            entry.Interpretation ?? string.Empty);
        captureGrid.Rows[index].Tag = entry;
    }

    private bool EntryMatchesCurrentFilter(OtmrCaptureEntry entry)
    {
        string filter = captureFilterComboBox.SelectedItem as string ?? "All";
        return filter switch
        {
            "RX" => entry.Direction == OtmrDirection.Rx,
            "TX" => entry.Direction == OtmrDirection.Tx,
            _ => true
        };
    }

    private void UpdateCaptureCount(int total) =>
        captureCountLabel.Text = $"Shown: {captureGrid.Rows.Count} / Total: {total}";

    private void UpdateConnectionUi(bool connected, string? portName)
    {
        connectButton.Enabled = !connected;
        disconnectButton.Enabled = connected;
        portComboBox.Enabled = !connected;
        refreshPortsButton.Enabled = !connected;
        statusLabel.Text = connected
            ? $"Connected to {portName} at 38400 / 8 / None / 1. No protocol commands are sent in Milestone 1."
            : "Disconnected. Milestone 1 is transport/capture only; no protocol commands are transmitted.";
    }

    private void SetBusy(bool busy)
    {
        if (busy)
        {
            connectButton.Enabled = false;
            disconnectButton.Enabled = false;
            refreshPortsButton.Enabled = false;
            portComboBox.Enabled = false;
        }
        else
        {
            UpdateConnectionUi(_liveService.IsConnected, portComboBox.SelectedItem as string);
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _closing = true;
        _liveService.CaptureAdded -= LiveService_CaptureAdded;
        _liveService.FrameReceived -= LiveService_FrameReceived;
        _liveService.ConnectionChanged -= LiveService_ConnectionChanged;
        _liveService.ErrorOccurred -= LiveService_ErrorOccurred;
        _liveService.Dispose();
        base.OnHandleDestroyed(e);
    }
}
