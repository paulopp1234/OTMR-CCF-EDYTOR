using System.Buffers.Binary;

namespace CcfEditor.Core;

public sealed class CcfRecord
{
    private readonly byte[] _workingBytes;

    internal CcfRecord(byte[] workingBytes, int physicalIndex)
    {
        _workingBytes = workingBytes;
        PhysicalIndex = physicalIndex;
        Offset = CcfConstants.GetRecordOffset(physicalIndex);
    }

    public int PhysicalIndex { get; }
    public int Offset { get; }
    public ushort EventIndex => ReadUInt16(CcfFieldDefinitions.Record.EventIndex);
    public byte Type => ReadByte(CcfFieldDefinitions.Record.Type);
    public byte ClassificationFlag => ReadByte(CcfFieldDefinitions.Record.ClassificationFlag);
    public string Name => CcfText.ReadFixedAscii(_workingBytes, Offset + CcfFieldDefinitions.Record.Name, CcfFieldDefinitions.Record.NameLength);
    public byte Card => ReadByte(CcfFieldDefinitions.Record.Card);
    public byte Channel => ReadByte(CcfFieldDefinitions.Record.Channel);
    public byte LoggerMode => ReadByte(CcfFieldDefinitions.Record.LoggerMode);
    public byte HardwareFunction => ReadByte(CcfFieldDefinitions.Record.HardwareFunction);
    public bool IsDigital => Type == 2;
    public bool IsNumeric => Type is 1 or 3 or 8;

    public string? OffDescription => IsDigital
        ? CcfText.ReadFixedAscii(_workingBytes, Offset + CcfFieldDefinitions.Digital.OffDescription, CcfFieldDefinitions.Digital.OffDescriptionLength)
        : null;

    public string? OnDescription => IsDigital
        ? CcfText.ReadFixedAscii(_workingBytes, Offset + CcfFieldDefinitions.Digital.OnDescription, CcfFieldDefinitions.Digital.OnDescriptionLength)
        : null;

    public ushort? PairRecord => IsDigital ? ReadUInt16(CcfFieldDefinitions.Digital.PairRecord) : null;

    public float? Minimum => IsNumeric ? ReadSingle(CcfFieldDefinitions.Numeric.Min) : null;
    public float? Maximum => IsNumeric ? ReadSingle(CcfFieldDefinitions.Numeric.Max) : null;
    public float? NumericOffset => IsNumeric ? ReadSingle(CcfFieldDefinitions.Numeric.Offset) : null;
    public float? Gain => IsNumeric ? ReadSingle(CcfFieldDefinitions.Numeric.Gain) : null;
    public string? Units => IsNumeric
        ? CcfText.ReadFixedAscii(_workingBytes, Offset + CcfFieldDefinitions.Numeric.Units, CcfFieldDefinitions.Numeric.UnitsLength)
        : null;
    public byte? DecimalPlaces => IsNumeric ? ReadByte(CcfFieldDefinitions.Numeric.DecimalPlaces) : null;

    public byte[] GetRawBytes() => _workingBytes.AsSpan(Offset, CcfConstants.RecordSize).ToArray();

    public byte[] GetRawBytes(int relativeOffset, int length)
    {
        EnsureRecordRange(relativeOffset, length);
        return _workingBytes.AsSpan(Offset + relativeOffset, length).ToArray();
    }

    public string GetRawHex() => Convert.ToHexString(_workingBytes.AsSpan(Offset, CcfConstants.RecordSize));

    public string ToDiagnosticString()
    {
        var pair = PairRecord.HasValue ? PairRecord.Value.ToString() : "-";
        var off = OffDescription ?? "-";
        var on = OnDescription ?? "-";

        return $"Record {PhysicalIndex,3} @ 0x{Offset:X4} | Event={EventIndex,3} | Type={Type,2} | Flag={ClassificationFlag,3} | Name=\"{Name}\" | Card={Card} Ch={Channel} | Logger={LoggerMode} Func={HardwareFunction} | Pair={pair} | OFF=\"{off}\" | ON=\"{on}\"";
    }

    private byte ReadByte(int relativeOffset)
    {
        EnsureRecordRange(relativeOffset, 1);
        return _workingBytes[Offset + relativeOffset];
    }

    private ushort ReadUInt16(int relativeOffset)
    {
        EnsureRecordRange(relativeOffset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(_workingBytes.AsSpan(Offset + relativeOffset, sizeof(ushort)));
    }

    private float ReadSingle(int relativeOffset)
    {
        EnsureRecordRange(relativeOffset, sizeof(float));
        var bits = BinaryPrimitives.ReadInt32LittleEndian(_workingBytes.AsSpan(Offset + relativeOffset, sizeof(float)));
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static void EnsureRecordRange(int relativeOffset, int length)
    {
        if (relativeOffset < 0 || length < 0 || relativeOffset > CcfConstants.RecordSize - length)
            throw new ArgumentOutOfRangeException(nameof(relativeOffset), "Requested field is outside the 100-byte record.");
    }
}
