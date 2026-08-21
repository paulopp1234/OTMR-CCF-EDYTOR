using CcfEditor.Otmr.Bench;

namespace CcfEditor.Tests;

public sealed class OtmrBenchProfileTests
{
    [Fact]
    public void TsvProfile_PreservesPinSafetyAndCcfReferenceFields()
    {
        string path = Path.Combine(Path.GetTempPath(), $"otmr_bench_{Guid.NewGuid():N}.tsv");
        File.WriteAllText(path,
            "Connector\tPin\tRole\tMIO\tChannel\tExpectedFunction\tReturnOrPair\tSafetyInstruction\tIsVoltageTestPoint\tExpectedRecordA\tExpectedRecordB\tExpectedCard\tExpectedChannel\tEvidenceStatus\tSource\n" +
            "J1\tA\tDigital input\t1\t1\tThrottle 1\tJ1-L\tBench input\ttrue\t0\t12\t0\t0\tPASS\tbench\n" +
            "J1\tL\tCommon return\t1\t1-10\t0V common\t-\tDO NOT APPLY +V\tfalse\t\t\t\t\tREFERENCE ONLY\tdrawing\n");

        try
        {
            IReadOnlyList<OtmrBenchPinDefinition> pins = OtmrBenchProfileReader.LoadTsv(path);

            Assert.Equal(2, pins.Count);
            Assert.Equal("J1", pins[0].Connector);
            Assert.Equal("A", pins[0].Pin);
            Assert.True(pins[0].IsVoltageTestPoint);
            Assert.Equal(0, pins[0].ExpectedRecordA);
            Assert.Equal(12, pins[0].ExpectedRecordB);
            Assert.Equal(0, pins[0].ExpectedCard);
            Assert.Equal(0, pins[0].ExpectedChannel);
            Assert.False(pins[1].IsVoltageTestPoint);
            Assert.Null(pins[1].ExpectedRecordA);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void TsvProfile_RejectsMissingRequiredColumns()
    {
        string path = Path.Combine(Path.GetTempPath(), $"otmr_bench_bad_{Guid.NewGuid():N}.tsv");
        File.WriteAllText(path, "Connector\tPin\nJ1\tA\n");

        try
        {
            Assert.Throws<InvalidDataException>(() => OtmrBenchProfileReader.LoadTsv(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
