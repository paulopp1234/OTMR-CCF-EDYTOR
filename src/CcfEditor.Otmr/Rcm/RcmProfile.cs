using System.Text.Json.Serialization;

namespace CcfEditor.Otmr.Rcm;

public static class RcmResultStates
{
    public const string NotTested = "NOT_TESTED";
    public const string VoltageRemovedCaptured = "VOLTAGE_REMOVED_CAPTURED";
    public const string VoltageApplied24VCaptured = "24V_CAPTURED";
    public const string BothStatesCaptured = "BOTH_STATES_CAPTURED";
    public const string RawDifferenceFound = "RAW_DIFFERENCE_FOUND";
    public const string NoRepeatableDifference = "NO_REPEATABLE_DIFFERENCE";
    public const string DecoderNotVerified = "DECODER_NOT_VERIFIED";
    public const string NotTestable = "NOT_TESTABLE";
    public const string Unassigned = "UNASSIGNED";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        NotTested,
        VoltageRemovedCaptured,
        VoltageApplied24VCaptured,
        BothStatesCaptured,
        RawDifferenceFound,
        NoRepeatableDifference,
        DecoderNotVerified,
        NotTestable,
        Unassigned
    };
}

public enum RcmElectricalTestState
{
    VoltageRemoved,
    VoltageApplied24V
}

public sealed class RcmProfile
{
    public const string CurrentSchemaVersion = "1.2";

    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string VehicleType { get; set; } = "Class 171";
    public string SourceCcfFilename { get; set; } = string.Empty;
    public string SourceCcfSha256 { get; set; } = string.Empty;
    public long SourceCcfSize { get; set; }
    public DateTimeOffset CreationTimestamp { get; set; }
    public DateTimeOffset LastModifiedTimestamp { get; set; }
    public List<RcmConnector> Connectors { get; set; } = new();
    public List<RcmPinProfile> Pins { get; set; } = new();

    public RcmPinProfile GetInput(Guid id) => Pins.Single(candidate => candidate.Id == id);

    public RcmPinProfile GetPin(string connector, string pin) =>
        Pins.Single(candidate =>
            string.Equals(candidate.Connector, connector, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Pin, pin, StringComparison.Ordinal));

    [JsonIgnore]
    public int LogicalCcfInputCount => Pins.Count(pin => pin.CcfReference is not null);
    [JsonIgnore]
    public int AssignedPhysicalInputCount => Pins.Count(pin => pin.PhysicalMappingAssigned);
    [JsonIgnore]
    public int UnassignedInputCount => Pins.Count(pin => !pin.PhysicalMappingAssigned);
    [JsonIgnore]
    public int TestablePinCount => Pins.Count(pin => pin.PhysicalMappingAssigned && pin.Testable);
    [JsonIgnore]
    public int CompletedTestablePinCount => Pins.Count(pin =>
        pin.PhysicalMappingAssigned && pin.Testable && pin.VoltageRemoved.Tested && pin.VoltageApplied24V.Tested);
}

public sealed class RcmConnector
{
    public string Name { get; set; } = string.Empty;
    public List<string> OrderedPins { get; set; } = new();
}

public sealed class RcmPinProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Connector { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public string Function { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Mio { get; set; } = string.Empty;
    public string PhysicalChannel { get; set; } = string.Empty;
    public string ReturnOrPair { get; set; } = string.Empty;
    public string SafetyClassification { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool Testable { get; set; }
    public RcmCcfReference? CcfReference { get; set; }
    public RcmStateEvidence VoltageRemoved { get; set; } = new();
    public RcmStateEvidence VoltageApplied24V { get; set; } = new();
    public RcmStateComparison Comparison { get; set; } = new();
    public string RcmResult { get; set; } = RcmResultStates.NotTested;

    [JsonIgnore]
    public bool PhysicalMappingAssigned =>
        !string.IsNullOrWhiteSpace(Connector) && !string.IsNullOrWhiteSpace(Pin);

    [JsonIgnore]
    public string DisplayKey => PhysicalMappingAssigned ? $"{Connector}-{Pin}" : RcmResultStates.Unassigned;
}

public sealed class RcmCcfReference
{
    public int? LogicalCard { get; set; }
    public int? LogicalChannel { get; set; }
    public int? RecordA { get; set; }
    public int? RecordB { get; set; }
    public string? RecordAText { get; set; }
    public string? RecordAValue { get; set; }
    public string? RecordBText { get; set; }
    public string? RecordBValue { get; set; }
    public int? RecordType { get; set; }
    public string? PairRelationship { get; set; }
}

public sealed class RcmStateEvidence
{
    public bool Tested { get; set; }
    public bool NoOtmrData { get; set; }
    public DateTimeOffset? CaptureStart { get; set; }
    public DateTimeOffset? CaptureStop { get; set; }
    public List<RcmRawFrameEvidence> CompleteRawFrames { get; set; } = new();
    public int FrameCount => CompleteRawFrames.Count;
    public Dictionary<string, int> FeatureFrequencies { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> CandidateStableFeatures { get; set; } = new(StringComparer.Ordinal);
    public string CandidateRawSignature { get; set; } = string.Empty;
}

public sealed class RcmRawFrameEvidence
{
    public int SequenceNumber { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string RawFrameHex { get; set; } = string.Empty;
    public List<int> RawFrameBytes { get; set; } = new();
}

public sealed class RcmStateComparison
{
    public DateTimeOffset? ComparedAt { get; set; }
    public List<string> CommonFeatures { get; set; } = new();
    public List<string> UniqueFeaturesVoltageRemoved { get; set; } = new();
    public List<string> UniqueFeaturesVoltageApplied24V { get; set; } = new();
    public List<string> RepeatableDifferences { get; set; } = new();
    public List<string> CandidateTransitionEvidence { get; set; } = new();
    public bool DecoderVerified { get; set; }
}
