using System.Buffers.Binary;
using System.Text;
using CcfEditor.Core;

namespace CcfEditor.Tests;

public sealed class CcfEditTests
{
    [Fact]
    public void SetRecordName_ChangesOnlyThe16ByteNameField()
    {
        byte[] bytes = TestCcfFactory.CreateDeterministicFile();
        TestCcfFactory.ConfigureDigitalRecord(bytes, 125, "AWS Bell", "Off", "On", 137, 4, 5, 3, 2);
        CcfDocument document = CcfParser.Parse(bytes);

        int fieldStart = CcfConstants.GetRecordOffset(125) + CcfFieldDefinitions.Record.Name;
        int fieldEnd = fieldStart + CcfFieldDefinitions.Record.NameLength - 1;

        CcfEditService.SetRecordName(document, 125, "AWS Bell New");

        Assert.Equal("AWS Bell New", document.Records[125].Name);
        Assert.NotEmpty(document.GetByteChanges());
        Assert.All(document.GetByteChanges(), change => Assert.InRange(change.Offset, fieldStart, fieldEnd));
    }

    [Fact]
    public void SetDigitalPairRecord_WritesOnlyTheLittleEndianPairField()
    {
        byte[] bytes = TestCcfFactory.CreateDeterministicFile();
        TestCcfFactory.ConfigureDigitalRecord(bytes, 125, "AWS Bell", "Off", "On", 137, 4, 5, 3, 2);
        CcfDocument document = CcfParser.Parse(bytes);

        int fieldStart = CcfConstants.GetRecordOffset(125) + CcfFieldDefinitions.Digital.PairRecord;
        int fieldEnd = fieldStart + 1;

        CcfEditService.SetDigitalPairRecord(document, 125, 200);

        Assert.True(document.Records[125].PairRecord.HasValue);
        Assert.Equal((ushort)200, document.Records[125].PairRecord!.Value);
        Assert.NotEmpty(document.GetByteChanges());
        Assert.All(document.GetByteChanges(), change => Assert.InRange(change.Offset, fieldStart, fieldEnd));

        byte[] working = document.GetWorkingBytesSnapshot();
        Assert.Equal((ushort)200, BinaryPrimitives.ReadUInt16LittleEndian(working.AsSpan(fieldStart, 2)));
    }

    [Fact]
    public void SetRecordColourHex_WritesExactlyFourRawBytes()
    {
        byte[] bytes = TestCcfFactory.CreateDeterministicFile();
        CcfDocument document = CcfParser.Parse(bytes);
        int fieldStart = CcfConstants.GetRecordOffset(7) + CcfFieldDefinitions.Record.Colour;
        int fieldEnd = fieldStart + CcfFieldDefinitions.Record.ColourLength - 1;

        CcfEditService.SetRecordColourHex(document, 7, "11 22 33 44");

        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, document.GetWorkingBytesSnapshot().AsSpan(fieldStart, 4).ToArray());
        Assert.All(document.GetByteChanges(), change => Assert.InRange(change.Offset, fieldStart, fieldEnd));
    }

    [Fact]
    public void SetHeaderVehicle_ChangesOnlyTheDeclaredHeaderField()
    {
        byte[] bytes = TestCcfFactory.CreateDeterministicFile();
        CcfDocument document = CcfParser.Parse(bytes);
        const int offset = CcfFieldDefinitions.Header.Vehicle;
        const int length = 10;

        CcfEditService.SetHeaderAscii(document, offset, length, "171804");

        byte[] working = document.GetWorkingBytesSnapshot();
        string vehicle = Encoding.ASCII.GetString(working.AsSpan(offset, 6));
        Assert.Equal("171804", vehicle);
        Assert.Equal(0, working[offset + 6]);
        Assert.All(document.GetByteChanges(), change => Assert.InRange(change.Offset, offset, offset + length - 1));
    }

    [Fact]
    public void EditedSaveAs_MatchesWorkingBytes_ButDoesNotPretendToBeNoEditSave()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "CcfEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string sourcePath = Path.Combine(tempDirectory, "input.ccf");
        string outputPath = Path.Combine(tempDirectory, "edited.ccf");
        byte[] sourceBytes = TestCcfFactory.CreateDeterministicFile();
        TestCcfFactory.ConfigureDigitalRecord(sourceBytes, 125, "AWS Bell", "Off", "On", 137, 4, 5, 3, 2);
        File.WriteAllBytes(sourcePath, sourceBytes);

        try
        {
            CcfDocument document = CcfParser.Load(sourcePath);
            CcfEditService.SetRecordName(document, 125, "AWS Bell New");

            SaveVerification save = CcfFileService.SaveAs(document, outputPath);

            Assert.True(save.OutputMatchesWorkingBytes);
            Assert.False(save.NoEditShaMatchesOriginal);
            Assert.NotEqual(save.OriginalSha256, save.WorkingSha256);
            Assert.Equal(save.WorkingSha256, save.OutputSha256);
            Assert.Equal(document.GetWorkingBytesSnapshot(), File.ReadAllBytes(outputPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void RestoreRecord_RestoresOnlyThatRecordToOriginalBytes()
    {
        byte[] bytes = TestCcfFactory.CreateDeterministicFile();
        TestCcfFactory.ConfigureDigitalRecord(bytes, 125, "AWS Bell", "Off", "On", 137, 4, 5, 3, 2);
        CcfDocument document = CcfParser.Parse(bytes);

        CcfEditService.SetRecordName(document, 125, "Changed");
        Assert.True(document.IsModified);

        CcfEditService.RestoreRecord(document, 125);

        Assert.False(document.IsModified);
        Assert.Equal("AWS Bell", document.Records[125].Name);
    }
}
