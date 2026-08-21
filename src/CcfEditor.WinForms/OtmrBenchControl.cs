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
    private bool _renderingGrid;
    private bool _updatingConnectorChoices;
    private bool _initialSplitterPositionApplied;
    private OtmrLiveState _otmrLiveState = OtmrLiveState.Disconnected;
    private string? _lastAssignedConnector;
    private string? _pendingConnectorEdit;
    private ComboBox? _activeConnectorEditingControl;

    public OtmrBenchControl()
    {
        InitializeComponent();
        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            RcmProfilePaths.EnsureDefaultFolder();
        UpdateWorkflowTextWidths();
        UpdateWorkflow();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (!IsDisposed)
            UpdateWorkflowTextWidths();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
            ApplyInitialSplitterPosition();
    }

    private void MainSplit_SplitterMoved(object? sender, SplitterEventArgs e) =>
        UpdateWorkflowTextWidths();

    private void ApplyInitialSplitterPosition()
    {
        if (_initialSplitterPositionApplied || mainSplit.Width <= 0)
            return;

        int maximumWorkflowWidth = mainSplit.Width - mainSplit.SplitterWidth - mainSplit.Panel1MinSize;
        int desiredWorkflowWidth = Math.Max(mainSplit.Panel2MinSize, (int)Math.Round(mainSplit.Width * 0.30));
        if (maximumWorkflowWidth >= mainSplit.Panel2MinSize)
        {
            mainSplit.SplitterDistance = mainSplit.Width - mainSplit.SplitterWidth -
                                         Math.Min(desiredWorkflowWidth, maximumWorkflowWidth);
            _initialSplitterPositionApplied = true;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime || !Visible || IsDisposed)
            return;
        RefreshCcfFromHost();
    }

    internal void SetCurrentCcf(CcfDocument? document)
    {
        _document = document;
        UpdateCcfStatus();
        UpdateCommandAvailability();
        RenderTable();
    }

    internal void SetOtmrLiveState(OtmrLiveState state)
    {
        if (_closing || IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => SetOtmrLiveState(state)));
            return;
        }

        _otmrLiveState = state;
        UpdateCommandAvailability();
        if (state != OtmrLiveState.LiveActive && _captureCoordinator.IsCapturing)
            statusLabel.Text = "OTMR live stream is no longer active. No further RCM frames will be accepted.";
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

        if (_otmrLiveState != OtmrLiveState.LiveActive || !_captureCoordinator.AddFrame(timestamp, frame))
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
        PopulateConnectors(resetToAll: true);
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
        SetCurrentCcf((FindForm() as MainForm)?.GetCurrentCcfForBench());
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
            PopulateConnectors(resetToAll: true);
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
            RenderTable();
            statusLabel.Text = $"Connector '{dialog.Value.Trim()}' added. Existing RCM inputs and the current filter are unchanged.";
            await SaveProfileToKnownPathAsync(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot add connector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void RenameConnectorButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null || SpecificConnectorFilter() is not string oldName)
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
            RenderTable();
            await SaveProfileToKnownPathAsync(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot rename connector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void DeleteConnectorButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null || SpecificConnectorFilter() is not string name)
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
        RenderTable();
        await SaveProfileToKnownPathAsync(false);
    }

    private async void EditConnectorPinsButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null)
            return;
        string? connectorName = SpecificConnectorFilter();
        if (connectorName is null && SelectedProfilePin() is { Connector.Length: > 0 } selected)
            connectorName = selected.Connector;
        RcmConnector? connector = connectorName is null ? null : _rcmProfile.Connectors.FirstOrDefault(item =>
            string.Equals(item.Name, connectorName, StringComparison.OrdinalIgnoreCase));
        if (connector is null)
        {
            statusLabel.Text = "Select a connector filter or an assigned input before editing an ordered pin sequence.";
            return;
        }

        using var dialog = new RcmConnectorPinsDialog(connector);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        RcmProfileEditor.SetConnectorOrderedPins(_rcmProfile, connector.Name, dialog.OrderedPins);
        MarkProfileModified();
        statusLabel.Text = dialog.OrderedPins.Count == 0
            ? $"Connector {connector.Name} has no assisted pin sequence; Assign Next Pin will not invent one."
            : $"Connector {connector.Name} pin sequence saved ({dialog.OrderedPins.Count} pins).";
        await SaveProfileToKnownPathAsync(false);
    }

    private async void AddInputButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null)
            return;
        string connector = SpecificConnectorFilter() ?? string.Empty;
        var initial = new RcmInputEdit { Connector = connector };
        using var dialog = new RcmInputEditorDialog("Add RCM Input / Pin", _rcmProfile.Connectors.Select(item => item.Name), initial);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null)
            return;
        try
        {
            RcmPinProfile pin = RcmProfileEditor.AddInput(_rcmProfile, dialog.Result, _document);
            MarkProfileModified();
            PopulateConnectors();
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

    private string? SpecificConnectorFilter()
    {
        string? selected = connectorComboBox.SelectedItem as string;
        return selected is null || string.Equals(selected, RcmInputFilter.All, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(selected, RcmInputFilter.Unassigned, StringComparison.OrdinalIgnoreCase)
            ? null
            : selected;
    }

    private void MarkProfileModified()
    {
        if (_rcmProfile is not null)
            _rcmProfile.LastModifiedTimestamp = DateTimeOffset.Now;
    }

    private void UpdateCcfStatus()
    {
        if (_rcmProfile is not null)
        {
            string loaded = _document is null
                ? "none"
                : Path.GetFileName(_document.SourcePath ?? "opened CCF");
            ccfStatusLabel.Text =
                $"{RcmProfileJson.GetCcfStatus(_rcmProfile, _document)} | loaded {loaded} | " +
                $"profile source {_rcmProfile.SourceCcfFilename} | " +
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

    private void ConnectorComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!_renderingGrid && !_updatingConnectorChoices)
            RenderTable();
    }

    private void RcmGrid_SelectionChanged(object? sender, EventArgs e)
    {
        if (!_renderingGrid)
            UpdateWorkflow();
    }

    private void CaptureVoltageRemovedButton_Click(object? sender, EventArgs e) =>
        BeginCapture(RcmElectricalTestState.VoltageRemoved);

    private void CaptureVoltageAppliedButton_Click(object? sender, EventArgs e) =>
        BeginCapture(RcmElectricalTestState.VoltageApplied24V);

    private void BeginCapture(RcmElectricalTestState state)
    {
        if (_otmrLiveState != OtmrLiveState.LiveActive)
        {
            MessageBox.Show(
                this,
                "OTMR live stream is not active. Connect and Start OTMR Live first.",
                "OTMR live stream required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

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

    private void PopulateConnectors() => PopulateConnectors(resetToAll: false);

    private void PopulateConnectors(bool resetToAll)
    {
        string? previous = resetToAll ? RcmInputFilter.All : connectorComboBox.SelectedItem as string;
        IEnumerable<string> values = _rcmProfile?.Connectors.Select(connector => connector.Name) ?? Enumerable.Empty<string>();
        string[] connectors = values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        connectorComboBox.BeginUpdate();
        _updatingConnectorChoices = true;
        try
        {
            connectorComboBox.Items.Clear();
            connectorComboBox.Items.Add(RcmInputFilter.All);
            connectorComboBox.Items.Add(RcmInputFilter.Unassigned);
            connectorComboBox.Items.AddRange(connectors);
            connectorColumn.Items.Clear();
            connectorColumn.Items.Add(string.Empty);
            connectorColumn.Items.AddRange(connectors);
            string? restored = connectorComboBox.Items.Cast<string>().FirstOrDefault(value =>
                string.Equals(value, previous, StringComparison.OrdinalIgnoreCase));
            if (restored is not null)
                connectorComboBox.SelectedItem = restored;
            else
                connectorComboBox.SelectedIndex = 0;
        }
        finally
        {
            _updatingConnectorChoices = false;
            connectorComboBox.EndUpdate();
        }
    }

    private void RenderTable(Guid? preferredInputId = null)
    {
        if (rcmGrid is null)
            return;

        preferredInputId ??= rcmGrid.CurrentRow?.Tag is Guid selectedId ? selectedId : null;
        string connectorSelection = connectorComboBox.SelectedItem as string ?? RcmInputFilter.All;
        rcmGrid.SuspendLayout();
        _renderingGrid = true;
        DataGridViewRow? selectedRow = null;
        try
        {
            rcmGrid.Rows.Clear();
            if (_rcmProfile is not null)
            {
                foreach (RcmPinProfile pin in RcmInputFilter.Apply(_rcmProfile, connectorSelection))
                {
                    int index = rcmGrid.Rows.Add();
                    DataGridViewRow row = rcmGrid.Rows[index];
                    row.Tag = pin.Id;
                    UpdateGridRow(row, pin);
                    if (pin.Id == preferredInputId)
                        selectedRow = row;
                }
            }
        }
        finally
        {
            _renderingGrid = false;
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

    private void RcmGrid_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_renderingGrid || e.RowIndex < 0 || e.ColumnIndex != connectorColumn.Index)
            return;
        string connector = _pendingConnectorEdit ?? Convert.ToString(e.FormattedValue)?.Trim() ?? string.Empty;
        if (!connectorColumn.Items.Cast<string>().Contains(connector, StringComparer.OrdinalIgnoreCase))
            connectorColumn.Items.Add(connector);
    }

    private async void RcmGrid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        int column = e.ColumnIndex;
        if (_renderingGrid || _rcmProfile is null || e.RowIndex < 0 ||
            (column != connectorColumn.Index && column != pinColumn.Index) ||
            rcmGrid.Rows[e.RowIndex].Tag is not Guid id)
            return;

        try
        {
            DataGridViewRow row = rcmGrid.Rows[e.RowIndex];
            string connector = column == connectorColumn.Index && _pendingConnectorEdit is not null
                ? _pendingConnectorEdit
                : Convert.ToString(row.Cells[connectorColumn.Index].Value)?.Trim() ?? string.Empty;
            string pin = Convert.ToString(row.Cells[pinColumn.Index].Value)?.Trim() ?? string.Empty;
            StopConnectorEditingCapture();
            RcmProfileEditor.AssignPhysical(_rcmProfile, id, connector, pin);
            if (connector.Length > 0)
                _lastAssignedConnector = connector;
            MarkProfileModified();
            statusLabel.Text = _rcmProfile.GetInput(id).PhysicalMappingAssigned
                ? $"Assigned {connector}/{pin}; stable input identity, CCF mapping and evidence preserved."
                : "Physical mapping remains UNASSIGNED until both Connector and Pin are entered.";
            SchedulePhysicalAssignmentUiRefresh(id);
            await SaveProfileToKnownPathAsync(false);
        }
        catch (Exception ex)
        {
            StopConnectorEditingCapture();
            ScheduleRejectedAssignmentRecovery(id, ex.Message);
        }
    }

    private void SchedulePhysicalAssignmentUiRefresh(Guid inputId)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        BeginInvoke((Action)(() => CompletePhysicalAssignmentUiRefresh(inputId)));
    }

    private void CompletePhysicalAssignmentUiRefresh(Guid inputId)
    {
        if (IsDisposed || _rcmProfile is null)
            return;

        PopulateConnectors();
        RcmPinProfile pin = _rcmProfile.GetInput(inputId);
        string filter = connectorComboBox.SelectedItem as string ?? RcmInputFilter.All;
        bool remainsVisible = RcmInputFilter.Apply(_rcmProfile, filter).Any(candidate => candidate.Id == inputId);
        DataGridViewRow? existingRow = rcmGrid.Rows.Cast<DataGridViewRow>()
            .SingleOrDefault(row => row.Tag is Guid id && id == inputId);

        if (!remainsVisible || existingRow is null)
        {
            // Filtering may legitimately remove the edited input. Rebuild only
            // after CellEndEdit has fully unwound, never from inside the edit event.
            RenderTable(remainsVisible ? inputId : null);
            return;
        }

        UpdateGridRow(existingRow, pin);
        existingRow.Selected = true;
        UpdateProgress();
        UpdateWorkflow();
    }

    private void ScheduleRejectedAssignmentRecovery(Guid inputId, string message)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        BeginInvoke((Action)(() =>
        {
            RenderTable(inputId);
            MessageBox.Show(this, message, "Cannot assign physical input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }));
    }

    private void UpdateGridRow(DataGridViewRow row, RcmPinProfile pin)
    {
        row.Cells[connectorColumn.Index].Value = pin.Connector;
        row.Cells[pinColumn.Index].Value = pin.Pin;
        row.Cells[functionColumn.Index].Value = pin.Function;
        row.Cells[expectedCcfColumn.Index].Value = FormatCcfReference(pin.CcfReference);
        row.Cells[voltageRemovedColumn.Index].Value = FormatCaptureState(pin, RcmElectricalTestState.VoltageRemoved);
        row.Cells[voltageAppliedColumn.Index].Value = FormatCaptureState(pin, RcmElectricalTestState.VoltageApplied24V);
        row.Cells[stateDifferenceColumn.Index].Value = FormatDifference(pin);
        row.Cells[decoderColumn.Index].Value = "NOT VERIFIED";
        row.Cells[rcmResultColumn.Index].Value = pin.RcmResult;
        ApplyRowStyle(row, pin);
    }

    private void RcmGrid_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (rcmGrid.CurrentCell?.ColumnIndex == connectorColumn.Index && e.Control is ComboBox combo)
        {
            StopConnectorEditingCapture();
            _activeConnectorEditingControl = combo;
            _pendingConnectorEdit = combo.Text.Trim();
            combo.TextChanged += ConnectorEditingControl_TextChanged;
            combo.DropDownStyle = ComboBoxStyle.DropDown;
            combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            combo.AutoCompleteSource = AutoCompleteSource.ListItems;
        }
    }

    private void ConnectorEditingControl_TextChanged(object? sender, EventArgs e)
    {
        if (sender is ComboBox combo)
            _pendingConnectorEdit = combo.Text.Trim();
    }

    private void StopConnectorEditingCapture()
    {
        if (_activeConnectorEditingControl is not null)
            _activeConnectorEditingControl.TextChanged -= ConnectorEditingControl_TextChanged;
        _activeConnectorEditingControl = null;
        _pendingConnectorEdit = null;
    }

    private async void AssignNextPinButton_Click(object? sender, EventArgs e)
    {
        if (_rcmProfile is null || SelectedProfilePin() is not RcmPinProfile selected)
            return;
        string? connector = selected.PhysicalMappingAssigned ? selected.Connector : _lastAssignedConnector ?? SpecificConnectorFilter();
        if (string.IsNullOrWhiteSpace(connector))
        {
            statusLabel.Text = "Assign a connector first; no connector was inferred.";
            return;
        }
        string? suggestedPin = RcmProfileEditor.SuggestNextUnusedPin(_rcmProfile, connector);
        if (suggestedPin is null)
        {
            statusLabel.Text = $"No pin was assigned: connector {connector} has no configured unused pin in its ordered sequence.";
            return;
        }

        RcmPinProfile? target = selected.PhysicalMappingAssigned ? NextUnassignedAfter(selected.Id) : selected;
        if (target is null)
        {
            statusLabel.Text = "There are no unassigned RCM inputs left.";
            return;
        }
        try
        {
            RcmProfileEditor.AssignPhysical(_rcmProfile, target.Id, connector, suggestedPin);
            _lastAssignedConnector = connector;
            MarkProfileModified();
            PopulateConnectors();
            RenderTable(target.Id);
            statusLabel.Text = $"Assigned next unassigned input to {connector}/{suggestedPin} using its configured pin sequence.";
            await SaveProfileToKnownPathAsync(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot assign next pin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private RcmPinProfile? NextUnassignedAfter(Guid inputId)
    {
        if (_rcmProfile is null || _rcmProfile.Pins.Count == 0)
            return null;
        int start = _rcmProfile.Pins.FindIndex(pin => pin.Id == inputId);
        for (int offset = 1; offset <= _rcmProfile.Pins.Count; offset++)
        {
            RcmPinProfile candidate = _rcmProfile.Pins[(start + offset + _rcmProfile.Pins.Count) % _rcmProfile.Pins.Count];
            if (!candidate.PhysicalMappingAssigned)
                return candidate;
        }
        return null;
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
            selectedPinLabel.Text = "Select an RCM input";
            voltageRemovedInstructionLabel.Text = "SELECT AN INPUT TO CONTINUE";
            voltageAppliedInstructionLabel.Text = "SELECT AN INPUT TO CONTINUE";
            voltageRemovedStatusLabel.Text = "NO INPUT SELECTED";
            voltageAppliedStatusLabel.Text = "NO INPUT SELECTED";
            evidenceTextBox.Text = "Create or open an RCM profile, then select a physical input.";
            UpdateCommandAvailability();
            return;
        }

        string pinKey = pin.DisplayKey;
        RcmCcfReference? ccf = pin.CcfReference;
        selectedPinLabel.Text = BuildSelectedInputSummary(pin, ccf);

        if (!pin.PhysicalMappingAssigned)
        {
            voltageRemovedInstructionLabel.Text = "PHYSICAL MAPPING REQUIRED";
            voltageAppliedInstructionLabel.Text = "PHYSICAL MAPPING REQUIRED";
            voltageRemovedStatusLabel.Text = "PHYSICAL MAPPING REQUIRED";
            voltageAppliedStatusLabel.Text = "PHYSICAL MAPPING REQUIRED";
            evidenceTextBox.Text =
                "PHYSICAL MAPPING REQUIRED\r\n\r\n" +
                "Assign Connector and Pin before performing an RCM electrical test. " +
                "Use the editable grid cells or Edit Selected Input. Voltage capture and comparison remain disabled.";
        }
        else if (!pin.Testable)
        {
            voltageRemovedInstructionLabel.Text = "VOLTAGE CAPTURE DISABLED — NOT TESTABLE";
            voltageAppliedInstructionLabel.Text = "VOLTAGE CAPTURE DISABLED — NOT TESTABLE";
            voltageRemovedStatusLabel.Text = "NOT TESTABLE";
            voltageAppliedStatusLabel.Text = "NOT TESTABLE";
            evidenceTextBox.Text =
                $"NOT TESTABLE\r\n\r\nSafety classification:\r\n{pin.SafetyClassification}\r\n\r\n" +
                "No voltage capture is offered for returns, supplies, RS485, link/termination, or unresolved unsafe points.";
        }
        else
        {
            voltageRemovedInstructionLabel.Text = $"REMOVE TEST VOLTAGE FROM {pinKey}";
            voltageAppliedInstructionLabel.Text = $"APPLY +24 V TO {pinKey}";
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
        bool canCapture = _rcmProfile is not null && pin?.PhysicalMappingAssigned == true && pin.Testable &&
                          _otmrLiveState == OtmrLiveState.LiveActive && !capturing;
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
        renameConnectorButton.Enabled = _rcmProfile is not null && SpecificConnectorFilter() is not null && !capturing;
        deleteConnectorButton.Enabled = renameConnectorButton.Enabled;
        editConnectorPinsButton.Enabled = _rcmProfile is not null &&
                                          (SpecificConnectorFilter() is not null || !string.IsNullOrWhiteSpace(pin?.Connector)) &&
                                          !capturing;
        addInputButton.Enabled = _rcmProfile is not null && !capturing;
        editInputButton.Enabled = pin is not null && !capturing;
        editSelectedWorkflowButton.Enabled = editInputButton.Enabled;
        assignNextPinButton.Enabled = pin is not null && !capturing;
        deleteInputButton.Enabled = pin is not null && !capturing;
    }

    private void UpdateProgress()
    {
        progressLabel.Text = _rcmProfile is null
            ? "RCM Progress: no profile created/opened"
            : $"Logical CCF inputs: {_rcmProfile.LogicalCcfInputCount:N0} | " +
              $"Assigned physical inputs: {_rcmProfile.AssignedPhysicalInputCount:N0} | " +
              $"Unassigned: {_rcmProfile.UnassignedInputCount:N0} | " +
              $"Testable: {_rcmProfile.TestablePinCount:N0} | " +
              $"RCM complete: {_rcmProfile.CompletedTestablePinCount:N0} / {_rcmProfile.TestablePinCount:N0}";
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
        if (!pin.PhysicalMappingAssigned)
            return RcmResultStates.Unassigned;
        if (!pin.Testable)
            return "NOT TESTABLE";
        RcmStateEvidence evidence = state == RcmElectricalTestState.VoltageRemoved
            ? pin.VoltageRemoved
            : pin.VoltageApplied24V;
        if (evidence.Tested)
            return $"CAPTURED | {evidence.FrameCount} frames";
        return evidence.NoOtmrData ? "NO OTMR DATA" : "NOT CAPTURED";
    }

    private static string BuildSelectedInputSummary(RcmPinProfile pin, RcmCcfReference? ccf)
    {
        string summary =
            $"{pin.DisplayKey}\r\n{pin.Function}\r\n" +
            $"Expected CCF records: {FormatRecordPair(ccf)}\r\n" +
            $"Card {ccf?.LogicalCard?.ToString() ?? "—"} / Channel {ccf?.LogicalChannel?.ToString() ?? "—"}";
        if (pin.PhysicalMappingAssigned)
            return summary;

        return summary +
               $"\r\n\r\nPhysical mapping:\r\n" +
               $"Connector: {(string.IsNullOrWhiteSpace(pin.Connector) ? "NOT ASSIGNED" : pin.Connector)}\r\n" +
               $"Pin: {(string.IsNullOrWhiteSpace(pin.Pin) ? "NOT ASSIGNED" : pin.Pin)}";
    }

    private void UpdateWorkflowTextWidths()
    {
        if (mainSplit is null || workflowLayout is null || selectedPinLabel is null)
            return;

        int summaryWidth = Math.Max(160, mainSplit.Panel2.ClientSize.Width - workflowLayout.Padding.Horizontal - 22);
        int instructionWidth = Math.Max(140, summaryWidth - 24);
        selectedPinLabel.MaximumSize = new Size(summaryWidth, 0);
        voltageRemovedInstructionLabel.MaximumSize = new Size(instructionWidth, 0);
        voltageAppliedInstructionLabel.MaximumSize = new Size(instructionWidth, 0);
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

    private static void ApplyRowStyle(DataGridViewRow row, RcmPinProfile pin)
    {
        // Rows are normally updated in place after physical assignment, so reset
        // both colours before applying the current state.
        row.DefaultCellStyle.ForeColor = Color.Black;
        if (!pin.PhysicalMappingAssigned)
        {
            row.DefaultCellStyle.BackColor = Color.LemonChiffon;
            row.DefaultCellStyle.ForeColor = Color.Black;
        }
        else if (!pin.Testable)
        {
            row.DefaultCellStyle.BackColor = Color.Gainsboro;
            row.DefaultCellStyle.ForeColor = Color.DimGray;
        }
        else if (pin.RcmResult == RcmResultStates.RawDifferenceFound)
        {
            row.DefaultCellStyle.BackColor = Color.LightCyan;
        }
        else if (pin.RcmResult == RcmResultStates.BothStatesCaptured)
        {
            row.DefaultCellStyle.BackColor = Color.LightGoldenrodYellow;
        }
        else if (pin.RcmResult is RcmResultStates.VoltageRemovedCaptured or RcmResultStates.VoltageApplied24VCaptured)
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
