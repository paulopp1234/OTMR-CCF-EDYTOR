using CcfEditor.Core;

namespace CcfEditor.Tests;

public sealed class CcfParserTests
{
    [Fact]
    public void Parse_ExactSize_ProducesHeaderAnd256RecordViews()
    {
        var bytes = TestCcfFactory.CreateDeterministicFile();
        var document = CcfParser.Parse(bytes);
        Assert.Equal(CcfConstants.FileSize, document.Length);
        Assert.NotNull(document.Header);
        Assert.Equal(256, document.Records.Count);
        Assert.True(document.IsByteIdenticalToOriginal);
        Assert.Equal(document.OriginalSha256, document.WorkingSha256);
    }

    [Theory]
    [InlineData(0, 0x03E8)]
    [InlineData(1, 0x044C)]
    [InlineData(23, 0x0CE4)]
    [InlineData(60, 0x1B58)]
    [InlineData(124, 0x3458)]
    [InlineData(125, 0x34BC)]
    [InlineData(127, 0x3584)]
    [InlineData(136, 0x3908)]
    [InlineData(139, 0x3A34)]
    [InlineData(255, 0x6784)]
    public void RecordOffsets_AreExactlyHeaderPlusIndexTimes100(int index, int expectedOffset)
    {
        var document = CcfParser.Parse(TestCcfFactory.CreateDeterministicFile());
        Assert.Equal(expectedOffset, document.Records[index].Offset);
        Assert.Equal((ushort)index, document.Records[index].EventIndex);
    }

    [Fact]
    public void DigitalRecord_DecodesOnlyKnownFieldsAtSpecifiedOffsets()
    {
        var bytes = TestCcfFactory.CreateDeterministicFile();
        TestCcfFactory.ConfigureDigitalRecord(bytes, 125, "AWS Bell", "Off", "On", 137, 4, 1, 2, 3);
        var record = CcfParser.Parse(bytes).Records[125];
        Assert.Equal((ushort)125, record.EventIndex);
        Assert.Equal((byte)2, record.Type);
        Assert.Equal("AWS Bell", record.Name);
        Assert.Equal("Off", record.OffDescription);
        Assert.Equal("On", record.OnDescription);
        Assert.True(record.PairRecord.HasValue);
        Assert.Equal((ushort)137, record.PairRecord.Value);
        Assert.Equal((byte)4, record.Card);
        Assert.Equal((byte)1, record.Channel);
        Assert.Equal((byte)2, record.LoggerMode);
        Assert.Equal((byte)3, record.HardwareFunction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(26599)]
    [InlineData(26601)]
    public void Parse_RejectsAnyLengthOtherThan26600(int length)
    {
        var bytes = new byte[length];
        var ex = Assert.Throws<InvalidDataException>(() => CcfParser.Parse(bytes));
        Assert.Contains("26,600", ex.Message);
    }

    [Fact]
    public void Parse_ClonesInputSoCallerCannotMutateDocumentBehindItsBack()
    {
        var source = TestCcfFactory.CreateDeterministicFile();
        var originalFirstByte = source[0];
        var document = CcfParser.Parse(source);
        source[0] ^= 0xFF;
        Assert.Equal(originalFirstByte, document.GetOriginalBytesSnapshot()[0]);
        Assert.Equal(originalFirstByte, document.GetWorkingBytesSnapshot()[0]);
        Assert.True(document.IsByteIdenticalToOriginal);
    }
}
