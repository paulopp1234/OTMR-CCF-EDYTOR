using CcfEditor.Core;
using CcfEditor.Otmr.Bench;

namespace CcfEditor.WinForms;

public partial class OtmrBenchControl : UserControl
{
    private readonly List<OtmrBenchPinDefinition> _definitions = new();
    private readonly Dictionary<string, BenchObservation> _observations = new(StringComparer.OrdinalIgnoreCase);
    private CcfDocument? _document;
    private string? _armedKey;
    private string? _profilePath;

    public OtmrBenchControl()
    {
        InitializeComponent();
        LoadBundledProfile();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && !IsDisposed)
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

        RenderConnector();
        statusLabel.Text = _armedKey is null
            ? $"Observed decoded record {activity.RecordIndex}."
            : $"Armed pin {_armedKey}: observed decoded record {activity.RecordIndex}.";
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
        stopObservationButton.Enabled = true;
        RenderConnector();
        statusLabel.Text =
            $"ARMED {_armedKey} ({definition.ExpectedFunction}). Observation only — the application does not apply voltage. " +
            "Automatic record capture will become active when the verified OTMR live decoder is connected.";
    }

    private void StopObservationButton_Click(object? sender, EventArgs e)
    {
        _armedKey = null;
        stopObservationButton.Enabled = false;
        RenderConnector();
        statusLabel.Text = "Pin observation stopped.";
    }

    private void ClearObservationsButton_Click(object? sender, EventArgs e)
    {
        _observations.Clear();
        _armedKey = null;
        stopObservationButton.Enabled = false;
        RenderConnector();
        statusLabel.Text = "Bench observations cleared.";
    }

    private void BenchGrid_SelectionChanged(object? sender, EventArgs e) => UpdateDetails();

    private void RenderConnector()
    {
        string connector = connectorComboBox.SelectedItem as string ?? string.Empty;
        benchGrid.SuspendLayout();
        try
        {
            benchGrid.Rows.Clear();
            foreach (OtmrBenchPinDefinition definition in _definitions.Where(definition =>
                         string.Equals(definition.Connector, connector, StringComparison.OrdinalIgnoreCase)))
            {
                AddDefinitionRow(definition);
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

        UpdateDetails();
    }

    private void AddDefinitionRow(OtmrBenchPinDefinition definition)
    {
        string key = Key(definition);
        _observations.TryGetValue(key, out BenchObservation? observation);

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
            BuildObservedSummary(observation),
            observation?.LastValue ?? string.Empty,
            EvaluateObservation(definition, observation));

        DataGridViewRow row = benchGrid.Rows[rowIndex];
        row.Tag = definition;
        ApplyRowStyle(row, definition, observation);
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

    private string BuildObservedSummary(BenchObservation? observation)
    {
        if (observation is null || observation.Records.Count == 0)
            return "—";

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

    private static string EvaluateObservation(OtmrBenchPinDefinition definition, BenchObservation? observation)
    {
        if (!definition.IsVoltageTestPoint)
            return "NOT TESTABLE";
        if (observation is null || observation.Records.Count == 0)
            return "WAITING";
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

    private void ApplyRowStyle(DataGridViewRow row, OtmrBenchPinDefinition definition, BenchObservation? observation)
    {
        string key = Key(definition);
        string result = EvaluateObservation(definition, observation);

        if (!definition.IsVoltageTestPoint)
        {
            row.DefaultCellStyle.BackColor = Color.Gainsboro;
            row.Cells[safetyColumn.Index].Style.BackColor = Color.MistyRose;
            row.Cells[safetyColumn.Index].Style.ForeColor = Color.DarkRed;
        }
        else if (string.Equals(_armedKey, key, StringComparison.OrdinalIgnoreCase))
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

        _observations.TryGetValue(Key(definition), out BenchObservation? observation);
        string lastActivity = observation is null
            ? "—"
            : observation.LastTimestamp.ToLocalTime().ToString("HH:mm:ss.fff");

        detailsTextBox.Text =
            $"PIN\r\n{definition.Connector}-{definition.Pin}\r\n\r\n" +
            $"ROLE / FUNCTION\r\n{definition.Role} | MIO {definition.Mio} | channel {definition.Channel}\r\n{definition.ExpectedFunction}\r\n\r\n" +
            $"RETURN / PAIR\r\n{definition.ReturnOrPair}\r\n\r\n" +
            $"SAFETY\r\n{definition.SafetyInstruction}\r\n\r\n" +
            $"REFERENCE MAPPING\r\nRecords: {FormatExpectedRecords(definition)} | {FormatExpectedCcf(definition)}\r\n{definition.EvidenceStatus}\r\n\r\n" +
            $"CURRENT OPENED CCF\r\n{BuildCurrentCcfSummary(definition)}\r\nCheck: {EvaluateCcf(definition)}\r\n\r\n" +
            $"LIVE OBSERVATION\r\n{BuildObservedSummary(observation)}\r\nLast value: {observation?.LastValue ?? "—"}\r\nLast activity: {lastActivity}\r\nResult: {EvaluateObservation(definition, observation)}\r\n\r\n" +
            $"SOURCE / NOTES\r\n{definition.Source}\r\n\r\n" +
            $"PROFILE FILE\r\n{_profilePath ?? "—"}";
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
