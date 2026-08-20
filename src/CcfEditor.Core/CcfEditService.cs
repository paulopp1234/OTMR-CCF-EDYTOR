using System.Buffers.Binary;

namespace CcfEditor.Core;

public static class CcfEditService
{
    public static void SetRecordName(CcfDocument document, int recordIndex, string value)
    {
        CcfRecord record = GetRecord(document, recordIndex);
        CcfText.WriteFixedAscii(
            document.WorkingBytesBuffer,
            record.Offset + CcfFieldDefinitions.Record.Name,
            CcfFieldDefinitions.Record.NameLength,
            value);
    }

    public static void SetRecordType(CcfDocument document, int recordIndex, byte value)
    {
        CcfRecord record = GetRecord(document, recordIndex);
        document.WorkingBytesBuffer[record.Offset + CcfFieldDefinitions.Record.Type] = value;
    }

    public static void SetRecordClassificationFlag(CcfDocument document, int recordIndex, byte value)
    {
        CcfRecord record = GetRecord(document, recordIndex);
        document.WorkingBytesBuffer[record.Offset + CcfFieldDefinitions.Record.ClassificationFlag] = value;
    }

    public static void SetRecordCard(CcfDocument document, int recordIndex, byte value)
    {
        CcfRecord record = GetRecord(document, recordIndex);
        document.WorkingBytesBuffer[record.Offset + CcfFieldDefinitions.Record.Card] = value;
    }

    public static void SetRecordChannel(CcfDocument document, int recordIndex, byte value)
    {
        CcfRecord record = GetRecord(document, recordIndex);
        document.WorkingBytesBuffer[record.Offset + CcfFieldDefinitions.Record.Channel] = value;
    }

    public static void SetRecordLoggerMode(CcfDocument document, int recordIndex, byte value)
    {
        CcfRecord record = GetRecord(document, recordIndex);
        document.WorkingBytesBuffer[record.Offset + CcfFieldDefinitions.Record.LoggerMode] = value;
    }

    public static void SetRecordHardwareFunction(CcfDocument document, int recordIndex, byte value)
    {
        CcfRecord record = GetRecord(document, recordIndex);
        document.WorkingBytesBuffer[record.Offset + CcfFieldDefinitions.Record.HardwareFunction] = value;
    }

    public static void SetRecordColourHex(CcfDocument document, int recordIndex, string value)
    {
        CcfRecord record = GetRecord(document, recordIndex);
        byte[] colour = ParseFourByteHex(value);
        colour.CopyTo(document.WorkingBytesBuffer, record.Offset + CcfFieldDefinitions.Record.Colour);
    }

    public static void SetDigitalOffDescription(CcfDocument document, int recordIndex, string value)
    {
        CcfRecord record = GetDigitalRecord(document, recordIndex);
        CcfText.WriteFixedAscii(
            document.WorkingBytesBuffer,
            record.Offset + CcfFieldDefinitions.Digital.OffDescription,
            CcfFieldDefinitions.Digital.OffDescriptionLength,
            value);
    }

    public static void SetDigitalOnDescription(CcfDocument document, int recordIndex, string value)
    {
        CcfRecord record = GetDigitalRecord(document, recordIndex);
        CcfText.WriteFixedAscii(
            document.WorkingBytesBuffer,
            record.Offset + CcfFieldDefinitions.Digital.OnDescription,
            CcfFieldDefinitions.Digital.OnDescriptionLength,
            value);
    }

    public static void SetDigitalPairRecord(CcfDocument document, int recordIndex, ushort pairRecord)
    {
        if (pairRecord >= CcfConstants.RecordCount)
            throw new ArgumentOutOfRangeException(nameof(pairRecord), "Pair record must be between 0 and 255.");

        CcfRecord record = GetDigitalRecord(document, recordIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(
            document.WorkingBytesBuffer.AsSpan(record.Offset + CcfFieldDefinitions.Digital.PairRecord, 2),
            pairRecord);
    }

    public static void SetHeaderAscii(CcfDocument document, int offset, int length, string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsEditableHeaderAsciiField(offset, length))
            throw new InvalidOperationException($"Header field 0x{offset:X4} length {length} is not enabled for ASCII editing.");

        CcfText.WriteFixedAscii(document.WorkingBytesBuffer, offset, length, value);
    }

    public static void SetHeaderByte(CcfDocument document, int offset, byte value)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsEditableHeaderByteField(offset))
            throw new InvalidOperationException($"Header byte 0x{offset:X4} is not enabled for editing.");

        document.WorkingBytesBuffer[offset] = value;
    }

    public static void SetHeaderUInt16(CcfDocument document, int offset, ushort value)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsEditableHeaderUInt16Field(offset))
            throw new InvalidOperationException($"Header UInt16 field 0x{offset:X4} is not enabled for editing.");

        BinaryPrimitives.WriteUInt16LittleEndian(document.WorkingBytesBuffer.AsSpan(offset, 2), value);
    }

    public static void RestoreRecord(CcfDocument document, int recordIndex)
    {
        CcfRecord record = GetRecord(document, recordIndex);
        document.OriginalBytesSpan
            .Slice(record.Offset, CcfConstants.RecordSize)
            .CopyTo(document.WorkingBytesBuffer.AsSpan(record.Offset, CcfConstants.RecordSize));
    }

    public static void RestoreHeaderField(CcfDocument document, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (offset < 0 || length < 1 || offset > CcfConstants.HeaderSize - length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        document.OriginalBytesSpan
            .Slice(offset, length)
            .CopyTo(document.WorkingBytesBuffer.AsSpan(offset, length));
    }

    public static void RestoreAll(CcfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.OriginalBytesSpan.CopyTo(document.WorkingBytesBuffer);
    }

    public static void ValidateFixedAscii(string value, int fieldLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (fieldLength < 1)
            throw new ArgumentOutOfRangeException(nameof(fieldLength));
        if (value.Any(ch => ch > 0x7F || ch == '\0'))
            throw new ArgumentException("Only ASCII text without NUL characters is supported.", nameof(value));
        if (value.Length > fieldLength - 1)
            throw new ArgumentException($"Maximum length is {fieldLength - 1} ASCII characters.", nameof(value));
    }

    public static byte[] ParseFourByteHex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string compact = new(value.Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray());
        if (compact.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            compact = compact[2..];

        if (compact.Length != CcfFieldDefinitions.Record.ColourLength * 2)
            throw new FormatException("Colour must contain exactly 4 bytes / 8 hexadecimal digits, for example 00FF0000.");

        try
        {
            return Convert.FromHexString(compact);
        }
        catch (FormatException)
        {
            throw new FormatException("Colour contains invalid hexadecimal characters.");
        }
    }

    public static bool IsEditableHeaderAsciiField(int offset, int length) =>
        (offset, length) is
            (CcfFieldDefinitions.Header.FirmwareVersion, 8) or
            (CcfFieldDefinitions.Header.SerialCore, 8) or
            (CcfFieldDefinitions.Header.Vehicle, 10) or
            (CcfFieldDefinitions.Header.Unit, 7) or
            (CcfFieldDefinitions.Header.VehicleType, 8);

    public static bool IsEditableHeaderByteField(int offset) =>
        offset is
            CcfFieldDefinitions.Header.CardsFitted or
            CcfFieldDefinitions.Header.PulsesPerRev or
            CcfFieldDefinitions.Header.Poll or
            CcfFieldDefinitions.Header.NoEvent or
            CcfFieldDefinitions.Header.SampleCount;

    public static bool IsEditableHeaderUInt16Field(int offset) =>
        offset is
            CcfFieldDefinitions.Header.Wheel1 or
            CcfFieldDefinitions.Header.Wheel2 or
            CcfFieldDefinitions.Header.DistanceTrigger or
            CcfFieldDefinitions.Header.MidJourney;

    private static CcfRecord GetRecord(CcfDocument document, int recordIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        if ((uint)recordIndex >= CcfConstants.RecordCount)
            throw new ArgumentOutOfRangeException(nameof(recordIndex));
        return document.Records[recordIndex];
    }

    private static CcfRecord GetDigitalRecord(CcfDocument document, int recordIndex)
    {
        CcfRecord record = GetRecord(document, recordIndex);
        if (!record.IsDigital)
            throw new InvalidOperationException($"Record {recordIndex} is type {record.Type}; OFF/ON/pair fields are only defined for digital type 2 records.");
        return record;
    }
}
