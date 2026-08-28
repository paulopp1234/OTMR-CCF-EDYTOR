using CcfEditor.Core;
using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.WinForms;

public partial class OtmrLiveControl : UserControl
{
    private readonly SerialOtmrTransport _transport;
    private readonly OtmrLiveService _liveService;
    private bool _closing;
    private bool _busy;
    private CancellationTokenSource? _startCancellation;
    private string? _connectedPortName;
    private int _assembledFrameCount;
    private bool _captureScrollPending;
    private bool _captureScrollInvokeQueued;
    private CcfDocument? _selectedCcf;

    internal OtmrLiveState State => _liveService.State;

    /// <summary>
    /// Raised once for each genuine complete live frame emitted by the shared
    /// OtmrLiveService. MainForm owns the downstream bench/RCM/realtime routing;
    /// the control must not rely on a nullable visual-tree lookup for that path.
    /// </summary>
    internal event EventHandler<OtmrLiveFrameEventArgs>? GenuineLiveFrameReceived;

    public OtmrLiveControl()
    {
        InitializeComponent();

        _transport = new SerialOtmrTransport();
        _liveService = new OtmrLiveService(_transport);
        _liveService.CaptureAdded += LiveService_CaptureAdded;
        _liveService.FrameReceived += LiveService_FrameReceived;
        _liveService.ConnectionChanged += LiveService_ConnectionChanged;
        _liveService.ErrorOccurred += LiveService_ErrorOccurred;
        _liveService.StateChanged += LiveService_StateChanged;
        _liveService.DiagnosticAdded += LiveService_DiagnosticAdded;

        RefreshPorts();
        UpdateStateUi();
        RefreshCaptureGrid();
        captureGrid.Layout += CaptureGrid_LayoutAvailable;
        captureGrid.SizeChanged += CaptureGrid_LayoutAvailable;
        captureGrid.VisibleChanged += CaptureGrid_LayoutAvailable;
    }

    internal void SetCurrentCcf(CcfDocument? document)
    {
        _selectedCcf = document;
        UpdateStateUi();
    }

    private void RefreshPortsButton_Click(object? sender, EventArgs e) => RefreshPorts();

    private void PortComboBox_SelectedIndexChanged(object? sender, EventArgs e) => UpdateStateUi();

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
            OtmrSerialSettings settings = OtmrSerialSettings.Class171Bench(portName, dtrHigh: false);
            await _liveService.ConnectAsync(settings);
        }
        catch (Exception ex)
        {
            UpdateStateUi();
            MessageBox.Show(this, ex.Message, "Unable to connect OTMR serial port", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void StartLiveButton_Click(object? sender, EventArgs e)
    {
        if (_selectedCcf is null)
        {
            MessageBox.Show(this, "Open and explicitly select a Class 171 CCF before starting live output.",
                "OTMR START preflight", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        const string confirmation =
            "Start the Class 171 OTMR real-time output sequence?\r\n\r\n" +
            "CONTROLLED BENCH VALIDATION: this interrogates the recorder, validates the selected CCF, " +
            "freezes its original pages, then sends the seven proven generated writes.\r\n\r\n" +
            "Live status still requires a complete FB FB ... FF frame.";
        if (MessageBox.Show(
                this,
                confirmation,
                "Start OTMR Live — captured interrogation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _startCancellation?.Dispose();
        _startCancellation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            await _liveService.StartLiveAsync(_selectedCcf, _startCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "OTMR live-start sequence cancelled; disconnecting.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Live start failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Unable to start OTMR live output", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void StopLiveButton_Click(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            await _liveService.StopLiveAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to stop OTMR realtime", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void StopRestoreButton_Click(object? sender, EventArgs e)
    {
        const string confirmation =
            "Stop realtime and perform the captured full restoration exchange using the frozen pre-START recorder values?\r\n\r\n" +
            "This is separate from Stop Live, which only closes the live COM port.";
        if (MessageBox.Show(this, confirmation, "Stop + Restore original OTMR configuration",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        SetBusy(true);
        try
        {
            await _liveService.StopAndRestoreAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to restore OTMR configuration", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DisconnectButton_Click(object? sender, EventArgs e)
    {
        _startCancellation?.Cancel();
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
                RequestCaptureScrollToLastRow();
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

        void Update()
        {
            _connectedPortName = e.IsConnected ? e.PortName : null;
            UpdateStateUi();
        }
        if (InvokeRequired)
            BeginInvoke((Action)Update);
        else
            Update();
    }

    private void LiveService_StateChanged(object? sender, OtmrLiveStateChangedEventArgs e)
    {
        if (_closing || IsDisposed)
            return;

        void Update()
        {
            UpdateStateUi();
            (FindForm() as MainForm)?.ReportOtmrLiveState(e.State);
        }

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
            GenuineLiveFrameReceived?.Invoke(this, e);
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

    private void LiveService_DiagnosticAdded(object? sender, OtmrProtocolDiagnosticEventArgs e)
    {
        if (_closing || IsDisposed)
            return;

        void Update() => statusLabel.Text = e.Entry.Message;
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

        RequestCaptureScrollToLastRow();
    }

    private void RequestCaptureScrollToLastRow()
    {
        if (_closing || captureGrid.IsDisposed || captureGrid.Rows.Count == 0)
            return;

        int target = captureGrid.Rows.Count - 1;
        if (DataGridViewViewport.TryScrollToRow(captureGrid, target))
        {
            _captureScrollPending = false;
            return;
        }

        _captureScrollPending = true;
        if (_captureScrollInvokeQueued || !captureGrid.IsHandleCreated || captureGrid.Disposing)
            return;

        _captureScrollInvokeQueued = true;
        BeginInvoke((Action)(() =>
        {
            _captureScrollInvokeQueued = false;
            TryCompletePendingCaptureScroll();
        }));
    }

    private void CaptureGrid_LayoutAvailable(object? sender, EventArgs e) => TryCompletePendingCaptureScroll();

    private void TryCompletePendingCaptureScroll()
    {
        if (!_captureScrollPending || _closing || captureGrid.IsDisposed || captureGrid.Rows.Count == 0)
            return;
        if (DataGridViewViewport.TryScrollToRow(captureGrid, captureGrid.Rows.Count - 1))
            _captureScrollPending = false;
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

    private void UpdateStateUi()
    {
        OtmrLiveState state = _liveService.State;
        bool disconnected = state == OtmrLiveState.Disconnected;
        bool hasPort = portComboBox.SelectedItem is string;
        connectButton.Enabled = !_busy && disconnected && hasPort;
        startLiveButton.Enabled = !_busy && state == OtmrLiveState.ConnectedIdle;
        stopLiveButton.Enabled = !_busy && state is OtmrLiveState.LiveActive or OtmrLiveState.LiveReady or OtmrLiveState.WaitingForLiveFrames;
        stopRestoreButton.Enabled = !_busy && state is OtmrLiveState.LiveActive or OtmrLiveState.LiveReady or OtmrLiveState.WaitingForLiveFrames or OtmrLiveState.NotLive;
        disconnectButton.Enabled = !_busy && state != OtmrLiveState.Disconnected;
        portComboBox.Enabled = !_busy && disconnected;
        refreshPortsButton.Enabled = !_busy && disconnected;

        (string text, Color color) = state switch
        {
            OtmrLiveState.Disconnected => ("DISCONNECTED", Color.DimGray),
            OtmrLiveState.ConnectedIdle => ("CONNECTED — IDLE (NO LIVE STREAM)", Color.DarkOrange),
            OtmrLiveState.PreflightingConfiguration => ("VALIDATING GENERATED CONFIGURATION EXCHANGE", Color.DarkOrange),
            >= OtmrLiveState.WaitingFor01_01 and <= OtmrLiveState.WaitingFor01_0C =>
                ($"INTERROGATING — {state.ToString().Replace("WaitingFor", string.Empty, StringComparison.Ordinal)}", Color.DarkOrange),
            OtmrLiveState.RecorderConfigurationWriteBlocked =>
                ("STOPPED — RECORDER CONFIGURATION WRITE SAFETY BOUNDARY", Color.DarkRed),
            >= OtmrLiveState.WaitingFor01_0D and <= OtmrLiveState.WaitingFor01_13 =>
                ($"GENERATED CONFIGURATION EXCHANGE — {state.ToString().Replace("WaitingFor", string.Empty, StringComparison.Ordinal)}", Color.DarkOrange),
            OtmrLiveState.StartingLive => ("STARTING LIVE — CAPTURED FINAL COMMAND SENT", Color.DarkOrange),
            OtmrLiveState.WaitingForLiveFrames => ("ARMING NATIVE LIVE RECEIVE", Color.DarkOrange),
            OtmrLiveState.LiveReady => ("OTMR LIVE READY — waiting for input events", Color.DarkGreen),
            OtmrLiveState.LiveActive => ("OTMR LIVE STREAM ACTIVE", Color.DarkGreen),
            OtmrLiveState.Error => ("ERROR — LIVE STREAM NOT ACTIVE", Color.DarkRed),
            OtmrLiveState.NotLive => ("NOT LIVE - COM CLOSED", Color.DimGray),
            OtmrLiveState.RestoringOriginalConfiguration => ("RESTORING FROZEN PRE-START CONFIGURATION", Color.DarkOrange),
            _ => (state.ToString(), SystemColors.ControlText)
        };
        liveStateLabel.Text = text;
        liveStateLabel.ForeColor = color;

        string port = _connectedPortName ?? portComboBox.SelectedItem as string ?? "selected COM";
        statusLabel.Text = state switch
        {
            OtmrLiveState.Disconnected => "Disconnected. Connect opens 38400/8/N/1 with RTS LOW and DTR LOW; it sends nothing.",
            OtmrLiveState.ConnectedIdle => $"{port} open at 38400/8/N/1, RTS LOW, DTR LOW. Click Start OTMR Live to send the controlled sequence.",
            OtmrLiveState.PreflightingConfiguration => "Validating selected CCF compatibility, frozen recorder pages, generated lengths/checks, and complete byte provenance.",
            >= OtmrLiveState.WaitingFor01_01 and <= OtmrLiveState.WaitingFor01_0C =>
                $"Captured read/interrogation stage {state.ToString().Replace("WaitingFor", string.Empty, StringComparison.Ordinal)}; waiting for its complete reply/data frame.",
            OtmrLiveState.RecorderConfigurationWriteBlocked =>
                "Configuration preflight failed. No generated configuration write or final live-start command was transmitted; see the precise error message.",
            >= OtmrLiveState.WaitingFor01_0D and <= OtmrLiveState.WaitingFor01_13 =>
                $"Generated write accepted; waiting for the complete expected {state.ToString().Replace("WaitingFor", string.Empty, StringComparison.Ordinal)} reply before advancing.",
            OtmrLiveState.StartingLive => "Captured final 01 07 sent after complete 01 13; closing after the observed ~17.6 ms interval.",
            OtmrLiveState.WaitingForLiveFrames => $"{port} native live receive is being armed.",
            OtmrLiveState.LiveReady => "OTMR Live Ready means the Class 171 START sequence completed and the native receive port is armed. Live Stream Active is shown after the first genuine realtime frame is received.",
            OtmrLiveState.LiveActive => "OTMR LIVE STREAM ACTIVE — genuine complete FB FB … FF traffic detected. Raw frames only; meanings are not inferred.",
            OtmrLiveState.Error => "Live-start error. Stop / Disconnect before retrying.",
            OtmrLiveState.NotLive => "Realtime stopped by closing COM. No stop command was sent. Use Stop + Restore for the separate captured restoration exchange.",
            OtmrLiveState.RestoringOriginalConfiguration => "Running the captured cleanup/restoration sequence from the frozen pre-START recorder snapshot.",
            _ => state.ToString()
        };
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateStateUi();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _closing = true;
        _liveService.CaptureAdded -= LiveService_CaptureAdded;
        _liveService.FrameReceived -= LiveService_FrameReceived;
        _liveService.ConnectionChanged -= LiveService_ConnectionChanged;
        _liveService.ErrorOccurred -= LiveService_ErrorOccurred;
        _liveService.StateChanged -= LiveService_StateChanged;
        _liveService.DiagnosticAdded -= LiveService_DiagnosticAdded;
        _startCancellation?.Cancel();
        _startCancellation?.Dispose();
        _liveService.Dispose();
        base.OnHandleDestroyed(e);
    }
}
