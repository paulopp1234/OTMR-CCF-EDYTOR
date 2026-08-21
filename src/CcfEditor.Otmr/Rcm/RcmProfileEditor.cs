using CcfEditor.Core;

namespace CcfEditor.Otmr.Rcm;

public sealed class RcmInputEdit
{
    public string Connector { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public string Function { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Mio { get; set; } = string.Empty;
    public string PhysicalChannel { get; set; } = string.Empty;
    public string ReturnOrPair { get; set; } = string.Empty;
    public bool Testable { get; set; }
    public string SafetyClassification { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public int? RecordA { get; set; }
    public int? RecordB { get; set; }
    public int? LogicalCard { get; set; }
    public int? LogicalChannel { get; set; }
    public string? RecordAText { get; set; }
    public string? RecordAValue { get; set; }
    public string? RecordBText { get; set; }
    public string? RecordBValue { get; set; }
    public int? RecordType { get; set; }
    public string? PairRelationship { get; set; }

    public static RcmInputEdit From(RcmPinProfile pin)
    {
        RcmCcfReference? ccf = pin.CcfReference;
        return new RcmInputEdit
        {
            Connector = pin.Connector,
            Pin = pin.Pin,
            Function = pin.Function,
            Role = pin.Role,
            Mio = pin.Mio,
            PhysicalChannel = pin.PhysicalChannel,
            ReturnOrPair = pin.ReturnOrPair,
            Testable = pin.Testable,
            SafetyClassification = pin.SafetyClassification,
            Notes = pin.Notes,
            RecordA = ccf?.RecordA,
            RecordB = ccf?.RecordB,
            LogicalCard = ccf?.LogicalCard,
            LogicalChannel = ccf?.LogicalChannel,
            RecordAText = ccf?.RecordAText,
            RecordAValue = ccf?.RecordAValue,
            RecordBText = ccf?.RecordBText,
            RecordBValue = ccf?.RecordBValue,
            RecordType = ccf?.RecordType,
            PairRelationship = ccf?.PairRelationship
        };
    }
}

public readonly record struct RcmInputEditResult(bool LogicalMappingChanged, bool HadCapturedEvidence);

public static class RcmProfileEditor
{
    public static void AddConnector(RcmProfile profile, string name, IEnumerable<string>? orderedPins = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        name = RequireConnectorName(name);
        if (profile.Connectors.Any(connector => string.Equals(connector.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Connector '{name}' already exists.");
        profile.Connectors.Add(new RcmConnector
        {
            Name = name,
            OrderedPins = NormalizeOrderedPins(orderedPins)
        });
    }

    public static void SetConnectorOrderedPins(RcmProfile profile, string connectorName, IEnumerable<string> orderedPins)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RcmConnector connector = profile.Connectors.Single(item =>
            string.Equals(item.Name, connectorName, StringComparison.OrdinalIgnoreCase));
        connector.OrderedPins = NormalizeOrderedPins(orderedPins);
    }

    public static string? SuggestNextUnusedPin(RcmProfile profile, string connectorName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RcmConnector? connector = profile.Connectors.SingleOrDefault(item =>
            string.Equals(item.Name, connectorName, StringComparison.OrdinalIgnoreCase));
        if (connector is null || connector.OrderedPins.Count == 0)
            return null;

        var used = profile.Pins
            .Where(pin => string.Equals(pin.Connector, connector.Name, StringComparison.OrdinalIgnoreCase) &&
                          !string.IsNullOrWhiteSpace(pin.Pin))
            .Select(pin => pin.Pin)
            .ToHashSet(StringComparer.Ordinal);
        return connector.OrderedPins.FirstOrDefault(pin => !used.Contains(pin));
    }

    public static void RenameConnector(RcmProfile profile, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        newName = RequireConnectorName(newName);
        RcmConnector connector = profile.Connectors.Single(item =>
            string.Equals(item.Name, oldName, StringComparison.OrdinalIgnoreCase));
        if (profile.Connectors.Any(item => !ReferenceEquals(item, connector) &&
                                          string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Connector '{newName}' already exists.");

        string previous = connector.Name;
        connector.Name = newName;
        foreach (RcmPinProfile pin in profile.Pins.Where(pin =>
                     string.Equals(pin.Connector, previous, StringComparison.OrdinalIgnoreCase)))
        {
            pin.Connector = newName;
        }
    }

    public static int DeleteConnector(RcmProfile profile, string name, bool deleteInputs)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RcmConnector connector = profile.Connectors.Single(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        List<RcmPinProfile> inputs = profile.Pins.Where(pin =>
            string.Equals(pin.Connector, connector.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (inputs.Count > 0 && !deleteInputs)
            throw new InvalidOperationException(
                $"Connector '{connector.Name}' contains {inputs.Count} input(s). Explicit deletion of those inputs is required.");

        foreach (RcmPinProfile input in inputs)
            profile.Pins.Remove(input);
        profile.Connectors.Remove(connector);
        return inputs.Count;
    }

    public static RcmPinProfile AddInput(RcmProfile profile, RcmInputEdit edit, CcfDocument? document = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(edit);
        ValidateEdit(profile, edit, document, null);
        var pin = new RcmPinProfile { Id = Guid.NewGuid() };
        Apply(pin, edit);
        pin.RcmResult = RcmCaptureWindowCoordinator.ResultForCapturedStates(pin);
        profile.Pins.Add(pin);
        EnsureConnectorExists(profile, pin.Connector);
        return pin;
    }

    public static RcmInputEditResult UpdateInput(
        RcmProfile profile,
        Guid inputId,
        RcmInputEdit edit,
        CcfDocument? document = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(edit);
        RcmPinProfile pin = profile.GetInput(inputId);
        ValidateEdit(profile, edit, document, inputId);
        bool logicalChanged = LogicalMappingChanged(pin.CcfReference, edit);
        bool hadEvidence = HasEvidence(pin);
        string previousResult = pin.RcmResult;
        Apply(pin, edit);
        EnsureConnectorExists(profile, pin.Connector);
        // Editing descriptive or logical metadata must not silently discard an
        // existing comparison/result. Only a testability transition needs a new
        // base state.
        RefreshResultAfterPhysicalEdit(pin, previousResult);
        return new RcmInputEditResult(logicalChanged, hadEvidence);
    }

    public static void AssignPhysical(RcmProfile profile, Guid inputId, string connector, string physicalPin)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RcmPinProfile pin = profile.GetInput(inputId);
        connector = string.IsNullOrWhiteSpace(connector) ? string.Empty : RequireConnectorName(connector);
        physicalPin = physicalPin.Trim();
        ValidatePhysicalAssignment(profile, inputId, connector, physicalPin);

        string previousResult = pin.RcmResult;
        pin.Connector = connector;
        pin.Pin = physicalPin;
        EnsureConnectorExists(profile, connector);
        RefreshResultAfterPhysicalEdit(pin, previousResult);
    }

    public static bool DeleteInput(RcmProfile profile, Guid inputId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RcmPinProfile pin = profile.GetInput(inputId);
        bool hadEvidence = HasEvidence(pin);
        profile.Pins.Remove(pin);
        return hadEvidence;
    }

    public static bool HasEvidence(RcmPinProfile pin) =>
        pin.VoltageRemoved.FrameCount > 0 || pin.VoltageApplied24V.FrameCount > 0 ||
        pin.VoltageRemoved.CaptureStart.HasValue || pin.VoltageApplied24V.CaptureStart.HasValue ||
        pin.Comparison.ComparedAt.HasValue;

    private static void Apply(RcmPinProfile pin, RcmInputEdit edit)
    {
        pin.Connector = edit.Connector.Trim();
        pin.Pin = edit.Pin.Trim();
        pin.Function = edit.Function.Trim();
        pin.Role = edit.Role.Trim();
        pin.Mio = edit.Mio.Trim();
        pin.PhysicalChannel = edit.PhysicalChannel.Trim();
        pin.ReturnOrPair = edit.ReturnOrPair.Trim();
        pin.Testable = edit.Testable;
        pin.SafetyClassification = edit.SafetyClassification.Trim();
        pin.Notes = edit.Notes.Trim();
        pin.CcfReference = HasAnyCcfValue(edit)
            ? new RcmCcfReference
            {
                RecordA = edit.RecordA,
                RecordB = edit.RecordB,
                LogicalCard = edit.LogicalCard,
                LogicalChannel = edit.LogicalChannel,
                RecordAText = edit.RecordAText,
                RecordAValue = edit.RecordAValue,
                RecordBText = edit.RecordBText,
                RecordBValue = edit.RecordBValue,
                RecordType = edit.RecordType,
                PairRelationship = edit.PairRelationship
            }
            : null;
    }

    private static void ValidateEdit(RcmProfile profile, RcmInputEdit edit, CcfDocument? document, Guid? existingId)
    {
        if (document is not null)
        {
            ValidateRecord(edit.RecordA, document, "Record A");
            ValidateRecord(edit.RecordB, document, "Record B");
        }

        ValidatePhysicalAssignment(profile, existingId, edit.Connector.Trim(), edit.Pin.Trim());
    }

    private static void ValidatePhysicalAssignment(
        RcmProfile profile,
        Guid? existingId,
        string connector,
        string physicalPin)
    {
        if (!string.IsNullOrWhiteSpace(connector) && !string.IsNullOrWhiteSpace(physicalPin) &&
            profile.Pins.Any(pin => pin.Id != existingId &&
                                    string.Equals(pin.Connector, connector, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(pin.Pin, physicalPin, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Physical input {connector}-{physicalPin} already exists.");
        }
    }

    private static void ValidateRecord(int? record, CcfDocument document, string label)
    {
        if (record is int value && (uint)value >= (uint)document.Records.Count)
            throw new ArgumentOutOfRangeException(label, $"{label} {value} is outside the loaded CCF.");
    }

    private static bool LogicalMappingChanged(RcmCcfReference? current, RcmInputEdit edit) =>
        current?.RecordA != edit.RecordA || current?.RecordB != edit.RecordB ||
        current?.LogicalCard != edit.LogicalCard || current?.LogicalChannel != edit.LogicalChannel ||
        current?.RecordType != edit.RecordType;

    private static bool HasAnyCcfValue(RcmInputEdit edit) =>
        edit.RecordA.HasValue || edit.RecordB.HasValue || edit.LogicalCard.HasValue || edit.LogicalChannel.HasValue ||
        edit.RecordType.HasValue || !string.IsNullOrWhiteSpace(edit.RecordAText) ||
        !string.IsNullOrWhiteSpace(edit.RecordAValue) || !string.IsNullOrWhiteSpace(edit.RecordBText) ||
        !string.IsNullOrWhiteSpace(edit.RecordBValue) || !string.IsNullOrWhiteSpace(edit.PairRelationship);

    private static string RequireConnectorName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        if (string.Equals(name, RcmInputFilter.All, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, RcmInputFilter.Unassigned, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{name}' is reserved for the connector filter.");
        return name;
    }

    private static List<string> NormalizeOrderedPins(IEnumerable<string>? orderedPins)
    {
        List<string> result = orderedPins?
            .Select(pin => pin.Trim())
            .Where(pin => pin.Length > 0)
            .ToList() ?? new List<string>();
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidOperationException("A connector pin sequence cannot contain duplicate pin names.");
        return result;
    }

    internal static void RefreshResultAfterPhysicalEdit(RcmPinProfile pin, string? previousResult = null)
    {
        if (!pin.PhysicalMappingAssigned)
        {
            pin.RcmResult = RcmResultStates.Unassigned;
            return;
        }
        if (!pin.Testable)
        {
            pin.RcmResult = RcmResultStates.NotTestable;
            return;
        }
        if (pin.Comparison.ComparedAt is not null)
        {
            pin.RcmResult = previousResult is not (null or RcmResultStates.Unassigned or RcmResultStates.NotTestable)
                ? previousResult
                : pin.Comparison.RepeatableDifferences.Count > 0
                    ? RcmResultStates.RawDifferenceFound
                    : RcmResultStates.NoRepeatableDifference;
            return;
        }
        pin.RcmResult = RcmCaptureWindowCoordinator.ResultForCapturedStates(pin);
    }

    private static void EnsureConnectorExists(RcmProfile profile, string connector)
    {
        if (string.IsNullOrWhiteSpace(connector) || profile.Connectors.Any(item =>
                string.Equals(item.Name, connector, StringComparison.OrdinalIgnoreCase)))
            return;
        profile.Connectors.Add(new RcmConnector { Name = connector });
    }
}
