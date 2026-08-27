namespace CcfEditor.Otmr.Server;

public sealed record OtmrVehicleSummary(
    string VehicleIdentifier,
    string? VehicleType,
    DateTimeOffset LatestSessionUtc,
    int SessionCount);

public sealed record OtmrServerSessionSummary(
    Guid SessionId,
    DateTimeOffset StartedUtc,
    DateTimeOffset? FinishedUtc,
    DateTimeOffset CreatedUtc,
    string? VehicleIdentifier,
    string? VehicleType,
    string SoftwareVersion,
    string? Notes,
    long RawEntryCount,
    long LiveFrameCount);

public sealed record OtmrHistoricalRecord(
    Guid SessionId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    byte[] Data,
    string DecodeStatus,
    string? DecoderVersion);

public sealed record OtmrVehicleConfiguration(
    string VehicleIdentifier,
    Guid SessionId,
    DateTimeOffset RecordedUtc,
    string? VehicleType,
    string? CcfFilename,
    string? CcfSha256,
    string? RcmProfileFilename,
    string? RcmProfileSha256,
    string? RcmProfileJsonSnapshot);

public sealed record OtmrLiveAvailability(
    string VehicleIdentifier,
    bool LiveAvailable,
    DateTimeOffset? AsOfUtc,
    IReadOnlyList<object> Signals);
