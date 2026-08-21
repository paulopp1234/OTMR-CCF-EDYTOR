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
            RcmProfilePaths.EnsureDefaultFolder();
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

    private RcmPinMapImportResult LoadPinMap(string path)
    {
        IReadOnlyList<OtmrBenchPinDefinition> loaded = OtmrBenchProfileReader.LoadTsv(path);
        _definitions.Clear();
        _definitions.AddRange(loaded);
        _pinMapPath = path;
        pinMapStatusLabel.Text = $"Optional pin map: {Path.GetFileName(path)}";
        if (_rcmProfile is null)
            return default;
        RcmPinMapImportResult result = RcmPinMapImporter.ImportFillUnassigned(_rcmProfile, _definitions);
        PopulateConnectors();
        RenderTable();
        return result;
    }

    private void LoadPinMapButton_Click(object? sender, EventArgs e)
    {
        if (_captureCoordinator.IsCapturing || _rcmProfile is null)
            return;

        using var dialog = new OpenFileDialog
        {
            Filter = "OTMR bench pin maps (*.tsv)|*.tsv|All files (*.*)|*.*",
            Title = "Import optional physical pin map into the RCM JSON",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            RcmPinMapImportResult result = LoadPinMap(dialog.FileName);
            statusLabel.Text =
                $"Optional pin map imported: {result.UpdatedUnassignedInputs} unassigned logical input(s) updated, " +
                $"{result.AddedPhysicalInputs} physical-only input(s) added. Existing physical text was not overwritten.";
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
        UpdateCcfStatus();
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
        _rcmProfile = RcmProfileFactory.CreateFromCcf(_document, "Class 171", DateTimeOffset.Now);
        _rcmProfilePath = null;
        PopulateConnectors();
        RenderTable();
        UpdateCcfStatus();
        statusLabel.Text =
            "RCM profile created from logical CCF records. Physical connector, pin, MIO, wiring, testability and safety remain unassigned.";
    }

    private async void OpenRcmProfileButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "OTMR RCM profiles (*.json)|*.json|JSON files (*.json)|*.json",
            Title = "Open OTMR RCM profile",
            InitialDirectory = RcmProfilePaths.EnsureDefaultFolder(),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            RcmProfile loaded = await RcmProfileJson.LoadAsync(dialog.FileName);
            _rcmProfile = loaded;
            _rcmProfilePath = Path.GetFullPath(dialog.FileName);
            PopulateConnectors();
            RenderTable();
            UpdateCcfStatus();
            statusLabel.Text =
                $"RCM profile reopened standalone: {Path.GetFileName(_rcmProfilePath)}. " +
                $"Progress restored; {RcmProfileJson.GetCcfStatus(loaded, _document)}.";
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
                InitialDirectory = RcmProfilePaths.EnsureDefaultFolder(),
                Title = "Save OTMR RCM profile"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            _rcmProfilePath = Path.GetFullPath(dialog.FileName);
        }

        await SaveProfileToKnownPathAsync(showConfirmation: true);
    }

    private async void SaveRcmProfileAsButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null)
            return;
        using var dialog = new SaveFileDialog
        {
            Filter = "OTMR RCM profiles (*.json)|*.json",
            DefaultExt = "json",
            AddExtension = true,
            InitialDirectory = RcmProfilePaths.EnsureDefaultFolder(),
            FileName = Path.GetFileName(_rcmProfilePath ?? $"{Path.GetFileNameWithoutExtension(_rcmProfile.SourceCcfFilename)}_RCM.json"),
            Title = "Save OTMR RCM profile as"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _rcmProfilePath = Path.GetFullPath(dialog.FileName);
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

    private async void AddConnectorButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null)
            return;
        using var dialog = new TextPromptDialog("Add Connector", "Connector name (for example J1, J3, MIO-A or TB1):");
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            RcmProfileEditor.AddConnector(_rcmProfile, dialog.Value);
            MarkProfileModified();
            PopulateConnectors();
            connectorComboBox.SelectedItem = dialog.Value;
            await SaveProfileToKnownPathAsync(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot add connector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void RenameConnectorButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null || connectorComboBox.SelectedIndex <= 0 || connectorComboBox.SelectedItem is not string oldName)
            return;
        using var dialog = new TextPromptDialog("Rename Connector", "New connector name:", oldName);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            RcmProfileEditor.RenameConnector(_rcmProfile, oldName, dialog.Value);
            MarkProfileModified();
            PopulateConnectors();
            connectorComboBox.SelectedItem = dialog.Value;
            await SaveProfileToKnownPathAsync(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot rename connector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void DeleteConnectorButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null || connectorComboBox.SelectedIndex <= 0 || connectorComboBox.SelectedItem is not string name)
            return;
        int count = _rcmProfile.Pins.Count(pin => string.Equals(pin.Connector, name, StringComparison.OrdinalIgnoreCase));
        string message = count == 0
            ? $"Delete connector '{name}'?"
            : $"Connector '{name}' contains {count} input(s), including any captured evidence.\r\n\r\n" +
              "Delete the connector AND those inputs? This cannot be undone.";
        if (MessageBox.Show(this, message, "Delete Connector", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        RcmProfileEditor.DeleteConnector(_rcmProfile, name, deleteInputs: true);
        MarkProfileModified();
        PopulateConnectors();
        await SaveProfileToKnownPathAsync(false);
    }

    private async void AddInputButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null)
            return;
        string connector = connectorComboBox.SelectedIndex > 0 ? connectorComboBox.SelectedItem as string ?? string.Empty : string.Empty;
        var initial = new RcmInputEdit { Connector = connector };
        using var dialog = new RcmInputEditorDialog("Add RCM Input / Pin", _rcmProfile.Connectors.Select(item => item.Name), initial);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null)
            return;
        try
        {
            RcmPinProfile pin = RcmProfileEditor.AddInput(_rcmProfile, dialog.Result, _document);
            MarkProfileModified();
            PopulateConnectors();
            SelectConnectorFor(pin.Connector);
            RenderTable(pin.Id);
            await SaveProfileToKnownPathAsync(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot add input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void EditInputButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null || SelectedProfilePin() is not RcmPinProfile pin)
            return;
        using var dialog = new RcmInputEditorDialog(
            $"Edit RCM Input {pin.DisplayKey}",
            _rcmProfile.Connectors.Select(item => item.Name),
            RcmInputEdit.From(pin));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null)
            return;

        bool logicalChange = IsLogicalMappingChange(pin.CcfReference, dialog.Result);
        if (logicalChange && RcmProfileEditor.HasEvidence(pin) && MessageBox.Show(
                this,
                "This input already contains captured RCM evidence. Changing its logical CCF mapping may invalidate the interpretation of that evidence.\r\n\r\nContinue without deleting evidence?",
                "Captured evidence warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try
        {
            RcmProfileEditor.UpdateInput(_rcmProfile, pin.Id, dialog.Result, _document);
            MarkProfileModified();
            PopulateConnectors();
            SelectConnectorFor(pin.Connector);
            RenderTable(pin.Id);
            await SaveProfileToKnownPathAsync(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot edit input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void DeleteInputButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null || SelectedProfilePin() is not RcmPinProfile pin)
            return;
        string evidenceWarning = RcmProfileEditor.HasEvidence(pin)
            ? " This input contains captured evidence which will also be deleted."
            : string.Empty;
        if (MessageBox.Show(this, $"Delete input {pin.DisplayKey}?{evidenceWarning}", "Delete Input", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        RcmProfileEditor.DeleteInput(_rcmProfile, pin.Id);
        MarkProfileModified();
        RenderTable();
        await SaveProfileToKnownPathAsync(false);
    }

    private void SelectConnectorFor(string connector) =>
        connectorComboBox.SelectedItem = string.IsNullOrWhiteSpace(connector) ? "(Unassigned)" : connector;

    private void MarkProfileModified()
    {
        if (_rcmProfile is not null)
            _rcmProfile.LastModifiedTimestamp = DateTimeOffset.Now;
    }

    private void UpdateCcfStatus()
    {
        if (_rcmProfile is not null)
        {
            ccfStatusLabel.Text =
                $"{RcmProfileJson.GetCcfStatus(_rcmProfile, _document)} | source {_rcmProfile.SourceCcfFilename} | " +
                $"SHA-256 {ShortHash(_rcmProfile.SourceCcfSha256)}";
        }
        else
        {
            ccfStatusLabel.Text = _document is null
                ? "CCF NOT LOADED"
                : $"CCF loaded: {Path.GetFileName(_document.SourcePath ?? "opened CCF")} | {_document.Length:N0} bytes";
        }
    }

    private static string ShortHash(string hash) => hash.Length <= 12 ? hash : hash[..12] + "…";

    private static bool IsLogicalMappingChange(RcmCcfReference? current, RcmInputEdit edit) =>
        current?.RecordA != edit.RecordA || current?.RecordB != edit.RecordB ||
        current?.LogicalCard != edit.LogicalCard || current?.LogicalChannel != edit.LogicalChannel ||
        current?.RecordType != edit.RecordType;

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
            RenderTable(pin.Id);
            UpdateCommandAvailability();
            statusLabel.Text = state == RcmElectricalTestState.VoltageRemoved
                ? $"CAPTURING {pin.DisplayKey}: operator condition = TEST VOLTAGE REMOVED."
                : $"CAPTURING {pin.DisplayKey}: operator condition = +24 V APPLIED.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to start capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void CaptureWindowTimer_Tick(object? sender, EventArgs e)
    {
        captureWindowTimer.Stop();
        Guid? inputId = _captureCoordinator.ActiveInputId;
        string? displayKey = _captureCoordinator.ActivePinKey;
        try
        {
            RcmStateEvidence evidence = _captureCoordinator.Stop(DateTimeOffset.Now);
            if (_rcmProfile is not null)
                _rcmProfile.LastModifiedTimestamp = DateTimeOffset.Now;
            RenderTable(inputId);
            UpdateCommandAvailability();
            statusLabel.Text =
                evidence.Tested
                    ? $"CAPTURED {displayKey}: {evidence.FrameCount} complete raw frame(s). CANDIDATE RAW EVIDENCE only; decoder NOT VERIFIED."
                    : $"NO OTMR DATA for {displayKey}: zero complete frames. State remains NOT CAPTURED and progress was not incremented.";
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
            RenderTable(pin.Id);
            statusLabel.Text =
                $"Compared {pin.DisplayKey}: {pin.Comparison.RepeatableDifferences.Count} repeatable candidate raw difference(s). " +
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
            RenderTable(pin.Id);
            statusLabel.Text = $"Reset test evidence for {pin.DisplayKey} only.";
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
        IEnumerable<string> values = _rcmProfile?.Connectors.Select(connector => connector.Name) ?? Enumerable.Empty<string>();
        string[] connectors = values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        connectorComboBox.BeginUpdate();
        try
        {
            connectorComboBox.Items.Clear();
            connectorComboBox.Items.Add("(Unassigned)");
            connectorComboBox.Items.AddRange(connectors);
            if (previous is not null && connectors.Contains(previous, StringComparer.OrdinalIgnoreCase))
                connectorComboBox.SelectedItem = connectors.First(value => string.Equals(value, previous, StringComparison.OrdinalIgnoreCase));
            else
                connectorComboBox.SelectedIndex = 0;
        }
        finally
        {
            connectorComboBox.EndUpdate();
        }
    }

    private void RenderTable(Guid? preferredInputId = null)
    {
        if (rcmGrid is null)
            return;

        preferredInputId ??= rcmGrid.CurrentRow?.Tag is Guid selectedId ? selectedId : null;
        string connectorSelection = connectorComboBox.SelectedItem as string ?? "(Unassigned)";
        string connector = connectorSelection == "(Unassigned)" ? string.Empty : connectorSelection;
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
                    row.Tag = pin.Id;
                    ApplyRowStyle(row, pin.Testable, pin.RcmResult);
                    if (pin.Id == preferredInputId)
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
        RcmPinProfile? pin = SelectedProfilePin();

        if (pin is null)
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

        string pinKey = pin.DisplayKey;
        RcmCcfReference? ccf = pin.CcfReference;
        selectedPinLabel.Text =
            $"{pinKey}\r\n{pin.Function}\r\n" +
            $"Expected CCF records: {FormatRecordPair(ccf)}\r\n" +
            $"Card {ccf?.LogicalCard?.ToString() ?? "—"} / Channel {ccf?.LogicalChannel?.ToString() ?? "—"}";
        voltageRemovedInstructionLabel.Text = $"REMOVE TEST VOLTAGE FROM {pinKey}";
        voltageAppliedInstructionLabel.Text = $"APPLY +24 V TO {pinKey}";

        if (!pin.Testable)
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
        createRcmProfileButton.Enabled = _document is not null && !capturing;
        openRcmProfileButton.Enabled = !capturing;
        saveRcmProfileButton.Enabled = _rcmProfile is not null && !capturing;
        saveRcmProfileAsButton.Enabled = _rcmProfile is not null && !capturing;
        loadPinMapButton.Enabled = _rcmProfile is not null && !capturing;
        refreshCcfButton.Enabled = !capturing;
        connectorComboBox.Enabled = !capturing;
        rcmGrid.Enabled = !capturing;
        captureSecondsNumeric.Enabled = !capturing;
        captureVoltageRemovedButton.Enabled = canCapture;
        captureVoltageAppliedButton.Enabled = canCapture;
        compareStatesButton.Enabled = canCapture && pin!.VoltageRemoved.Tested && pin.VoltageApplied24V.Tested;
        resetInputButton.Enabled = _rcmProfile is not null && pin is not null && !capturing;
        addConnectorButton.Enabled = _rcmProfile is not null && !capturing;
        renameConnectorButton.Enabled = _rcmProfile is not null && connectorComboBox.SelectedIndex > 0 && !capturing;
        deleteConnectorButton.Enabled = renameConnectorButton.Enabled;
        addInputButton.Enabled = _rcmProfile is not null && !capturing;
        editInputButton.Enabled = pin is not null && !capturing;
        deleteInputButton.Enabled = pin is not null && !capturing;
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

    private RcmPinProfile? SelectedProfilePin() =>
        rcmGrid?.CurrentRow?.Tag is Guid id && _rcmProfile is not null
            ? _rcmProfile.Pins.SingleOrDefault(pin => pin.Id == id)
            : null;

    private static string FormatCaptureState(RcmPinProfile pin, RcmElectricalTestState state)
    {
        if (!pin.Testable)
            return "NOT TESTABLE";
        RcmStateEvidence evidence = state == RcmElectricalTestState.VoltageRemoved
            ? pin.VoltageRemoved
            : pin.VoltageApplied24V;
        if (evidence.Tested)
            return $"CAPTURED | {evidence.FrameCount} frames";
        return evidence.NoOtmrData ? "NO OTMR DATA" : "NOT CAPTURED";
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

    private static string FormatRecordPair(RcmCcfReference? reference)
    {
        int? a = reference?.RecordA;
        int? b = reference?.RecordB;
        return a is int recordA ? b is int recordB ? $"{recordA} ↔ {recordB}" : recordA.ToString() : "—";
    }

    private static string BuildEvidenceSummary(RcmPinProfile pin)
    {
        static string CaptureSummary(string label, RcmStateEvidence evidence) =>
            $"{label}: {(evidence.Tested ? "CAPTURED" : evidence.NoOtmrData ? "NO OTMR DATA" : "NOT CAPTURED")} | " +
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
