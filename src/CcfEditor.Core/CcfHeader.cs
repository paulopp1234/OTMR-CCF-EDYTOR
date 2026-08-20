using System.Buffers.Binary;

namespace CcfEditor.Core;

public sealed class CcfHeader
{
    private readonly byte[] _workingBytes;

    internal CcfHeader(byte[] workingBytes)
    {
        _workingBytes = workingBytes;
    }

    public int Offset => 0;
    public int Length => CcfConstants.HeaderSize;

    // The handoff explicitly identifies 0x002E as a profile/family WORD.
    public ushort ProfileFamilyWord => ReadUInt16LittleEndian(CcfFieldDefinitions.Header.ProfileFamilyWord);

    public byte ReadByte(int relativeOffset)
    {
        EnsureRange(relativeOffset, 1);
        return _workingBytes[relativeOffset];
    }

    public ushort ReadUInt16LittleEndian(int relativeOffset)
    {
        EnsureRange(relativeOffset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(_workingBytes.AsSpan(relativeOffset, sizeof(ushort)));
    }

    public byte[] GetRawBytes(int relativeOffset, int length)
    {
        EnsureRange(relativeOffset, length);
        return _workingBytes.AsSpan(relativeOffset, length).ToArray();
    }

    public byte[] GetRawHeaderBytes() => _workingBytes.AsSpan(0, CcfConstants.HeaderSize).ToArray();

    private static void EnsureRange(int relativeOffset, int length)
    {
        if (relativeOffset < 0 || length < 0 || relativeOffset > CcfConstants.HeaderSize - length)
            throw new ArgumentOutOfRangeException(nameof(relativeOffset), "Requested header range is outside 0x0000..0x03E7.");
    }
}
