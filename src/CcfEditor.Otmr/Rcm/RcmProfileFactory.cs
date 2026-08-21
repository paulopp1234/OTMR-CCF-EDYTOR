using CcfEditor.Core;
using CcfEditor.Otmr.Bench;

namespace CcfEditor.Otmr.Rcm;

public static class RcmProfileFactory
{
    public static RcmProfile CreateFromCcf(
        CcfDocument document,
        string vehicleType,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleType);
        RcmProfile profile = CreateHeader(document, vehicleType, timestamp);
        var consumed = new HashSet<int>();

        foreach (CcfRecord recordA in document.Records)
        {
            if (consumed.Contains(recordA.PhysicalIndex) || string.IsNullOrWhiteSpace(recordA.Name))
                continue;

            int? recordBIndex = recordA.IsDigital && recordA.PairRecord is ushort pair &&
                               pair < document.Records.Count && pair != recordA.PhysicalIndex
                ? pair
                : null;
            CcfRecord? recordB = recordBIndex is int b ? document.Records[b] : null;
            consumed.Add(recordA.PhysicalIndex);
            if (recordB is not null)
                consumed.Add(recordB.PhysicalIndex);

            profile.Pins.Add(new RcmPinProfile
            {
                Id = Guid.NewGuid(),
                Connector = string.Empty,
                Pin = string.Empty,
                Function = recordA.Name,
                Role = string.Empty,
                Mio = string.Empty,
                PhysicalChannel = string.Empty,
                ReturnOrPair = string.Empty,
                SafetyClassification = string.Empty,
                Testable = false,
                CcfReference = BuildCcfReference(recordA, recordB),
                RcmResult = RcmResultStates.Unassigned
            });
        }

        return profile;
    }

    public static RcmProfile Create(
        CcfDocument document,
        IReadOnlyList<OtmrBenchPinDefinition> physicalPins,
        string vehicleType,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(physicalPins);
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleType);

        RcmProfile profile = CreateHeader(document, vehicleType, timestamp);

        foreach (OtmrBenchPinDefinition definition in physicalPins)
        {
            profile.Pins.Add(new RcmPinProfile
            {
                Id = Guid.NewGuid(),
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
            if (!string.IsNullOrWhiteSpace(definition.Connector) &&
                !profile.Connectors.Any(connector => string.Equals(connector.Name, definition.Connector, StringComparison.OrdinalIgnoreCase)))
            {
                profile.Connectors.Add(new RcmConnector { Name = definition.Connector });
            }
        }

        return profile;
    }

    private static RcmProfile CreateHeader(CcfDocument document, string vehicleType, DateTimeOffset timestamp) => new()
    {
        VehicleType = vehicleType,
        SourceCcfFilename = Path.GetFileName(document.SourcePath ?? "opened.ccf"),
        SourceCcfSha256 = document.OriginalSha256,
        SourceCcfSize = document.GetOriginalBytesSnapshot().LongLength,
        CreationTimestamp = timestamp,
        LastModifiedTimestamp = timestamp
    };

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

    private static RcmCcfReference BuildCcfReference(CcfRecord recordA, CcfRecord? recordB) => new()
    {
        LogicalCard = recordA.Card,
        LogicalChannel = recordA.Channel,
        RecordA = recordA.PhysicalIndex,
        RecordB = recordB?.PhysicalIndex,
        RecordAText = recordA.Name,
        RecordAValue = FormatRecordValue(recordA),
        RecordBText = recordB?.Name,
        RecordBValue = FormatRecordValue(recordB),
        RecordType = recordA.Type,
        PairRelationship = recordB is null ? null : $"{recordA.PhysicalIndex} ↔ {recordB.PhysicalIndex}"
    };

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
