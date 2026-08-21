using System.ComponentModel;
using CcfEditor.Core;
using CcfEditor.Otmr.Bench;
using CcfEditor.Otmr.Live;

namespace CcfEditor.WinForms;

public partial class OtmrBenchControl : UserControl
{
    private readonly List<OtmrBenchPinDefinition> _definitions = new();
    private readonly Dictionary<string, BenchObservation> _observations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OtmrBenchObservationSession> _rawObservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OtmrBenchObservationSession> _completedSessions = new();
    private CcfDocument? _document;
    private string? _armedKey;
    private OtmrBenchObservationSession? _activeSession;
    private OtmrBenchObservationSession? _lastCompletedSession;
    private string? _profilePath;
    private int _totalRawFrameCount;

    public OtmrBenchControl()
    {
        InitializeComponent();

        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
        {
            LoadBundledProfile();
        }
        else
        {
            profileStatusLabel.Text = "Pin map: design-time preview";
            decoderStatusLabel.Text = "Live record detection: runtime only";
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            return;

        if (!Visible || IsDisposed || ccfStatusLabel is null || benchGrid is null)
            return;

        RefreshCcfFromHost();
    }

    public void ReportLiveSignalActivity(OtmrBenchLiveActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => ReportLiveSignalActivity(activity)));
            return;
        }

        if (_armedKey is not null)
        {
            AddObservation(_armedKey, activity);
        }
        else
        {
            foreach (OtmrBenchPinDefinition definition in _definitions.Where(definition =>
                         definition.ExpectedRecordA == activity.RecordIndex ||
                         definition.ExpectedRecordB == activity.RecordIndex))
            {
                AddObservation(Key(definition), activity);
            }
        }

        RenderConnector(_armedKey);
        decoderStatusLabel.Text = "Live record detection: VERIFIED DECODED EVENT RECEIVED";
        statusLabel.Text = _armedKey is null
            ? $"Observed decoded record {activity.RecordIndex}."
            : $"Armed pin {_armedKey}: observed decoded record {activity.RecordIndex}.";
    }

    public void ReportRawLiveFrame(DateTimeOffset timestamp, OtmrLiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => ReportRawLiveFrame(timestamp, frame)));
            return;
        }

        _totalRawFrameCount++;
        if (_armedKey is null)
        {
            decoderStatusLabel.Text = $"Live framing: {_totalRawFrameCount} complete raw frame(s); event decoder not proven";
            return;
        }

        if (_activeSession is null)
        {
            statusLabel.Text = $"ARMED {_armedKey}: no active raw observation session is available.";
            return;
        }

        _activeSession.AddFrame(timestamp, frame);
        OtmrBenchObservationSession observation = _activeSession;
        string armedKey = _armedKey;
        RenderConnector(armedKey);
        decoderStatusLabel.Text = "Live record detection: ARMED - RAW OBSERVATION; event decoder not proven";
        statusLabel.Text =
            $"ARMED {armedKey}: received complete raw frame #{observation.FrameCount} at " +
            $"{timestamp.ToLocalTime():HH:mm:ss.fff}. No record/value mapping has been claimed.";
    }

    private void AddObservation(string key, OtmrBenchLiveActivity activity)
    {
        if (!_observations.TryGetValue(key, out BenchObservation? observation))
        {
            observation = new BenchObservation();
            _observations.Add(key, observation);
        }

        observation.Add(activity);
    }

    private void LoadBundledProfile()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Profiles",
            "Class171",
            "Class171_Bench_PinMap.tsv");

        if (!File.Exists(path))
        {
            profileStatusLabel.Text = "Pin map: bundled Class 171 profile not found";
            statusLabel.Text = $"Expected external pin map was not found: {path}";
            return;
        }

        try
        {
            LoadProfile(path);
        }
        catch (Exception ex)
        {
            profileStatusLabel.Text = "Pin map: load failed";
            statusLabel.Text = ex.Message;
        }
    }

    private void LoadProfile(string path)
    {
        IReadOnlyList<OtmrBenchPinDefinition> loaded = OtmrBenchProfileReader.LoadTsv(path);
        _definitions.Clear();
        _definitions.AddRange(loaded);
        _profilePath = path;
        _armedKey = null;
        _observations.Clear();
        _rawObservations.Clear();
        _completedSessions.Clear();
        _activeSession = null;
        _lastCompletedSession = null;
        _totalRawFrameCount = 0;
        armSelectedButton.Enabled = true;
        stopObservationButton.Enabled = false;
        saveObservationButton.Enabled = false;

        string? previousConnector = connectorComboBox.SelectedItem as string;
        string[] connectors = _definitions
            .Select(definition => definition.Connector)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        connectorComboBox.BeginUpdate();
        try
        {
            connectorComboBox.Items.Clear();
            connectorComboBox.Items.AddRange(connectors);
            if (previousConnector is not null && connectors.Contains(previousConnector, StringComparer.OrdinalIgnoreCase))
                connectorComboBox.SelectedItem = connectors.First(value => string.Equals(value, previousConnector, StringComparison.OrdinalIgnoreCase));
            else if (connectors.Length > 0)
                connectorComboBox.SelectedIndex = 0;
        }
        finally
        {
            connectorComboBox.EndUpdate();
        }

        int j1 = _definitions.Count(definition => string.Equals(definition.Connector, "J1", StringComparison.OrdinalIgnoreCase));
        int j2 = _definitions.Count(definition => string.Equals(definition.Connector, "J2", StringComparison.OrdinalIgnoreCase));
        profileStatusLabel.Text = $"External pin map: {Path.GetFileName(path)} | J1 {j1} rows | J2 {j2} rows";
        RenderConnector();
    }

    private void LoadProfileButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "OTMR bench pin maps (*.tsv)|*.tsv|All files (*.*)|*.*",
            Title = "Load external OTMR bench pin map",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            LoadProfile(dialog.FileName);
            statusLabel.Text = "External bench pin map loaded.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to load bench pin map", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ConnectorComboBox_SelectedIndexChanged(object? sender, EventArgs e) => RenderConnector();

    private void RefreshCcfButton_Click(object? sender, EventArgs e) => RefreshCcfFromHost();

    private void RefreshCcfFromHost()
    {
        _document = (FindForm() as MainForm)?.GetCurrentCcfForBench();
        ccfStatusLabel.Text = _document is null
            ? "CCF: none loaded"
            : $"CCF: {Path.GetFileName(_document.SourcePath ?? "opened CCF")} | working bytes";
        RenderConnector();
    }

    private void ArmSelectedButton_Click(object? sender, EventArgs e)
    {
        if (benchGrid.CurrentRow?.Tag is not OtmrBenchPinDefinition definition)
        {
            MessageBox.Show(this, "Select a connector pin row first.", "OTMR I/O Bench", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!definition.IsVoltageTestPoint)
        {
            MessageBox.Show(
                this,
                $"{definition.Connector}-{definition.Pin} is not classified as a voltage-test input.{Environment.NewLine}{Environment.NewLine}" +
                definition.SafetyInstruction,
                "Do not stimulate this pin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _armedKey = Key(definition);
        _observations.Remove(_armedKey);
        _activeSession = new OtmrBenchObservationSession(
            definition.Connector,
            definition.Pin,
            definition.ExpectedFunction,
            definition.ExpectedRecordA,
            definition.ExpectedRecordB,
            DateTimeOffset.Now);
        _rawObservations[_armedKey] = _activeSession;
        armSelectedButton.Enabled = false;
        stopObservationButton.Enabled = true;
        saveObservationButton.Enabled = false;
        RenderConnector(_armedKey);
        decoderStatusLabel.Text = "Live record detection: ARMED - RAW OBSERVATION; event decoder not proven";
        statusLabel.Text =
            $"ARMED {_armedKey} | {definition.ExpectedFunction} | Expected records: {FormatExpectedRecords(definition)} | " +
            "Waiting for live transition...";
    }

    private void StopObservationButton_Click(object? sender, EventArgs e)
    {
        string? stoppedKey = _armedKey;
        OtmrBenchObservationSession? stoppedSession = _activeSession;
        if (stoppedSession is not null)
        {
            stoppedSession.Stop(DateTimeOffset.Now);
            _completedSessions.Add(stoppedSession);
            _lastCompletedSession = stoppedSession;
        }

        _activeSession = null;
        _armedKey = null;
        armSelectedButton.Enabled = true;
        stopObservationButton.Enabled = false;
        saveObservationButton.Enabled = _completedSessions.Count > 0;
        RenderConnector(stoppedKey);
        decoderStatusLabel.Text = "Live framing active; event decoder not proven";
        statusLabel.Text = stoppedSession is null
            ? "Pin observation stopped."
            : $"Observation stopped: {stoppedSession.Connector}-{stoppedSession.Pin}, " +
              $"{stoppedSession.FrameCount} complete raw frame(s) retained. Use Save Observation Session...";
    }

    private async void SaveObservationButton_Click(object? sender, EventArgs e)
    {
        OtmrBenchObservationSession? session = _lastCompletedSession;
        if (session is null)
        {
            MessageBox.Show(
                this,
                "Stop an armed observation session before saving it.",
                "OTMR I/O Bench",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "OTMR observation session JSON Lines (*.jsonl)|*.jsonl",
            DefaultExt = "jsonl",
            AddExtension = true,
            FileName = $"OTMR_OBSERVATION_{session.ArmTimestamp:yyyyMMdd_HHmmss}_{session.Connector}-{session.Pin}.jsonl",
            Title = "Save controlled OTMR raw observation session"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            await OtmrBenchObservationSessionWriter.WriteJsonLinesAsync(dialog.FileName, session);
            _completedSessions.Remove(session);
            _lastCompletedSession = _completedSessions.LastOrDefault();
            saveObservationButton.Enabled = _lastCompletedSession is not null && _activeSession is null;
            statusLabel.Text =
                $"Saved raw-only observation session {session.Connector}-{session.Pin}: " +
                $"{session.FrameCount} complete frame(s). No protocol meaning was inferred. " +
                $"Pending unsaved sessions: {_completedSessions.Count}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Unable to save observation session",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ClearObservationsButton_Click(object? sender, EventArgs e)
    {
        _observations.Clear();
        _rawObservations.Clear();
        _completedSessions.Clear();
        _activeSession = null;
        _lastCompletedSession = null;
        _armedKey = null;
        _totalRawFrameCount = 0;
        armSelectedButton.Enabled = true;
        stopObservationButton.Enabled = false;
        saveObservationButton.Enabled = false;
        RenderConnector();
        decoderStatusLabel.Text = "Live framing ready; event decoder not proven";
        statusLabel.Text = "Bench observations cleared.";
    }

    private void BenchGrid_SelectionChanged(object? sender, EventArgs e) => UpdateDetails();

    private void RenderConnector(string? preferredSelectionKey = null)
    {
        string connector = connectorComboBox.SelectedItem as string ?? string.Empty;
        preferredSelectionKey ??= benchGrid.CurrentRow?.Tag is OtmrBenchPinDefinition selected
            ? Key(selected)
            : null;
        DataGridViewRow? rowToSelect = null;
        benchGrid.SuspendLayout();
        try
        {
            benchGrid.Rows.Clear();
            foreach (OtmrBenchPinDefinition definition in _definitions.Where(definition =>
                         string.Equals(definition.Connector, connector, StringComparison.OrdinalIgnoreCase)))
            {
                AddDefinitionRow(definition);
                DataGridViewRow addedRow = benchGrid.Rows[^1];
                if (string.Equals(Key(definition), preferredSelectionKey, StringComparison.OrdinalIgnoreCase))
                    rowToSelect = addedRow;
            }
        }
        finally
        {
            benchGrid.ResumeLayout();
        }

        if (string.Equals(connector, "J2", StringComparison.OrdinalIgnoreCase))
        {
            coverageLabel.Text =
                "J2 STATUS: mapping is deliberately incomplete. Only current bench candidate pins are shown until the full source pin map is imported. " +
                "Do not infer missing J2 pins or card↔MIO ordering.";
        }
        else
        {
            coverageLabel.Text =
                "J1 STATUS: rows come from the external Class 171 bench pin map. Red/grey rows are return, supply, RS485, link or unresolved points — not 24 V digital test inputs.";
        }

        if (rowToSelect is not null)
        {
            benchGrid.ClearSelection();
            rowToSelect.Selected = true;
            benchGrid.CurrentCell = rowToSelect.Cells[0];
        }

        UpdateDetails();
    }

    private void AddDefinitionRow(OtmrBenchPinDefinition definition)
    {
        string key = Key(definition);
        _observations.TryGetValue(key, out BenchObservation? observation);
        _rawObservations.TryGetValue(key, out OtmrBenchObservationSession? rawObservation);
        bool isArmed = string.Equals(_armedKey, key, StringComparison.OrdinalIgnoreCase);

        int rowIndex = benchGrid.Rows.Add(
            definition.Pin,
            definition.Role,
            definition.Mio,
            definition.Channel,
            definition.ExpectedFunction,
            definition.SafetyInstruction,
            FormatExpectedRecords(definition),
            FormatExpectedCcf(definition),
            BuildCurrentCcfSummary(definition),
            EvaluateCcf(definition),
            BuildObservedSummary(observation, rawObservation),
            observation?.LastValue ?? (rawObservation?.FrameCount > 0 ? $"RAW #{rawObservation.FrameCount}" : string.Empty),
            EvaluateObservation(definition, observation, rawObservation, isArmed));

        DataGridViewRow row = benchGrid.Rows[rowIndex];
        row.Tag = definition;
        ApplyRowStyle(row, definition, observation, rawObservation, isArmed);
    }

    private static string FormatExpectedRecords(OtmrBenchPinDefinition definition)
    {
        if (definition.ExpectedRecordA is not int a)
            return "—";
        return definition.ExpectedRecordB is int b ? $"{a} ↔ {b}" : a.ToString();
    }

    private static string FormatExpectedCcf(OtmrBenchPinDefinition definition)
    {
        if (definition.ExpectedCard is not int card || definition.ExpectedChannel is not int channel)
            return "—";
        return $"card {card} / ch {channel}";
    }

    private string BuildCurrentCcfSummary(OtmrBenchPinDefinition definition)
    {
        if (_document is null)
            return "No CCF loaded";
        if (definition.ExpectedRecordA is not int recordIndex)
            return "No fixed record — discover from live data";
        if ((uint)recordIndex >= (uint)_document.Records.Count)
            return $"Record {recordIndex} outside CCF";

        CcfRecord record = _document.Records[recordIndex];
        string pair = record.PairRecord.HasValue ? record.PairRecord.Value.ToString() : "—";
        return $"rec {recordIndex}: {record.Name} | c{record.Card}/ch{record.Channel} | type {record.Type} | pair {pair}";
    }

    private string EvaluateCcf(OtmrBenchPinDefinition definition)
    {
        if (!definition.IsVoltageTestPoint)
            return "NOT TESTABLE";
        if (_document is null)
            return "NO CCF";
        if (definition.ExpectedRecordA is not int recordIndex)
            return "UNMAPPED";
        if ((uint)recordIndex >= (uint)_document.Records.Count)
            return "BAD RECORD";

        CcfRecord record = _document.Records[recordIndex];
        bool matches = true;
        if (definition.ExpectedCard is int card)
            matches &= record.Card == card;
        if (definition.ExpectedChannel is int channel)
            matches &= record.Channel == channel;
        if (definition.ExpectedRecordB is int pair)
            matches &= record.Type == 2 && record.PairRecord.HasValue && record.PairRecord.Value == pair;

        return matches ? "STRUCTURE MATCH" : "CCF CHECK";
    }

    private string BuildObservedSummary(BenchObservation? observation, OtmrBenchObservationSession? rawObservation)
    {
        if (observation is null || observation.Records.Count == 0)
            return rawObservation?.FrameCount > 0
                ? $"No decoded record | raw frames: {rawObservation.FrameCount}"
                : "—";

        return string.Join(", ", observation.Records.OrderBy(value => value).Select(recordIndex =>
        {
            if (_document is not null && (uint)recordIndex < (uint)_document.Records.Count)
            {
                CcfRecord record = _document.Records[recordIndex];
                return $"{recordIndex}:{record.Name} c{record.Card}/ch{record.Channel}";
            }
            return recordIndex.ToString();
        }));
    }

    private static string EvaluateObservation(
        OtmrBenchPinDefinition definition,
        BenchObservation? observation,
        OtmrBenchObservationSession? rawObservation,
        bool isArmed)
    {
        if (!definition.IsVoltageTestPoint)
            return "NOT TESTABLE";
        if (observation is null || observation.Records.Count == 0)
        {
            if (rawObservation?.FrameCount > 0)
                return isArmed ? "ARMED - RAW OBSERVATION" : "RAW OBSERVATION STOPPED";
            return isArmed ? "ARMED - WAITING" : "WAITING";
        }
        if (definition.ExpectedRecordA is not int expectedA)
            return observation.Records.Count == 1 ? "DISCOVERED" : "DISCOVERED MULTIPLE";

        var allowed = new HashSet<int> { expectedA };
        if (definition.ExpectedRecordB is int expectedB)
            allowed.Add(expectedB);

        bool anyExpected = observation.Records.Any(allowed.Contains);
        bool unexpected = observation.Records.Any(record => !allowed.Contains(record));

        if (anyExpected && !unexpected)
            return "LIVE MATCH";
        if (unexpected)
            return "MISMATCH / EXTRA";
        return "NO EXPECTED EVENT";
    }

    private void ApplyRowStyle(
        DataGridViewRow row,
        OtmrBenchPinDefinition definition,
        BenchObservation? observation,
        OtmrBenchObservationSession? rawObservation,
        bool isArmed)
    {
        string result = EvaluateObservation(definition, observation, rawObservation, isArmed);

        if (!definition.IsVoltageTestPoint)
        {
            row.DefaultCellStyle.BackColor = Color.Gainsboro;
            row.Cells[safetyColumn.Index].Style.BackColor = Color.MistyRose;
            row.Cells[safetyColumn.Index].Style.ForeColor = Color.DarkRed;
        }
        else if (isArmed)
        {
            row.DefaultCellStyle.BackColor = Color.LightGoldenrodYellow;
        }
        else if (result == "LIVE MATCH")
        {
            row.DefaultCellStyle.BackColor = Color.Honeydew;
        }
        else if (result.StartsWith("MISMATCH", StringComparison.Ordinal))
        {
            row.DefaultCellStyle.BackColor = Color.MistyRose;
        }
        else if (result.StartsWith("DISCOVERED", StringComparison.Ordinal))
        {
            row.DefaultCellStyle.BackColor = Color.AliceBlue;
        }
        else
        {
            row.DefaultCellStyle.BackColor = Color.White;
        }
    }

    private void UpdateDetails()
    {
        if (benchGrid.CurrentRow?.Tag is not OtmrBenchPinDefinition definition)
        {
            detailsTextBox.Text = "Select a pin row to see mapping and safety details.";
            return;
        }

        string key = Key(definition);
        _observations.TryGetValue(key, out BenchObservation? observation);
        _rawObservations.TryGetValue(key, out OtmrBenchObservationSession? rawObservation);
        bool isArmed = string.Equals(_armedKey, key, StringComparison.OrdinalIgnoreCase);
        DateTimeOffset? latestTimestamp = LatestTimestamp(observation, rawObservation);
        string lastActivity = latestTimestamp?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";
        string armedSummary = isArmed
            ? $"ARMED {key}\r\n{definition.ExpectedFunction}\r\nExpected records: {FormatExpectedRecords(definition)}\r\nWaiting for live transition...\r\n\r\n"
            : string.Empty;
        string rawSummary = rawObservation?.FrameCount > 0
            ? $"Raw frames since arming: {rawObservation.FrameCount}\r\n" +
              $"Last raw frame: {rawObservation.LastRawHex}\r\n" +
              $"{rawObservation.LastCandidateRawDelta}\r\n" +
              $"Session armed: {rawObservation.ArmTimestamp:O}\r\n" +
              $"Session stopped: {(rawObservation.StopTimestamp.HasValue ? rawObservation.StopTimestamp.Value.ToString("O") : "ACTIVE")}\r\n" +
              "Decoder: no pin/record mapping proven by the capture\r\n"
            : "Raw frames since arming: 0\r\nLast raw frame: —\r\nCANDIDATE RAW DELTA: —\r\n";

        detailsTextBox.Text =
            armedSummary +
            $"PIN\r\n{definition.Connector}-{definition.Pin}\r\n\r\n" +
            $"ROLE / FUNCTION\r\n{definition.Role} | MIO {definition.Mio} | channel {definition.Channel}\r\n{definition.ExpectedFunction}\r\n\r\n" +
            $"RETURN / PAIR\r\n{definition.ReturnOrPair}\r\n\r\n" +
            $"SAFETY\r\n{definition.SafetyInstruction}\r\n\r\n" +
            $"REFERENCE MAPPING\r\nRecords: {FormatExpectedRecords(definition)} | {FormatExpectedCcf(definition)}\r\n{definition.EvidenceStatus}\r\n\r\n" +
            $"CURRENT OPENED CCF\r\n{BuildCurrentCcfSummary(definition)}\r\nCheck: {EvaluateCcf(definition)}\r\n\r\n" +
            $"LIVE OBSERVATION\r\n{BuildObservedSummary(observation, rawObservation)}\r\n" +
            $"Last value: {observation?.LastValue ?? (rawObservation?.FrameCount > 0 ? $"RAW #{rawObservation.FrameCount}" : "—")}\r\n" +
            $"Last activity: {lastActivity}\r\n" +
            $"Result: {EvaluateObservation(definition, observation, rawObservation, isArmed)}\r\n" +
            rawSummary + "\r\n" +
            $"SOURCE / NOTES\r\n{definition.Source}\r\n\r\n" +
            $"PROFILE FILE\r\n{_profilePath ?? "—"}";
    }

    private static DateTimeOffset? LatestTimestamp(
        BenchObservation? observation,
        OtmrBenchObservationSession? rawObservation)
    {
        if (observation is null)
            return rawObservation?.LastTimestamp;
        if (rawObservation?.LastTimestamp is not DateTimeOffset rawTimestamp)
            return observation.LastTimestamp;
        return observation.LastTimestamp >= rawTimestamp
            ? observation.LastTimestamp
            : rawTimestamp;
    }

    private static string Key(OtmrBenchPinDefinition definition) => $"{definition.Connector}-{definition.Pin}";

    private sealed class BenchObservation
    {
        public HashSet<int> Records { get; } = new();
        public DateTimeOffset LastTimestamp { get; private set; }
        public string LastValue { get; private set; } = string.Empty;
        public int? LastCard { get; private set; }
        public int? LastChannel { get; private set; }
        public string LastRawHex { get; private set; } = string.Empty;

        public void Add(OtmrBenchLiveActivity activity)
        {
            Records.Add(activity.RecordIndex);
            LastTimestamp = activity.Timestamp;
            LastValue = activity.StateOrValue;
            LastCard = activity.Card;
            LastChannel = activity.Channel;
            LastRawHex = activity.RawHex;
        }
    }

}
