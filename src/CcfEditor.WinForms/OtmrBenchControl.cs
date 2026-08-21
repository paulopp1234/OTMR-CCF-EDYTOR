using System.ComponentModel;
using CcfEditor.Core;
using CcfEditor.Otmr.Bench;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.WinForms;

public partial class OtmrBenchControl : UserControl
{
    private readonly List<OtmrBenchPinDefinition> _definitions = new();
    private readonly RcmCaptureWindowCoordinator _captureCoordinator = new();
    private CcfDocument? _document;
    private RcmProfile? _rcmProfile;
    private string? _rcmProfilePath;
    private string? _pinMapPath;
    private bool _closing;

    public OtmrBenchControl()
    {
        InitializeComponent();

        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            LoadBundledPinMap();
        UpdateWorkflow();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime || !Visible || IsDisposed)
            return;
        RefreshCcfFromHost();
    }

    public void ReportRawLiveFrame(DateTimeOffset timestamp, OtmrLiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_closing || IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => ReportRawLiveFrame(timestamp, frame)));
            return;
        }

        if (!_captureCoordinator.AddFrame(timestamp, frame))
            return;

        UpdateCaptureStatusOnly();
        statusLabel.Text =
            $"CAPTURING {_captureCoordinator.ActivePinKey}: complete frame retained at " +
            $"{timestamp.ToLocalTime():HH:mm:ss.fff}. Raw evidence only; decoder NOT VERIFIED.";
    }

    private void LoadBundledPinMap()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Profiles",
            "Class171",
            "Class171_Bench_PinMap.tsv");
        if (!File.Exists(path))
        {
            statusLabel.Text = $"Class 171 physical pin map was not found: {path}";
            return;
        }

        LoadPinMap(path);
    }

    private void LoadPinMap(string path)
    {
        IReadOnlyList<OtmrBenchPinDefinition> loaded = OtmrBenchProfileReader.LoadTsv(path);
        _definitions.Clear();
        _definitions.AddRange(loaded);
        _pinMapPath = path;
        pinMapStatusLabel.Text =
            $"Physical pin map: {Path.GetFileName(path)} | " +
            $"{_definitions.Count(definition => definition.IsVoltageTestPoint)} testable / {_definitions.Count} total";
        PopulateConnectors();
        RenderTable();
    }

    private void LoadPinMapButton_Click(object? sender, EventArgs e)
    {
        if (_captureCoordinator.IsCapturing)
            return;

        using var dialog = new OpenFileDialog
        {
            Filter = "OTMR bench pin maps (*.tsv)|*.tsv|All files (*.*)|*.*",
            Title = "Load Class 171 physical pin map",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            LoadPinMap(dialog.FileName);
            statusLabel.Text = "Physical pin map loaded. Create a new RCM profile to use it.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to load pin map", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshCcfButton_Click(object? sender, EventArgs e) => RefreshCcfFromHost();

    private void RefreshCcfFromHost()
    {
        _document = (FindForm() as MainForm)?.GetCurrentCcfForBench();
        ccfStatusLabel.Text = _document is null
            ? "CCF: none loaded"
            : $"CCF: {Path.GetFileName(_document.SourcePath ?? "opened CCF")} | " +
              $"{_document.Length:N0} bytes | SHA-256 {_document.OriginalSha256[..12]}…";
        UpdateCommandAvailability();
        RenderTable();
    }

    private void CreateRcmProfileButton_Click(object? sender, EventArgs e)
    {
        if (_document is null)
        {
            MessageBox.Show(this, "Load a CCF before creating an RCM profile.", "RCM Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_definitions.Count == 0)
        {
            MessageBox.Show(this, "Load the Class 171 physical pin map first.", "RCM Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _rcmProfile = RcmProfileFactory.Create(_document, _definitions, "Class 171", DateTimeOffset.Now);
        _rcmProfilePath = null;
        PopulateConnectors();
        RenderTable();
        statusLabel.Text =
            "RCM profile created in memory from the source CCF and physical pin map. Save it to begin progressive persistence.";
    }

    private async void OpenRcmProfileButton_Click(object? sender, EventArgs e)
    {
        if (_document is null)
        {
            MessageBox.Show(this, "Load the source CCF before opening its RCM profile.", "Open RCM Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "OTMR RCM profiles (*.json)|*.json|JSON files (*.json)|*.json",
            Title = "Open OTMR RCM profile",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            RcmProfile loaded = await RcmProfileJson.LoadAsync(dialog.FileName);
            RcmProfileJson.EnsureMatchesSource(loaded, _document.OriginalSha256, _document.Length);
            _rcmProfile = loaded;
            _rcmProfilePath = Path.GetFullPath(dialog.FileName);
            PopulateConnectors();
            RenderTable();
            statusLabel.Text = $"RCM profile reopened: {Path.GetFileName(_rcmProfilePath)}. Progress restored.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to open RCM profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void SaveRcmProfileButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null)
            return;

        if (_rcmProfilePath is null)
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "OTMR RCM profiles (*.json)|*.json",
                DefaultExt = "json",
                AddExtension = true,
                FileName = $"{Path.GetFileNameWithoutExtension(_rcmProfile.SourceCcfFilename)}_RCM.json",
                Title = "Save OTMR RCM profile"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            _rcmProfilePath = Path.GetFullPath(dialog.FileName);
        }

        await SaveProfileToKnownPathAsync(showConfirmation: true);
    }

    private async Task SaveProfileToKnownPathAsync(bool showConfirmation)
    {
        if (_rcmProfile is null || _rcmProfilePath is null)
            return;

        try
        {
            await RcmProfileJson.SaveAsync(_rcmProfilePath, _rcmProfile, DateTimeOffset.Now);
            if (showConfirmation)
                statusLabel.Text = $"RCM progress saved: {_rcmProfilePath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to save RCM profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ConnectorComboBox_SelectedIndexChanged(object? sender, EventArgs e) => RenderTable();

    private void RcmGrid_SelectionChanged(object? sender, EventArgs e) => UpdateWorkflow();

    private void CaptureVoltageRemovedButton_Click(object? sender, EventArgs e) =>
        BeginCapture(RcmElectricalTestState.VoltageRemoved);

    private void CaptureVoltageAppliedButton_Click(object? sender, EventArgs e) =>
        BeginCapture(RcmElectricalTestState.VoltageApplied24V);

    private void BeginCapture(RcmElectricalTestState state)
    {
        RcmPinProfile? pin = SelectedProfilePin();
        if (pin is null)
            return;

        try
        {
            _captureCoordinator.Begin(pin, state, DateTimeOffset.Now);
            captureWindowTimer.Interval = Math.Max(100, decimal.ToInt32(captureSecondsNumeric.Value * 1000M));
            captureWindowTimer.Start();
            RenderTable(pin.Key);
            UpdateCommandAvailability();
            statusLabel.Text = state == RcmElectricalTestState.VoltageRemoved
                ? $"CAPTURING {pin.Key}: operator condition = TEST VOLTAGE REMOVED."
                : $"CAPTURING {pin.Key}: operator condition = +24 V APPLIED.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to start capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void CaptureWindowTimer_Tick(object? sender, EventArgs e)
    {
        captureWindowTimer.Stop();
        string? key = _captureCoordinator.ActivePinKey;
        try
        {
            RcmStateEvidence evidence = _captureCoordinator.Stop(DateTimeOffset.Now);
            if (_rcmProfile is not null)
                _rcmProfile.LastModifiedTimestamp = DateTimeOffset.Now;
            RenderTable(key);
            UpdateCommandAvailability();
            statusLabel.Text =
                $"CAPTURED {key}: {evidence.FrameCount} complete raw frame(s). " +
                "CANDIDATE RAW EVIDENCE only; decoder NOT VERIFIED.";
            await SaveProfileToKnownPathAsync(showConfirmation: false);
        }
        catch (Exception ex)
        {
            statusLabel.Text = ex.Message;
            UpdateCommandAvailability();
        }
    }

    private async void CompareStatesButton_Click(object? sender, EventArgs e)
    {
        RcmPinProfile? pin = SelectedProfilePin();
        if (pin is null)
            return;

        try
        {
            _captureCoordinator.Compare(pin, DateTimeOffset.Now);
            if (_rcmProfile is not null)
                _rcmProfile.LastModifiedTimestamp = DateTimeOffset.Now;
            RenderTable(pin.Key);
            statusLabel.Text =
                $"Compared {pin.Key}: {pin.Comparison.RepeatableDifferences.Count} repeatable candidate raw difference(s). " +
                "Decoder NOT VERIFIED.";
            await SaveProfileToKnownPathAsync(showConfirmation: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot compare states", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async void ResetInputButton_Click(object? sender, EventArgs e)
    {
        RcmPinProfile? pin = SelectedProfilePin();
        if (pin is null)
            return;

        try
        {
            _captureCoordinator.Reset(pin);
            if (_rcmProfile is not null)
                _rcmProfile.LastModifiedTimestamp = DateTimeOffset.Now;
            RenderTable(pin.Key);
            statusLabel.Text = $"Reset test evidence for {pin.Key} only.";
            await SaveProfileToKnownPathAsync(showConfirmation: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot reset input", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void PopulateConnectors()
    {
        string? previous = connectorComboBox.SelectedItem as string;
        IEnumerable<string> values = _rcmProfile is null
            ? _definitions.Select(definition => definition.Connector)
            : _rcmProfile.Pins.Select(pin => pin.Connector);
        string[] connectors = values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        connectorComboBox.BeginUpdate();
        try
        {
            connectorComboBox.Items.Clear();
            connectorComboBox.Items.AddRange(connectors);
            if (previous is not null && connectors.Contains(previous, StringComparer.OrdinalIgnoreCase))
                connectorComboBox.SelectedItem = connectors.First(value => string.Equals(value, previous, StringComparison.OrdinalIgnoreCase));
            else if (connectors.Length > 0)
                connectorComboBox.SelectedIndex = 0;
        }
        finally
        {
            connectorComboBox.EndUpdate();
        }
    }

    private void RenderTable(string? preferredKey = null)
    {
        if (rcmGrid is null)
            return;

        preferredKey ??= rcmGrid.CurrentRow?.Tag as string;
        string connector = connectorComboBox.SelectedItem as string ?? string.Empty;
        rcmGrid.SuspendLayout();
        DataGridViewRow? selectedRow = null;
        try
        {
            rcmGrid.Rows.Clear();
            if (_rcmProfile is not null)
            {
                foreach (RcmPinProfile pin in _rcmProfile.Pins.Where(pin =>
                             string.Equals(pin.Connector, connector, StringComparison.OrdinalIgnoreCase)))
                {
                    int index = rcmGrid.Rows.Add(
                        pin.Pin,
                        pin.Function,
                        FormatCcfReference(pin.CcfReference),
                        FormatCaptureState(pin, RcmElectricalTestState.VoltageRemoved),
                        FormatCaptureState(pin, RcmElectricalTestState.VoltageApplied24V),
                        FormatDifference(pin),
                        "NOT VERIFIED",
                        pin.Testable ? pin.RcmResult : "NOT TESTABLE");
                    DataGridViewRow row = rcmGrid.Rows[index];
                    row.Tag = pin.Key;
                    ApplyRowStyle(row, pin.Testable, pin.RcmResult);
                    if (string.Equals(pin.Key, preferredKey, StringComparison.Ordinal))
                        selectedRow = row;
                }
            }
            else
            {
                foreach (OtmrBenchPinDefinition definition in _definitions.Where(definition =>
                             string.Equals(definition.Connector, connector, StringComparison.OrdinalIgnoreCase)))
                {
                    string key = $"{definition.Connector}-{definition.Pin}";
                    int index = rcmGrid.Rows.Add(
                        definition.Pin,
                        definition.ExpectedFunction,
                        FormatDefinitionCcf(definition),
                        definition.IsVoltageTestPoint ? "PROFILE REQUIRED" : "NOT TESTABLE",
                        definition.IsVoltageTestPoint ? "PROFILE REQUIRED" : "NOT TESTABLE",
                        "—",
                        "NOT VERIFIED",
                        definition.IsVoltageTestPoint ? RcmResultStates.NotTested : "NOT TESTABLE");
                    DataGridViewRow row = rcmGrid.Rows[index];
                    row.Tag = key;
                    ApplyRowStyle(row, definition.IsVoltageTestPoint, row.Cells[7].Value?.ToString() ?? string.Empty);
                    if (string.Equals(key, preferredKey, StringComparison.Ordinal))
                        selectedRow = row;
                }
            }
        }
        finally
        {
            rcmGrid.ResumeLayout();
        }

        if (selectedRow is not null)
        {
            rcmGrid.ClearSelection();
            selectedRow.Selected = true;
            rcmGrid.CurrentCell = selectedRow.Cells[0];
        }

        UpdateProgress();
        UpdateWorkflow();
    }

    private void UpdateCaptureStatusOnly()
    {
        RcmPinProfile? pin = SelectedProfilePin();
        if (pin is null)
            return;

        if (_captureCoordinator.ActiveState == RcmElectricalTestState.VoltageRemoved)
            voltageRemovedStatusLabel.Text = $"CAPTURING | {pin.VoltageRemoved.FrameCount} complete frame(s)";
        else if (_captureCoordinator.ActiveState == RcmElectricalTestState.VoltageApplied24V)
            voltageAppliedStatusLabel.Text = $"CAPTURING | {pin.VoltageApplied24V.FrameCount} complete frame(s)";
    }

    private void UpdateWorkflow()
    {
        string? key = rcmGrid?.CurrentRow?.Tag as string;
        OtmrBenchPinDefinition? definition = FindDefinition(key);
        RcmPinProfile? pin = FindProfilePin(key);

        if (definition is null && pin is null)
        {
            selectedPinLabel.Text = "Select a physical pin";
            voltageRemovedInstructionLabel.Text = "REMOVE TEST VOLTAGE FROM SELECTED PIN";
            voltageAppliedInstructionLabel.Text = "APPLY +24 V TO SELECTED PIN";
            voltageRemovedStatusLabel.Text = "NOT CAPTURED";
            voltageAppliedStatusLabel.Text = "NOT CAPTURED";
            evidenceTextBox.Text = "Create or open an RCM profile, then select a physical input.";
            UpdateCommandAvailability();
            return;
        }

        string connector = pin?.Connector ?? definition!.Connector;
        string physicalPin = pin?.Pin ?? definition!.Pin;
        string function = pin?.Function ?? definition!.ExpectedFunction;
        string pinKey = $"{connector}-{physicalPin}";
        RcmCcfReference? ccf = pin?.CcfReference;
        selectedPinLabel.Text =
            $"{pinKey}\r\n{function}\r\n" +
            $"Expected CCF records: {FormatRecordPair(ccf, definition)}\r\n" +
            $"Card {ccf?.LogicalCard?.ToString() ?? definition?.ExpectedCard?.ToString() ?? "—"} / " +
            $"Channel {ccf?.LogicalChannel?.ToString() ?? definition?.ExpectedChannel?.ToString() ?? "—"}";
        voltageRemovedInstructionLabel.Text = $"REMOVE TEST VOLTAGE FROM {pinKey}";
        voltageAppliedInstructionLabel.Text = $"APPLY +24 V TO {pinKey}";

        if (pin is null)
        {
            voltageRemovedStatusLabel.Text = "PROFILE NOT CREATED";
            voltageAppliedStatusLabel.Text = "PROFILE NOT CREATED";
            evidenceTextBox.Text = "Create RCM Profile From Loaded CCF before capturing evidence.";
        }
        else if (!pin.Testable)
        {
            voltageRemovedStatusLabel.Text = "NOT TESTABLE";
            voltageAppliedStatusLabel.Text = "NOT TESTABLE";
            evidenceTextBox.Text =
                $"NOT TESTABLE\r\n\r\nSafety classification:\r\n{pin.SafetyClassification}\r\n\r\n" +
                "No voltage capture is offered for returns, supplies, RS485, link/termination, or unresolved unsafe points.";
        }
        else
        {
            voltageRemovedStatusLabel.Text = FormatCaptureState(pin, RcmElectricalTestState.VoltageRemoved);
            voltageAppliedStatusLabel.Text = FormatCaptureState(pin, RcmElectricalTestState.VoltageApplied24V);
            evidenceTextBox.Text = BuildEvidenceSummary(pin);
        }

        UpdateCommandAvailability();
    }

    private void UpdateCommandAvailability()
    {
        bool capturing = _captureCoordinator.IsCapturing;
        RcmPinProfile? pin = SelectedProfilePin();
        bool canCapture = _rcmProfile is not null && pin?.Testable == true && !capturing;
        createRcmProfileButton.Enabled = _document is not null && _definitions.Count > 0 && !capturing;
        openRcmProfileButton.Enabled = _document is not null && !capturing;
        saveRcmProfileButton.Enabled = _rcmProfile is not null && !capturing;
        loadPinMapButton.Enabled = !capturing;
        refreshCcfButton.Enabled = !capturing;
        connectorComboBox.Enabled = !capturing;
        rcmGrid.Enabled = !capturing;
        captureSecondsNumeric.Enabled = !capturing;
        captureVoltageRemovedButton.Enabled = canCapture;
        captureVoltageAppliedButton.Enabled = canCapture;
        compareStatesButton.Enabled = canCapture && pin!.VoltageRemoved.Tested && pin.VoltageApplied24V.Tested;
        resetInputButton.Enabled = _rcmProfile is not null && pin is not null && !capturing;
    }

    private void UpdateProgress()
    {
        progressLabel.Text = _rcmProfile is null
            ? "RCM Progress: no profile created/opened"
            : $"RCM Progress: {_rcmProfile.CompletedTestablePinCount} / {_rcmProfile.TestablePinCount} testable inputs complete";
        profilePathLabel.Text = _rcmProfile is null
            ? "RCM JSON: none"
            : $"RCM JSON: {_rcmProfilePath ?? "not saved yet"} | Decoder: NOT VERIFIED";
    }

    private RcmPinProfile? SelectedProfilePin() => FindProfilePin(rcmGrid?.CurrentRow?.Tag as string);

    private RcmPinProfile? FindProfilePin(string? key) => key is null || _rcmProfile is null
        ? null
        : _rcmProfile.Pins.SingleOrDefault(pin => string.Equals(pin.Key, key, StringComparison.Ordinal));

    private OtmrBenchPinDefinition? FindDefinition(string? key) => key is null
        ? null
        : _definitions.SingleOrDefault(definition =>
            string.Equals($"{definition.Connector}-{definition.Pin}", key, StringComparison.Ordinal));

    private static string FormatCaptureState(RcmPinProfile pin, RcmElectricalTestState state)
    {
        if (!pin.Testable)
            return "NOT TESTABLE";
        RcmStateEvidence evidence = state == RcmElectricalTestState.VoltageRemoved
            ? pin.VoltageRemoved
            : pin.VoltageApplied24V;
        return evidence.Tested ? $"CAPTURED | {evidence.FrameCount} frames" : "NOT CAPTURED";
    }

    private string FormatDifference(RcmPinProfile pin)
    {
        if (pin.Comparison.ComparedAt is null)
            return pin.VoltageRemoved.Tested && pin.VoltageApplied24V.Tested ? "READY TO COMPARE" : "—";
        return pin.Comparison.RepeatableDifferences.Count > 0
            ? $"{pin.Comparison.RepeatableDifferences.Count} candidate difference(s)"
            : "NO REPEATABLE DIFFERENCE";
    }

    private static string FormatCcfReference(RcmCcfReference? reference)
    {
        if (reference is null)
            return "No fixed CCF reference";
        string records = reference.RecordA is int a
            ? reference.RecordB is int b ? $"{a} ↔ {b}" : a.ToString()
            : "—";
        return $"records {records} | card {reference.LogicalCard?.ToString() ?? "—"} / ch {reference.LogicalChannel?.ToString() ?? "—"}";
    }

    private static string FormatDefinitionCcf(OtmrBenchPinDefinition definition)
    {
        string records = definition.ExpectedRecordA is int a
            ? definition.ExpectedRecordB is int b ? $"{a} ↔ {b}" : a.ToString()
            : "—";
        return $"records {records} | card {definition.ExpectedCard?.ToString() ?? "—"} / ch {definition.ExpectedChannel?.ToString() ?? "—"}";
    }

    private static string FormatRecordPair(RcmCcfReference? reference, OtmrBenchPinDefinition? definition)
    {
        int? a = reference?.RecordA ?? definition?.ExpectedRecordA;
        int? b = reference?.RecordB ?? definition?.ExpectedRecordB;
        return a is int recordA ? b is int recordB ? $"{recordA} ↔ {recordB}" : recordA.ToString() : "—";
    }

    private static string BuildEvidenceSummary(RcmPinProfile pin)
    {
        static string CaptureSummary(string label, RcmStateEvidence evidence) =>
            $"{label}: {(evidence.Tested ? "CAPTURED" : "NOT CAPTURED")} | " +
            $"frames {evidence.FrameCount} | stable raw features {evidence.CandidateStableFeatures.Count}\r\n" +
            $"Candidate raw signature: {(string.IsNullOrEmpty(evidence.CandidateRawSignature) ? "—" : evidence.CandidateRawSignature)}";

        string differences = pin.Comparison.RepeatableDifferences.Count == 0
            ? "No comparison evidence yet."
            : string.Join("\r\n", pin.Comparison.RepeatableDifferences.Take(20));
        return
            $"CANDIDATE RAW EVIDENCE\r\n\r\n" +
            CaptureSummary("Voltage Removed", pin.VoltageRemoved) + "\r\n\r\n" +
            CaptureSummary("+24V Applied", pin.VoltageApplied24V) + "\r\n\r\n" +
            $"State Difference:\r\n{differences}\r\n\r\n" +
            "Decoder: NOT VERIFIED\r\n" +
            $"RCM Result: {pin.RcmResult}\r\n\r\n" +
            "Electrical condition is operator-supplied. It is not interpreted as CCF ON/OFF, a record number, card/channel, PASS, or FAIL.";
    }

    private static void ApplyRowStyle(DataGridViewRow row, bool testable, string result)
    {
        if (!testable)
        {
            row.DefaultCellStyle.BackColor = Color.Gainsboro;
            row.DefaultCellStyle.ForeColor = Color.DimGray;
        }
        else if (result == RcmResultStates.RawDifferenceFound)
        {
            row.DefaultCellStyle.BackColor = Color.LightCyan;
        }
        else if (result == RcmResultStates.BothStatesCaptured)
        {
            row.DefaultCellStyle.BackColor = Color.LightGoldenrodYellow;
        }
        else if (result is RcmResultStates.VoltageRemovedCaptured or RcmResultStates.VoltageApplied24VCaptured)
        {
            row.DefaultCellStyle.BackColor = Color.AliceBlue;
        }
        else
        {
            row.DefaultCellStyle.BackColor = Color.White;
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _closing = true;
        captureWindowTimer.Stop();
        base.OnHandleDestroyed(e);
    }
}
