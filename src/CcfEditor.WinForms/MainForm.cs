using System.Buffers.Binary;
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

    public MainForm()
    {
        InitializeComponent();

        recordsGrid.DataSource = _recordsBinding;
        headerGrid.DataSource = _headerBinding;
        hexGrid.DataSource = _hexBinding;
    }

    private void OpenMenuItem_Click(object? sender, EventArgs e)
    {
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

        string baseName = Path.GetFileNameWithoutExtension(_document.SourcePath ?? "ccf");
        string suggested = $"{baseName}_NOEDIT_{DateTime.Now:yyyyMMdd_HHmmss}.ccf";

        using var dialog = new SaveFileDialog
        {
            Filter = "CCF files (*.ccf)|*.ccf|All files (*.*)|*.*",
            Title = "Save byte-identical no-edit copy",
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
                $"Original SHA-256:{Environment.NewLine}{verification.OriginalSha256}{Environment.NewLine}{Environment.NewLine}" +
                $"Output SHA-256:{Environment.NewLine}{verification.OutputSha256}{Environment.NewLine}{Environment.NewLine}" +
                $"Byte-identical: {verification.OutputMatchesWorkingBytes}{Environment.NewLine}" +
                $"No-edit SHA matches original: {verification.NoEditShaMatchesOriginal}";

            MessageBox.Show(
                this,
                result,
                verification.NoEditShaMatchesOriginal ? "Save verified" : "Save verification FAILED",
                MessageBoxButtons.OK,
                verification.NoEditShaMatchesOriginal ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExitMenuItem_Click(object? sender, EventArgs e) => Close();

    private void SearchTextBox_TextChanged(object? sender, EventArgs e) => ApplyRecordFilter();

    private void RecordsGrid_SelectionChanged(object? sender, EventArgs e)
    {
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

    private void LoadCcf(string path)
    {
        CcfDocument document = CcfParser.Load(path);
        _document = document;

        _allRecordRows = document.Records.Select(ToRecordRow).ToList();
        _hexRows = BuildHexRows(document.GetWorkingBytesSnapshot());

        searchTextBox.Text = string.Empty;
        _recordsBinding.DataSource = _allRecordRows;
        _headerBinding.DataSource = BuildHeaderRows(document);
        _hexBinding.DataSource = _hexRows;

        PopulateValidation(document);
        fileStatusLabel.Text = $"{Path.GetFileName(path)}  |  {document.Length:N0} bytes  |  {document.Records.Count} records";
        shaStatusLabel.Text = $"SHA-256: {document.OriginalSha256}";
        saveCopyMenuItem.Enabled = true;
        saveCopyButton.Enabled = true;

        if (_allRecordRows.Count > 0)
            SelectRecord(0, scrollRecordsGrid: true);
    }

    private void ApplyRecordFilter()
    {
        string filter = searchTextBox.Text.Trim();
        if (filter.Length == 0)
        {
            _recordsBinding.DataSource = _allRecordRows;
            return;
        }

        string needle = filter.ToUpperInvariant();
        List<RecordGridRow> filtered = _allRecordRows.Where(row =>
            row.Record.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            row.EventIndex.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            row.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            row.Pair.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            row.OffText.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            row.OnText.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            row.Card.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            row.Channel.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _recordsBinding.DataSource = filtered;
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
                    recordsGrid.CurrentCell = recordsGrid.Rows[i].Cells[0];
                    if (i >= 0 && i < recordsGrid.RowCount)
                        recordsGrid.FirstDisplayedScrollingRowIndex = i;
                    break;
                }
            }
        }
    }

    private void HighlightHexRecord(int recordIndex)
    {
        int start = CcfConstants.GetRecordOffset(recordIndex);
        int endExclusive = start + CcfConstants.RecordSize;
        int firstHighlightedRow = -1;

        foreach (DataGridViewRow gridRow in hexGrid.Rows)
        {
            if (gridRow.DataBoundItem is not HexLineRow line)
                continue;

            bool overlaps = line.OffsetValue < endExclusive && line.OffsetValue + 16 > start;
            gridRow.DefaultCellStyle.BackColor = overlaps ? Color.LightGoldenrodYellow : Color.White;
            if (overlaps && firstHighlightedRow < 0)
                firstHighlightedRow = gridRow.Index;
        }

        if (firstHighlightedRow >= 0 && firstHighlightedRow < hexGrid.RowCount)
            hexGrid.FirstDisplayedScrollingRowIndex = firstHighlightedRow;
    }

    private void PopulateValidation(CcfDocument document)
    {
        validationList.Items.Clear();
        IReadOnlyList<CcfValidationIssue> issues = CcfValidator.ValidateMilestone1(document);

        validationList.Items.Add(new ListViewItem(new[]
        {
            document.IsByteIdenticalToOriginal ? "Information" : "Error",
            $"Original SHA-256 = {document.OriginalSha256}; Working SHA-256 = {document.WorkingSha256}; identical = {document.IsByteIdenticalToOriginal}."
        }));

        foreach (CcfValidationIssue issue in issues)
        {
            validationList.Items.Add(new ListViewItem(new[] { issue.Severity.ToString(), issue.Message }));
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

        AddHeader(rows, bytes, 0x002E, 2, "Profile / family word", $"0x{document.Header.ProfileFamilyWord:X4}");
        AddHeader(rows, bytes, 0x01B0, 8, "Firmware / version", AsciiPreview(bytes, 0x01B0, 8));
        AddHeader(rows, bytes, 0x01B8, 8, "Serial core", AsciiPreview(bytes, 0x01B8, 8));
        AddHeader(rows, bytes, 0x01C0, 10, "Vehicle", AsciiPreview(bytes, 0x01C0, 10));
        AddHeader(rows, bytes, 0x01CA, 7, "Unit", AsciiPreview(bytes, 0x01CA, 7));
        AddHeader(rows, bytes, 0x01D1, 8, "Vehicle type", AsciiPreview(bytes, 0x01D1, 8));
        AddHeader(rows, bytes, 0x01F9, 1, "Cards fitted", bytes[0x01F9].ToString());
        AddHeader(rows, bytes, 0x01FA, 8, "JP6", "Raw array");
        AddHeader(rows, bytes, 0x0202, 8, "JP12", "Raw array");
        AddHeader(rows, bytes, 0x0228, 4, "Mileage", "Raw only - field width/encoding not assumed");
        AddHeader(rows, bytes, 0x022C, 2, "Wheel 1", BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x022C, 2)).ToString());
        AddHeader(rows, bytes, 0x022E, 2, "Wheel 2", BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x022E, 2)).ToString());
        AddHeader(rows, bytes, 0x0230, 1, "Pulses / rev", bytes[0x0230].ToString());
        AddHeader(rows, bytes, 0x0231, 1, "Unknown", bytes[0x0231].ToString());
        AddHeader(rows, bytes, 0x025C, 1, "Poll", bytes[0x025C].ToString());
        AddHeader(rows, bytes, 0x025D, 1, "No-event", bytes[0x025D].ToString());
        AddHeader(rows, bytes, 0x025E, 1, "Sample count", bytes[0x025E].ToString());
        AddHeader(rows, bytes, 0x025F, 2, "Distance trigger", BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x025F, 2)).ToString());
        AddHeader(rows, bytes, 0x0261, 2, "Mid-journey", BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x0261, 2)).ToString());

        return rows;
    }

    private static void AddHeader(List<HeaderFieldRow> rows, byte[] bytes, int offset, int length, string meaning, string decoded)
    {
        rows.Add(new HeaderFieldRow
        {
            Offset = $"0x{offset:X4}",
            Meaning = meaning,
            RawHex = Convert.ToHexString(bytes.AsSpan(offset, length)),
            Decoded = decoded
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
