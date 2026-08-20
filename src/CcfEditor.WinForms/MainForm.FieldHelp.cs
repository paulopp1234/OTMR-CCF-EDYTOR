using CcfEditor.Core;
using CcfEditor.WinForms.Models;

namespace CcfEditor.WinForms;

public partial class MainForm
{
    private void RecordsGrid_CellEnter(object? sender, DataGridViewCellEventArgs e)
    {
        if (_isRefreshingUi || _document is null || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        if (recordsGrid.Rows[e.RowIndex].DataBoundItem is not RecordGridRow row)
            return;

        string property = recordsGrid.Columns[e.ColumnIndex].DataPropertyName;
        UpdateRecordFieldHelp(row, property);
    }

    private void UpdateRecordFieldHelp(RecordGridRow row, string property)
    {
        if (_document is null || (uint)row.Record >= CcfConstants.RecordCount)
            return;

        CcfRecord record = _document.Records[row.Record];
        string field;
        string bytes;
        string editable;
        string current;
        string description;

        switch (property)
        {
            case nameof(RecordGridRow.Record):
                field = "Record";
                bytes = $"Record starts at 0x{record.Offset:X4}";
                editable = "No";
                current = row.Record.ToString();
                description = "Physical CCF record slot, from 0 to 255. Each record is exactly 100 bytes. This value is calculated from the record position and is not written as a separate editable field.";
                break;

            case nameof(RecordGridRow.EventIndex):
                field = "Event";
                bytes = DescribeBytes(record, CcfFieldDefinitions.Record.EventIndex, 2);
                editable = "No";
                current = row.EventIndex.ToString();
                description = "16-bit event/index value stored at the beginning of the record. It normally matches the physical record number, but the editor does not assume that it always must.";
                break;

            case nameof(RecordGridRow.Type):
                field = "Type";
                bytes = DescribeBytes(record, CcfFieldDefinitions.Record.Type, 1);
                editable = "Yes";
                current = row.Type.ToString();
                description = "Record type byte. Proven/common values include 0 = inactive/dummy/paired slot, 1 = analogue, 2 = digital, 3 = frequency and 8 = PWM. Other values exist and are not all fully decoded. Changing Type can change how the 64-byte type-dependent area is interpreted, so edit with care.";
                break;

            case nameof(RecordGridRow.Flag):
                field = "Flag";
                bytes = DescribeBytes(record, CcfFieldDefinitions.Record.ClassificationFlag, 1);
                editable = "No";
                current = row.Flag.ToString();
                description = "Classification/display-related byte. Its exact Arrowvale semantics are not yet sufficiently proven, so it is shown from the opened CCF but kept read-only.";
                break;

            case nameof(RecordGridRow.Name):
                field = "Name";
                bytes = DescribeBytes(record, CcfFieldDefinitions.Record.Name, CcfFieldDefinitions.Record.NameLength);
                editable = "Yes";
                current = row.Name;
                description = "Signal name stored in a fixed 16-byte ASCII field. The editor allows up to 15 ASCII characters, then writes a NUL terminator and zero-pads the remaining bytes.";
                break;

            case nameof(RecordGridRow.ColourHex):
                field = "Colour raw";
                bytes = DescribeBytes(record, CcfFieldDefinitions.Record.Colour, CcfFieldDefinitions.Record.ColourLength);
                editable = "Yes";
                current = row.ColourHex;
                description = "Four raw colour bytes from the opened CCF. Enter exactly 8 hexadecimal digits, for example FF99CC00. The byte location is proven; the exact RGB/reserved interpretation of every byte is not treated as proven yet.";
                break;

            case nameof(RecordGridRow.Card):
                field = "Card";
                bytes = DescribeBytes(record, CcfFieldDefinitions.Record.Card, 1);
                editable = "Yes";
                current = row.Card.ToString();
                description = "Logical card number stored in the record. It is zero-based in the CCF. Do not automatically assume that logical card numbering maps sequentially to a particular physical MIO module.";
                break;

            case nameof(RecordGridRow.Channel):
                field = "Channel";
                bytes = DescribeBytes(record, CcfFieldDefinitions.Record.Channel, 1);
                editable = "Yes";
                current = row.Channel.ToString();
                description = "Logical channel number on the configured card. It is zero-based in the CCF, so channel 0 is the first logical channel.";
                break;

            case nameof(RecordGridRow.LoggerMode):
                field = "Logger";
                bytes = DescribeBytes(record, CcfFieldDefinitions.Record.LoggerMode, 1);
                editable = "Yes";
                current = row.LoggerMode.ToString();
                description = "Logger-mode byte stored by the CCF. The byte location is proven, but the full meaning of every numeric mode is not yet mapped, so the editor displays and edits the raw numeric value without inventing labels.";
                break;

            case nameof(RecordGridRow.HardwareFunction):
                field = "Function";
                bytes = DescribeBytes(record, CcfFieldDefinitions.Record.HardwareFunction, 1);
                editable = "Yes";
                current = row.HardwareFunction.ToString();
                description = "Hardware-function byte. The field position is proven, while the complete numeric function-code table is not yet proven. The editor therefore shows the actual stored number rather than guessing a meaning.";
                break;

            case nameof(RecordGridRow.Pair):
                field = "Pair";
                bytes = record.Type == 2
                    ? DescribeBytes(record, CcfFieldDefinitions.Digital.PairRecord, 2)
                    : "Type-dependent; not a Pair field for this type";
                editable = record.Type == 2 ? "Yes - digital Type 2 only" : "No for this record type";
                current = record.Type == 2 ? row.Pair : "N/A";
                description = record.Type == 2
                    ? "For a digital Type 2 record, this 16-bit value identifies the paired/opposite-edge event record. Valid record numbers are 0 to 255."
                    : "The bytes at +0x38 are type-dependent. Pair is defined only for digital Type 2 records; for numeric types the same area has another meaning such as the units string.";
                break;

            case nameof(RecordGridRow.OffText):
                field = "OFF text";
                bytes = record.Type == 2
                    ? DescribeBytes(record, CcfFieldDefinitions.Digital.OffDescription, CcfFieldDefinitions.Digital.OffDescriptionLength)
                    : "Type-dependent; not OFF text for this type";
                editable = record.Type == 2 ? "Yes - digital Type 2 only" : "No for this record type";
                current = record.Type == 2 ? row.OffText : "N/A";
                description = record.Type == 2
                    ? "Text associated with the digital LOW/OFF state. Fixed 16-byte ASCII field; maximum 15 ASCII characters plus NUL termination."
                    : "This field interpretation is valid only for digital Type 2 records. The same bytes have different meanings for other record types.";
                break;

            case nameof(RecordGridRow.OnText):
                field = "ON text";
                bytes = record.Type == 2
                    ? DescribeBytes(record, CcfFieldDefinitions.Digital.OnDescription, CcfFieldDefinitions.Digital.OnDescriptionLength)
                    : "Type-dependent; not ON text for this type";
                editable = record.Type == 2 ? "Yes - digital Type 2 only" : "No for this record type";
                current = record.Type == 2 ? row.OnText : "N/A";
                description = record.Type == 2
                    ? "Text associated with the digital HIGH/ON state. Fixed 16-byte ASCII field; maximum 15 ASCII characters plus NUL termination."
                    : "This field interpretation is valid only for digital Type 2 records. The same bytes have different meanings for other record types.";
                break;

            case nameof(RecordGridRow.RawOffset):
                field = "Offset";
                bytes = "Calculated from record position";
                editable = "No";
                current = row.RawOffset;
                description = "Absolute file offset where this 100-byte record begins. Formula: 0x03E8 + (Record × 100). It is calculated from the opened file structure.";
                break;

            default:
                field = recordsGrid.CurrentCell?.OwningColumn?.HeaderText ?? "Field";
                bytes = "-";
                editable = "-";
                current = Convert.ToString(recordsGrid.CurrentCell?.Value) ?? string.Empty;
                description = "No schema description is available for this column yet.";
                break;
        }

        fieldHelpTextBox.Text =
            $"FIELD\r\n{field}\r\n\r\n" +
            $"CURRENT VALUE - FROM OPENED CCF\r\n{current}\r\n\r\n" +
            $"CCF BYTES\r\n{bytes}\r\n\r\n" +
            $"EDITABLE\r\n{editable}\r\n\r\n" +
            $"DESCRIPTION / SCHEMA HELP\r\n{description}";
    }

    private static string DescribeBytes(CcfRecord record, int relativeOffset, int length)
    {
        int absoluteStart = record.Offset + relativeOffset;
        int absoluteEnd = absoluteStart + length - 1;
        string relative = length == 1
            ? $"+0x{relativeOffset:X2}"
            : $"+0x{relativeOffset:X2}..+0x{relativeOffset + length - 1:X2}";
        string absolute = length == 1
            ? $"0x{absoluteStart:X4}"
            : $"0x{absoluteStart:X4}..0x{absoluteEnd:X4}";

        return $"{relative} in record  |  absolute {absolute}  |  {length} byte(s)";
    }
}
