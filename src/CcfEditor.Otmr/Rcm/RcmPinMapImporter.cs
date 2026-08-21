using CcfEditor.Otmr.Bench;

namespace CcfEditor.Otmr.Rcm;

public readonly record struct RcmPinMapImportResult(int UpdatedUnassignedInputs, int AddedPhysicalInputs);

public static class RcmPinMapImporter
{
    public static RcmPinMapImportResult ImportFillUnassigned(
        RcmProfile profile,
        IReadOnlyList<OtmrBenchPinDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(definitions);
        int updated = 0;
        int added = 0;

        foreach (OtmrBenchPinDefinition definition in definitions)
        {
            RcmPinProfile? pin = profile.Pins.FirstOrDefault(candidate =>
                candidate.CcfReference?.RecordA == definition.ExpectedRecordA &&
                candidate.CcfReference?.RecordB == definition.ExpectedRecordB &&
                definition.ExpectedRecordA.HasValue &&
                string.IsNullOrWhiteSpace(candidate.Connector) && string.IsNullOrWhiteSpace(candidate.Pin));
            if (pin is null)
            {
                pin = new RcmPinProfile
                {
                    Id = Guid.NewGuid(),
                    CcfReference = definition.ExpectedRecordA.HasValue || definition.ExpectedRecordB.HasValue
                        ? new RcmCcfReference
                        {
                            RecordA = definition.ExpectedRecordA,
                            RecordB = definition.ExpectedRecordB,
                            LogicalCard = definition.ExpectedCard,
                            LogicalChannel = definition.ExpectedChannel,
                            PairRelationship = definition.ExpectedRecordA is int a && definition.ExpectedRecordB is int b ? $"{a} ↔ {b}" : null
                        }
                        : null
                };
                profile.Pins.Add(pin);
                added++;
            }
            else
            {
                updated++;
            }

            FillBlank(pin, definition);
            if (!profile.Connectors.Any(item => string.Equals(item.Name, definition.Connector, StringComparison.OrdinalIgnoreCase)))
                profile.Connectors.Add(new RcmConnector { Name = definition.Connector });
        }

        return new RcmPinMapImportResult(updated, added);
    }

    private static void FillBlank(RcmPinProfile pin, OtmrBenchPinDefinition definition)
    {
        string previousResult = pin.RcmResult;
        if (string.IsNullOrWhiteSpace(pin.Connector)) pin.Connector = definition.Connector;
        if (string.IsNullOrWhiteSpace(pin.Pin)) pin.Pin = definition.Pin;
        if (string.IsNullOrWhiteSpace(pin.Function)) pin.Function = definition.ExpectedFunction;
        if (string.IsNullOrWhiteSpace(pin.Role)) pin.Role = definition.Role;
        if (string.IsNullOrWhiteSpace(pin.Mio)) pin.Mio = definition.Mio;
        if (string.IsNullOrWhiteSpace(pin.PhysicalChannel)) pin.PhysicalChannel = definition.Channel;
        if (string.IsNullOrWhiteSpace(pin.ReturnOrPair)) pin.ReturnOrPair = definition.ReturnOrPair;
        if (string.IsNullOrWhiteSpace(pin.SafetyClassification)) pin.SafetyClassification = definition.SafetyInstruction;
        pin.Testable = definition.IsVoltageTestPoint;
        pin.RcmResult = !pin.Testable
            ? RcmResultStates.NotTestable
            : previousResult == RcmResultStates.NotTestable
                ? RcmCaptureWindowCoordinator.ResultForCapturedStates(pin)
                : previousResult;
    }
}
