using System.Buffers.Binary;
using System.Text;
using CcfEditor.Core;

namespace CcfEditor.Tests;

internal static class TestCcfFactory
{
    public static byte[] CreateDeterministicFile()
    {
        var bytes = new byte[CcfConstants.FileSize];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)((i * 73 + 41) & 0xFF);
        for (var recordIndex = 0; recordIndex < CcfConstants.RecordCount; recordIndex++)
        {
            var offset = CcfConstants.GetRecordOffset(recordIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), (ushort)recordIndex);
        }
        return bytes;
    }

    public static void ConfigureDigitalRecord(byte[] bytes, int recordIndex, string name, string off, string on, ushort pair, byte card, byte channel, byte loggerMode, byte hardwareFunction)
    {
        var offset = CcfConstants.GetRecordOffset(recordIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + CcfFieldDefinitions.Record.EventIndex, 2), (ushort)recordIndex);
        bytes[offset + CcfFieldDefinitions.Record.Type] = 2;
        bytes[offset + CcfFieldDefinitions.Record.ClassificationFlag] = 1;
        WriteFixed(bytes, offset + CcfFieldDefinitions.Record.Name, 16, name);
        WriteFixed(bytes, offset + CcfFieldDefinitions.Digital.OffDescription, 16, off);
        WriteFixed(bytes, offset + CcfFieldDefinitions.Digital.OnDescription, 16, on);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + CcfFieldDefinitions.Digital.PairRecord, 2), pair);
        bytes[offset + CcfFieldDefinitions.Record.Card] = card;
        bytes[offset + CcfFieldDefinitions.Record.Channel] = channel;
        bytes[offset + CcfFieldDefinitions.Record.LoggerMode] = loggerMode;
        bytes[offset + CcfFieldDefinitions.Record.HardwareFunction] = hardwareFunction;
    }

    private static void WriteFixed(byte[] bytes, int offset, int length, string value)
    {
        bytes.AsSpan(offset, length).Clear();
        var encoded = Encoding.ASCII.GetBytes(value);
        encoded.AsSpan(0, Math.Min(encoded.Length, length)).CopyTo(bytes.AsSpan(offset, length));
    }
}
