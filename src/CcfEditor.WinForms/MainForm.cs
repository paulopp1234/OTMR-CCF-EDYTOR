using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using CcfEditor.Core;
using CcfEditor.WinForms.Models;

namespace CcfEditor.WinForms;

public partial class MainForm : Form
{
    private readonly BindingSource _recordsBinding = new();
    private readonly BindingSource _headerBinding = new();
    private readonly BindingSource _hexBinding = new();

    private CcfDocument? _document;
    private List<RecordGridRow> _allRecordRows = new();
    private List<HexLineRow> _hexRows = new();
    private int? _selectedRecordIndex;
    private bool _isRefreshingUi;

    public MainForm()
    {
        InitializeComponent();

        recordsGrid.DataSource = _recordsBinding;
        headerGrid.DataSource = _headerBinding;
        hexGrid.DataSource = _hexBinding;
        otmrLiveControl.SetCurrentCcf(_document);
        otmrBenchControl.SetCurrentCcf(_document);
        otmrLiveControl.GenuineLiveFrameReceived += OtmrLiveControl_GenuineLiveFrameReceived;
        otmrLiveControl.GenuineLiveSessionStarted += OtmrLiveControl_GenuineLiveSessionStarted;
        otmrBenchControl.CurrentRcmProfileChanged += OtmrBenchControl_CurrentRcmProfileChanged;
        otmrRcmLiveControl.VerifiedLiveStateDecoded += OtmrRcmLiveControl_VerifiedLiveStateDecoded;
        ReportApplicationPresenceContext();
    }

    private void OpenMenuItem_Click(object? sender, EventArgs e)
    {
        if (_document?.IsModified == true && !ConfirmDiscardChanges())
            return;

        using var dialog = new OpenFileDialog
        {
            Filter = "CCF files (*.ccf)|*.ccf|All files (*.*)|*.*",
            Title = "Open CCF",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            LoadCcf(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to open CCF", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveCopyMenuItem_Click(object? sender, EventArgs e)
    {
        if (_document is null)
            return;

        int changedBytes = _document.GetByteChanges().Count;
        string baseName = Path.GetFileNameWithoutExtension(_document.SourcePath ?? "ccf");
        string suffix = changedBytes == 0 ? "COPY" : "EDITED";
        string suggested = $"{baseName}_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss}.ccf";

        using var dialog = new SaveFileDialog
        {
            Filter = "CCF files (*.ccf)|*.ccf|All files (*.*)|*.*",
            Title = "Save CCF As",
            FileName = suggested,
            AddExtension = true,
            DefaultExt = "ccf",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            SaveVerification verification = CcfFileService.SaveAs(_document, dialog.FileName);
            string result =
                $"Saved: {verification.OutputPath}{Environment.NewLine}{Environment.NewLine}" +
                $"Changed bytes versus opened file: {changedBytes}{Environment.NewLine}{Environment.NewLine}" +
                $"Original SHA-256:{Environment.NewLine}{verification.OriginalSha256}{Environment.NewLine}{Environment.NewLine}" +
                $"Working SHA-256:{Environment.NewLine}{verification.WorkingSha256}{Environment.NewLine}{Environment.NewLine}" +
                $"Saved SHA-256:{Environment.NewLine}{verification.OutputSha256}{Environment.NewLine}{Environment.NewLine}" +
                $"Saved file matches working bytes: {verification.OutputMatchesWorkingBytes}";

            MessageBox.Show(
                this,
                result,
                verification.OutputMatchesWorkingBytes ? "Save verified" : "Save verification FAILED",
                MessageBoxButtons.OK,
                verification.OutputMatchesWorkingBytes ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExitMenuItem_Click(object? sender, EventArgs e) => Close();

    private void SearchTextBox_TextChanged(object? sender, EventArgs e)
    {
        if (!_isRefreshingUi)
            ApplyRecordFilter();
    }

    private void RecordsGrid_SelectionChanged(object? sender, EventArgs e)
    {
        if (_isRefreshingUi)
            return;

        if (recordsGrid.CurrentRow?.DataBoundItem is not RecordGridRow row || _document is null)
            return;

        SelectRecord(row.Record, scrollRecordsGrid: false);
    }

    private void JumpToPairButton_Click(object? sender, EventArgs e)
    {
        if (_document is null || _selectedRecordIndex is null)
            return;

        CcfRecord record = _document.Records[_selectedRecordIndex.Value];
        if (record.PairRecord is not ushort pair || pair >= CcfConstants.RecordCount)
            return;

        SelectRecord(pair, scrollRecordsGrid: true);
    }

    private void RecordsGrid_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (_isRefreshingUi || _document is null || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            e.Cancel = true;
            return;
        }

        if (recordsGrid.Rows[e.RowIndex].DataBoundItem is not RecordGridRow row)
        {
            e.Cancel = true;
            return;
        }

        string property = recordsGrid.Columns[e.ColumnIndex].DataPropertyName;
        if (property is nameof(RecordGridRow.Pair) or nameof(RecordGridRow.OffText) or nameof(RecordGridRow.OnText))
        {
            if (_document.Records[row.Record].Type != 2)
            {
                e.Cancel = true;
                fileStatusLabel.Text = $"Record {row.Record}: OFF/ON/pair editing is only valid for digital type 2 records.";
            }
        }
    }

    private void RecordsGrid_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_isRefreshingUi || _document is null || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        if (recordsGrid.Rows[e.RowIndex].DataBoundItem is not RecordGridRow row)
            return;

        string property = recordsGrid.Columns[e.ColumnIndex].DataPropertyName;
        string value = e.FormattedValue?.ToString() ?? string.Empty;

        try
        {
            ValidateRecordCell(row.Record, property, value);
            recordsGrid.Rows[e.RowIndex].ErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            e.Cancel = true;
            recordsGrid.Rows[e.RowIndex].ErrorText = ex.Message;
        }
    }

    private void RecordsGrid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_isRefreshingUi || _document is null || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        recordsGrid.Rows[e.RowIndex].ErrorText = string.Empty;
        if (recordsGrid.Rows[e.RowIndex].DataBoundItem is not RecordGridRow row)
            return;

        string property = recordsGrid.Columns[e.ColumnIndex].DataPropertyName;
        string value = Convert.ToString(recordsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture) ?? string.Empty;

        try
        {
            ApplyRecordEdit(row.Record, property, value);
            RefreshAfterEdit(row.Record);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Record edit rejected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshAfterEdit(row.Record);
        }
    }

    private void RecordsGrid_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        if (e.RowIndex >= 0 && e.RowIndex < recordsGrid.Rows.Count)
            recordsGrid.Rows[e.RowIndex].ErrorText = e.Exception?.Message ?? "Invalid value.";
    }

    private void HeaderGrid_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (_isRefreshingUi || _document is null || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            e.Cancel = true;
            return;
        }

        if (headerGrid.Rows[e.RowIndex].DataBoundItem is not HeaderFieldRow row ||
            headerGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(HeaderFieldRow.Decoded) ||
            !row.Editable)
        {
            e.Cancel = true;
        }
    }

    private void HeaderGrid_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_isRefreshingUi || _document is null || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        if (headerGrid.Rows[e.RowIndex].DataBoundItem is not HeaderFieldRow row ||
            headerGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(HeaderFieldRow.Decoded) ||
            !row.Editable)
            return;

        string value = e.FormattedValue?.ToString() ?? string.Empty;
        try
        {
            ValidateHeaderCell(row, value);
            headerGrid.Rows[e.RowIndex].ErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            e.Cancel = true;
            headerGrid.Rows[e.RowIndex].ErrorText = ex.Message;
        }
    }

    private void HeaderGrid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_isRefreshingUi || _document is null || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        headerGrid.Rows[e.RowIndex].ErrorText = string.Empty;
        if (headerGrid.Rows[e.RowIndex].DataBoundItem is not HeaderFieldRow row || !row.Editable)
            return;

        string value = Convert.ToString(headerGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        try
        {
            ApplyHeaderEdit(row, value);
            RefreshAfterEdit(_selectedRecordIndex);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Header edit rejected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshAfterEdit(_selectedRecordIndex);
        }
    }

    private void HeaderGrid_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        if (e.RowIndex >= 0 && e.RowIndex < headerGrid.Rows.Count)
            headerGrid.Rows[e.RowIndex].ErrorText = e.Exception?.Message ?? "Invalid value.";
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_document?.IsModified == true && !ConfirmDiscardChanges())
            e.Cancel = true;
    }

    private bool ConfirmDiscardChanges()
    {
        if (_document?.IsModified != true)
            return true;

        int changedBytes = _document.GetByteChanges().Count;
        DialogResult result = MessageBox.Show(
            this,
            $"The opened CCF has unsaved working changes ({changedBytes} changed bytes).{Environment.NewLine}{Environment.NewLine}Discard them?",
            "Unsaved CCF changes",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        return result == DialogResult.Yes;
    }

    private void LoadCcf(string path)
    {
        CcfDocument document = CcfParser.Load(path);
        _document = document;
        _selectedRecordIndex = null;

        searchTextBox.Text = string.Empty;
        saveCopyMenuItem.Enabled = true;
        saveCopyButton.Enabled = true;

        RefreshAfterEdit(0);
    }

    private void ApplyRecordFilter()
    {
        string filter = searchTextBox.Text.Trim();
        if (filter.Length == 0)
        {
            _recordsBinding.DataSource = _allRecordRows;
            return;
        }

        List<RecordGridRow> filtered = _allRecordRows.Where(row =>
            row.Record.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.EventIndex.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.ColourHex.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.Pair.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.OffText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.OnText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.Card.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            row.Channel.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _recordsBinding.DataSource = filtered;
    }

    private void RefreshAfterEdit(int? recordIndex)
    {
        if (_document is null)
            return;

        _isRefreshingUi = true;
        try
        {
            _allRecordRows = _document.Records.Select(ToRecordRow).ToList();
            _hexRows = BuildHexRows(_document.GetWorkingBytesSnapshot());

            ApplyRecordFilter();
            _headerBinding.DataSource = BuildHeaderRows(_document);
            _hexBinding.DataSource = _hexRows;

            PopulateValidation(_document);
            UpdateStatus(_document);
        }
        finally
        {
            _isRefreshingUi = false;
        }

        if (recordIndex.HasValue && recordIndex.Value >= 0 && recordIndex.Value < CcfConstants.RecordCount)
            SelectRecord(recordIndex.Value, scrollRecordsGrid: true);

        // The bench owns no independent CCF lifecycle. Push the host's current
        // document after every load/edit refresh so its status and profile
        // creation source cannot lag behind MainForm.
        otmrLiveControl.SetCurrentCcf(_document);
        otmrBenchControl.SetCurrentCcf(_document);
        ReportApplicationPresenceContext();
    }

    private void UpdateStatus(CcfDocument document)
    {
        int changes = document.GetByteChanges().Count;
        string modified = changes == 0 ? "Unmodified" : $"MODIFIED: {changes} byte(s)";
        fileStatusLabel.Text = $"{Path.GetFileName(document.SourcePath)}  |  {document.Length:N0} bytes  |  {document.Records.Count} records  |  {modified}";
        shaStatusLabel.Text = $"Working SHA-256: {document.WorkingSha256}";
    }

    private void SelectRecord(int recordIndex, bool scrollRecordsGrid)
    {
        if (_document is null || (uint)recordIndex >= CcfConstants.RecordCount)
            return;

        _selectedRecordIndex = recordIndex;
        CcfRecord record = _document.Records[recordIndex];

        detailRecordTextBox.Text = $"Physical {record.PhysicalIndex} / event {record.EventIndex}";
        detailTypeTextBox.Text = $"Type {record.Type} / flag {record.ClassificationFlag}";
        detailNameTextBox.Text = record.Name;
        detailPairTextBox.Text = record.PairRecord?.ToString() ?? "-";
        detailCardChannelTextBox.Text = $"Card {record.Card} / channel {record.Channel}";
        detailLoggerFunctionTextBox.Text = $"Logger {record.LoggerMode} / function {record.HardwareFunction}";
        detailOffTextBox.Text = record.OffDescription ?? "-";
        detailOnTextBox.Text = record.OnDescription ?? "-";
        detailOffsetTextBox.Text = $"0x{record.Offset:X4}";
        detailRawTextBox.Text = FormatBytes(record.GetRawBytes());
        jumpToPairButton.Enabled = record.PairRecord.HasValue && record.PairRecord.Value <= 255;

        HighlightHexRecord(recordIndex);

        if (scrollRecordsGrid)
        {
            for (int i = 0; i < recordsGrid.Rows.Count; i++)
            {
                if (recordsGrid.Rows[i].DataBoundItem is RecordGridRow row && row.Record == recordIndex)
                {
                    recordsGrid.ClearSelection();
                    recordsGrid.Rows[i].Selected = true;
                    DataGridViewCell firstCell = recordsGrid.Rows[i].Cells[0];
                    if (DataGridViewViewport.CanDisplayRows(recordsGrid) && recordsGrid.Rows[i].Visible && firstCell.Visible)
                        recordsGrid.CurrentCell = firstCell;
                    DataGridViewViewport.TryScrollToRow(recordsGrid, i);
                    break;
                }
            }
        }
    }

    private void HighlightHexRecord(int recordIndex)
    {
        if (_document is null)
            return;

        int start = CcfConstants.GetRecordOffset(recordIndex);
        int endExclusive = start + CcfConstants.RecordSize;
        int firstHighlightedRow = -1;
        HashSet<int> changedOffsets = _document.GetByteChanges().Select(change => change.Offset).ToHashSet();

        foreach (DataGridViewRow gridRow in hexGrid.Rows)
        {
            if (gridRow.DataBoundItem is not HexLineRow line)
                continue;

            bool selectedRecord = line.OffsetValue < endExclusive && line.OffsetValue + 16 > start;
            bool changed = false;
            int lineEnd = Math.Min(line.OffsetValue + 16, _document.Length);
            for (int offset = line.OffsetValue; offset < lineEnd; offset++)
            {
                if (changedOffsets.Contains(offset))
                {
                    changed = true;
                    break;
                }
            }

            gridRow.DefaultCellStyle.BackColor = selectedRecord && changed
                ? Color.Orange
                : selectedRecord
                    ? Color.LightGoldenrodYellow
                    : changed
                        ? Color.MistyRose
                        : Color.White;

            if (selectedRecord && firstHighlightedRow < 0)
                firstHighlightedRow = gridRow.Index;
        }

        DataGridViewViewport.TryScrollToRow(hexGrid, firstHighlightedRow);
    }

    private void PopulateValidation(CcfDocument document)
    {
        validationList.Items.Clear();
        IReadOnlyList<CcfValidationIssue> issues = CcfValidator.ValidateMilestone1(document);
        int changedBytes = document.GetByteChanges().Count;

        validationList.Items.Add(new ListViewItem(new[]
        {
            changedBytes == 0 ? "Information" : "Modified",
            $"Original SHA-256 = {document.OriginalSha256}; Working SHA-256 = {document.WorkingSha256}; changed bytes = {changedBytes}."
        }));

        foreach (CcfValidationIssue issue in issues)
        {
            validationList.Items.Add(new ListViewItem(new[] { issue.Severity.ToString(), issue.Message }));
        }
    }

    private static void ValidateRecordCell(int recordIndex, string property, string value)
    {
        _ = recordIndex;
        switch (property)
        {
            case nameof(RecordGridRow.Type):
            case nameof(RecordGridRow.Card):
            case nameof(RecordGridRow.Channel):
            case nameof(RecordGridRow.LoggerMode):
            case nameof(RecordGridRow.HardwareFunction):
                if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new FormatException("Value must be an integer from 0 to 255.");
                break;

            case nameof(RecordGridRow.Pair):
                if (!ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort pair) || pair >= CcfConstants.RecordCount)
                    throw new FormatException("Pair record must be an integer from 0 to 255.");
                break;

            case nameof(RecordGridRow.Name):
                CcfEditService.ValidateFixedAscii(value, CcfFieldDefinitions.Record.NameLength);
                break;

            case nameof(RecordGridRow.OffText):
                CcfEditService.ValidateFixedAscii(value, CcfFieldDefinitions.Digital.OffDescriptionLength);
                break;

            case nameof(RecordGridRow.OnText):
                CcfEditService.ValidateFixedAscii(value, CcfFieldDefinitions.Digital.OnDescriptionLength);
                break;

            case nameof(RecordGridRow.ColourHex):
                _ = CcfEditService.ParseFourByteHex(value);
                break;
        }
    }

    private void ApplyRecordEdit(int recordIndex, string property, string value)
    {
        if (_document is null)
            return;

        switch (property)
        {
            case nameof(RecordGridRow.Type):
                CcfEditService.SetRecordType(_document, recordIndex, byte.Parse(value, CultureInfo.InvariantCulture));
                break;
            case nameof(RecordGridRow.Name):
                CcfEditService.SetRecordName(_document, recordIndex, value);
                break;
            case nameof(RecordGridRow.ColourHex):
                CcfEditService.SetRecordColourHex(_document, recordIndex, value);
                break;
            case nameof(RecordGridRow.Card):
                CcfEditService.SetRecordCard(_document, recordIndex, byte.Parse(value, CultureInfo.InvariantCulture));
                break;
            case nameof(RecordGridRow.Channel):
                CcfEditService.SetRecordChannel(_document, recordIndex, byte.Parse(value, CultureInfo.InvariantCulture));
                break;
            case nameof(RecordGridRow.LoggerMode):
                CcfEditService.SetRecordLoggerMode(_document, recordIndex, byte.Parse(value, CultureInfo.InvariantCulture));
                break;
            case nameof(RecordGridRow.HardwareFunction):
                CcfEditService.SetRecordHardwareFunction(_document, recordIndex, byte.Parse(value, CultureInfo.InvariantCulture));
                break;
            case nameof(RecordGridRow.Pair):
                CcfEditService.SetDigitalPairRecord(_document, recordIndex, ushort.Parse(value, CultureInfo.InvariantCulture));
                break;
            case nameof(RecordGridRow.OffText):
                CcfEditService.SetDigitalOffDescription(_document, recordIndex, value);
                break;
            case nameof(RecordGridRow.OnText):
                CcfEditService.SetDigitalOnDescription(_document, recordIndex, value);
                break;
        }
    }

    private static void ValidateHeaderCell(HeaderFieldRow row, string value)
    {
        switch (row.EditKind)
        {
            case HeaderEditKind.Ascii:
                CcfEditService.ValidateFixedAscii(value, row.Length);
                break;
            case HeaderEditKind.Byte:
                if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new FormatException("Value must be an integer from 0 to 255.");
                break;
            case HeaderEditKind.UInt16:
                if (!ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new FormatException("Value must be an integer from 0 to 65535.");
                break;
            default:
                throw new InvalidOperationException("This header field is read-only because its encoding is not sufficiently proven.");
        }
    }

    private void ApplyHeaderEdit(HeaderFieldRow row, string value)
    {
        if (_document is null)
            return;

        switch (row.EditKind)
        {
            case HeaderEditKind.Ascii:
                CcfEditService.SetHeaderAscii(_document, row.OffsetValue, row.Length, value);
                break;
            case HeaderEditKind.Byte:
                CcfEditService.SetHeaderByte(_document, row.OffsetValue, byte.Parse(value, CultureInfo.InvariantCulture));
                break;
            case HeaderEditKind.UInt16:
                CcfEditService.SetHeaderUInt16(_document, row.OffsetValue, ushort.Parse(value, CultureInfo.InvariantCulture));
                break;
            default:
                throw new InvalidOperationException("This header field is read-only.");
        }
    }

    private static RecordGridRow ToRecordRow(CcfRecord record)
    {
        return new RecordGridRow
        {
            Record = record.PhysicalIndex,
            EventIndex = record.EventIndex,
            Type = record.Type,
            Flag = record.ClassificationFlag,
            Name = record.Name,
            ColourHex = Convert.ToHexString(record.GetRawBytes(CcfFieldDefinitions.Record.Colour, CcfFieldDefinitions.Record.ColourLength)),
            Card = record.Card,
            Channel = record.Channel,
            LoggerMode = record.LoggerMode,
            HardwareFunction = record.HardwareFunction,
            Pair = record.PairRecord?.ToString() ?? string.Empty,
            OffText = record.OffDescription ?? string.Empty,
            OnText = record.OnDescription ?? string.Empty,
            RawOffset = $"0x{record.Offset:X4}"
        };
    }

    private static List<HexLineRow> BuildHexRows(byte[] bytes)
    {
        var rows = new List<HexLineRow>((bytes.Length + 15) / 16);
        for (int offset = 0; offset < bytes.Length; offset += 16)
        {
            int count = Math.Min(16, bytes.Length - offset);
            ReadOnlySpan<byte> span = bytes.AsSpan(offset, count);
            string hex = string.Join(" ", span.ToArray().Select(b => b.ToString("X2")));
            string ascii = new string(span.ToArray().Select(b => b is >= 32 and <= 126 ? (char)b : '.').ToArray());
            rows.Add(new HexLineRow
            {
                OffsetValue = offset,
                Offset = $"0x{offset:X4}",
                Hex = hex,
                Ascii = ascii
            });
        }
        return rows;
    }

    private static List<HeaderFieldRow> BuildHeaderRows(CcfDocument document)
    {
        byte[] bytes = document.GetWorkingBytesSnapshot();
        var rows = new List<HeaderFieldRow>();

        AddHeader(rows, bytes, 0x002E, 2, "Profile / family word", $"0x{document.Header.ProfileFamilyWord:X4}", HeaderEditKind.None);
        AddHeader(rows, bytes, 0x01B0, 8, "Firmware / version", AsciiPreview(bytes, 0x01B0, 8), HeaderEditKind.Ascii);
        AddHeader(rows, bytes, 0x01B8, 8, "Serial core", AsciiPreview(bytes, 0x01B8, 8), HeaderEditKind.Ascii);
        AddHeader(rows, bytes, 0x01C0, 10, "Vehicle", AsciiPreview(bytes, 0x01C0, 10), HeaderEditKind.Ascii);
        AddHeader(rows, bytes, 0x01CA, 7, "Unit", AsciiPreview(bytes, 0x01CA, 7), HeaderEditKind.Ascii);
        AddHeader(rows, bytes, 0x01D1, 8, "Vehicle type", AsciiPreview(bytes, 0x01D1, 8), HeaderEditKind.Ascii);
        AddHeader(rows, bytes, 0x01F9, 1, "Cards fitted", bytes[0x01F9].ToString(CultureInfo.InvariantCulture), HeaderEditKind.Byte);
        AddHeader(rows, bytes, 0x01FA, 8, "JP6", "Raw array", HeaderEditKind.None);
        AddHeader(rows, bytes, 0x0202, 8, "JP12", "Raw array", HeaderEditKind.None);
        AddHeader(rows, bytes, 0x0228, 4, "Mileage", "Raw only - field width/encoding not assumed", HeaderEditKind.None);
        AddHeader(rows, bytes, 0x022C, 2, "Wheel 1", BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x022C, 2)).ToString(CultureInfo.InvariantCulture), HeaderEditKind.UInt16);
        AddHeader(rows, bytes, 0x022E, 2, "Wheel 2", BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x022E, 2)).ToString(CultureInfo.InvariantCulture), HeaderEditKind.UInt16);
        AddHeader(rows, bytes, 0x0230, 1, "Pulses / rev", bytes[0x0230].ToString(CultureInfo.InvariantCulture), HeaderEditKind.Byte);
        AddHeader(rows, bytes, 0x0231, 1, "Unknown", bytes[0x0231].ToString(CultureInfo.InvariantCulture), HeaderEditKind.None);
        AddHeader(rows, bytes, 0x025C, 1, "Poll", bytes[0x025C].ToString(CultureInfo.InvariantCulture), HeaderEditKind.Byte);
        AddHeader(rows, bytes, 0x025D, 1, "No-event", bytes[0x025D].ToString(CultureInfo.InvariantCulture), HeaderEditKind.Byte);
        AddHeader(rows, bytes, 0x025E, 1, "Sample count", bytes[0x025E].ToString(CultureInfo.InvariantCulture), HeaderEditKind.Byte);
        AddHeader(rows, bytes, 0x025F, 2, "Distance trigger", BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x025F, 2)).ToString(CultureInfo.InvariantCulture), HeaderEditKind.UInt16);
        AddHeader(rows, bytes, 0x0261, 2, "Mid-journey", BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x0261, 2)).ToString(CultureInfo.InvariantCulture), HeaderEditKind.UInt16);

        return rows;
    }

    private static void AddHeader(List<HeaderFieldRow> rows, byte[] bytes, int offset, int length, string meaning, string decoded, HeaderEditKind editKind)
    {
        rows.Add(new HeaderFieldRow
        {
            Offset = $"0x{offset:X4}",
            OffsetValue = offset,
            Length = length,
            Meaning = meaning,
            RawHex = Convert.ToHexString(bytes.AsSpan(offset, length)),
            Decoded = decoded,
            EditKind = editKind
        });
    }

    private static string AsciiPreview(byte[] bytes, int offset, int length)
    {
        ReadOnlySpan<byte> span = bytes.AsSpan(offset, length);
        int nul = span.IndexOf((byte)0);
        if (nul >= 0)
            span = span[..nul];

        var builder = new StringBuilder(span.Length);
        foreach (byte b in span)
            builder.Append(b is >= 32 and <= 126 ? (char)b : '.');
        return builder.ToString();
    }

    private static string FormatBytes(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 3);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
                builder.Append(' ');
            builder.Append(bytes[i].ToString("X2"));
        }
        return builder.ToString();
    }
}
