using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.WinForms;

public partial class OtmrLiveControl : UserControl
{
    private readonly SerialOtmrTransport _transport;
    private readonly OtmrLiveService _liveService;
    private bool _closing;

    public OtmrLiveControl()
    {
        InitializeComponent();

        _transport = new SerialOtmrTransport();
        _liveService = new OtmrLiveService(_transport);
        _liveService.CaptureAdded += LiveService_CaptureAdded;
        _liveService.ConnectionChanged += LiveService_ConnectionChanged;
        _liveService.ErrorOccurred += LiveService_ErrorOccurred;

        RefreshPorts();
        UpdateConnectionUi(false, null);
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
        captureGrid.Rows.Clear();
        captureCountLabel.Text = "Capture entries: 0";
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
            Filter = "OTMR capture JSON Lines (*.jsonl)|*.jsonl|All files (*.*)|*.*",
            DefaultExt = "jsonl",
            AddExtension = true,
            FileName = $"OTMR_CAPTURE_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl",
            Title = "Save raw OTMR capture"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            await OtmrCaptureWriter.WriteJsonLinesAsync(dialog.FileName, snapshot);
            statusLabel.Text = $"Saved {snapshot.Count} raw capture entries.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to save OTMR capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LiveService_CaptureAdded(object? sender, OtmrCaptureEntryEventArgs e)
    {
        if (_closing || IsDisposed)
            return;

        void AddRow()
        {
            OtmrCaptureEntry entry = e.Entry;
            captureGrid.Rows.Add(
                entry.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"),
                entry.Direction.ToString().ToUpperInvariant(),
                entry.Hex,
                entry.Interpretation ?? string.Empty);

            captureCountLabel.Text = $"Capture entries: {captureGrid.Rows.Count}";
            if (captureGrid.Rows.Count > 0)
                captureGrid.FirstDisplayedScrollingRowIndex = captureGrid.Rows.Count - 1;
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
        _liveService.ConnectionChanged -= LiveService_ConnectionChanged;
        _liveService.ErrorOccurred -= LiveService_ErrorOccurred;
        _liveService.Dispose();
        base.OnHandleDestroyed(e);
    }
}
