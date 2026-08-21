using CcfEditor.Core;
using CcfEditor.Otmr.Bench;

namespace CcfEditor.Otmr.Rcm;

public static class RcmProfileFactory
{
    public static RcmProfile Create(
        CcfDocument document,
        IReadOnlyList<OtmrBenchPinDefinition> physicalPins,
        string vehicleType,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(physicalPins);
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleType);

        var profile = new RcmProfile
        {
            VehicleType = vehicleType,
            SourceCcfFilename = Path.GetFileName(document.SourcePath ?? "opened.ccf"),
            SourceCcfSha256 = document.OriginalSha256,
            SourceCcfSize = document.GetOriginalBytesSnapshot().LongLength,
            CreationTimestamp = timestamp,
            LastModifiedTimestamp = timestamp
        };

        foreach (OtmrBenchPinDefinition definition in physicalPins)
        {
            profile.Pins.Add(new RcmPinProfile
            {
                Connector = definition.Connector,
                Pin = definition.Pin,
                Function = definition.ExpectedFunction,
                Role = definition.Role,
                Mio = definition.Mio,
                PhysicalChannel = definition.Channel,
                ReturnOrPair = definition.ReturnOrPair,
                SafetyClassification = definition.SafetyInstruction,
                Testable = definition.IsVoltageTestPoint,
                CcfReference = BuildCcfReference(document, definition),
                RcmResult = definition.IsVoltageTestPoint
                    ? RcmResultStates.NotTested
                    : RcmResultStates.NotTestable
            });
        }

        return profile;
    }

    private static RcmCcfReference? BuildCcfReference(
        CcfDocument document,
        OtmrBenchPinDefinition definition)
    {
        if (definition.ExpectedRecordA is null &&
            definition.ExpectedRecordB is null &&
            definition.ExpectedCard is null &&
            definition.ExpectedChannel is null)
        {
            return null;
        }

        CcfRecord? recordA = GetRecord(document, definition.ExpectedRecordA);
        CcfRecord? recordB = GetRecord(document, definition.ExpectedRecordB);
        return new RcmCcfReference
        {
            LogicalCard = definition.ExpectedCard ?? recordA?.Card,
            LogicalChannel = definition.ExpectedChannel ?? recordA?.Channel,
            RecordA = definition.ExpectedRecordA,
            RecordB = definition.ExpectedRecordB,
            RecordAText = recordA?.Name,
            RecordAValue = FormatRecordValue(recordA),
            RecordBText = recordB?.Name,
            RecordBValue = FormatRecordValue(recordB),
            RecordType = recordA?.Type,
            PairRelationship = BuildPairRelationship(recordA, definition.ExpectedRecordB)
        };
    }

    private static CcfRecord? GetRecord(CcfDocument document, int? index) =>
        index is int value && (uint)value < (uint)document.Records.Count
            ? document.Records[value]
            : null;

    private static string? FormatRecordValue(CcfRecord? record)
    {
        if (record is null)
            return null;
        if (record.IsDigital)
            return $"OFF text: {record.OffDescription ?? string.Empty} | ON text: {record.OnDescription ?? string.Empty}";
        if (record.IsNumeric)
            return $"min {record.Minimum} | max {record.Maximum} | units {record.Units}";
        return $"event {record.EventIndex}";
    }

    private static string? BuildPairRelationship(CcfRecord? recordA, int? expectedRecordB)
    {
        if (recordA is null && expectedRecordB is null)
            return null;
        string left = recordA?.PhysicalIndex.ToString() ?? "?";
        string right = expectedRecordB?.ToString() ?? recordA?.PairRecord?.ToString() ?? "?";
        return $"{left} ↔ {right}";
    }
}
