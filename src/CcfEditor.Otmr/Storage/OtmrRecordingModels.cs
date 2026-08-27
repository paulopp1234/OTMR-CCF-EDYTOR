using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;
using CcfEditor.Otmr.Sync;

namespace CcfEditor.Otmr.Storage;

public sealed class OtmrRecordingSessionContext
{
    public string SoftwareVersion { get; init; } = string.Empty;
    public string ComPort { get; init; } = string.Empty;
    public string SerialSettings { get; init; } = "38400/8/N/1";
    public string? VehicleIdentifier { get; init; }
    public string? VehicleType { get; init; }
    public string? CcfFilename { get; init; }
    public string? CcfSha256 { get; init; }
    public string? RcmProfileFilename { get; init; }
    public string? RcmProfileSha256 { get; init; }
    public string? RcmProfileJsonSnapshot { get; init; }
    public string? Notes { get; init; }
}

public sealed record OtmrRecordingStatus(
    bool IsRecording,
    Guid? SessionId,
    long RawEntryCount,
    long CompleteFrameCount,
    string DatabasePath,
    string SyncState);

public sealed class OtmrRecordingStatusChangedEventArgs : EventArgs
{
    public OtmrRecordingStatusChangedEventArgs(OtmrRecordingStatus status) => Status = status;
    public OtmrRecordingStatus Status { get; }
}

public sealed record RcmProfileRecordingContext(
    string? Filename,
    string? Sha256,
    string? JsonSnapshot,
    string? VehicleIdentifier,
    string? VehicleType);

public sealed record OtmrPendingUpload(
    Guid OutboxId,
    Guid SessionId,
    DateTimeOffset CreatedAtUtc,
    int AttemptCount,
    string? LastError);

public sealed record OtmrUploadLease(
    Guid OutboxId,
    Guid SessionId,
    Guid LeaseId,
    DateTimeOffset AcquiredUtc,
    DateTimeOffset ExpiresUtc,
    int AttemptCount);

public sealed record OtmrUploadSessionMetadata(
    Guid SessionId,
    DateTimeOffset StartedUtc,
    DateTimeOffset? FinishedUtc,
    string SoftwareVersion,
    string ComPort,
    string SerialSettings,
    string? VehicleIdentifier,
    string? VehicleType,
    string? CcfFilename,
    string? CcfSha256,
    string? RcmProfileFilename,
    string? RcmProfileSha256,
    string? RcmProfileJsonSnapshot,
    string SyncState,
    DateTimeOffset CreatedUtc = default,
    string? Notes = null,
    string? RemoteSessionId = null);

public sealed record OtmrUploadRawEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Direction,
    byte[] Data,
    string? Interpretation);

public sealed record OtmrUploadLiveFrame(
    long Sequence,
    DateTimeOffset TimestampUtc,
    byte[] Data,
    string DecodeStatus,
    string? DecoderVersion);

/// <summary>
/// Legacy complete local session projection. The HTTP wire contract is the
/// strongly typed OtmrApiV1UploadRequest produced from this snapshot.
/// RcmPayloadJson remains internal and is never double-encoded on the wire.
/// </summary>
public sealed record OtmrSessionUploadPackage(
    OtmrUploadSessionMetadata Session,
    IReadOnlyList<OtmrUploadRawEntry> RawEntries,
    IReadOnlyList<OtmrUploadLiveFrame> LiveFrames,
    string RcmPayloadJson);

public static class OtmrDatabasePaths
{
    public const string DefaultRootDirectory = @"C:\OTMR_RCM";

    public static string DefaultDatabasePath =>
        Path.Combine(DefaultRootDirectory, "OTMR_RCM.db");
}

/// <summary>
/// Stable HTTP contract planned for the later online server. The Windows app
/// does not contact a server yet; completed local sessions are queued in the
/// SQLite outbox so a future uploader can use these endpoints idempotently.
/// </summary>
public static class OtmrServerSyncContract
{
    public const string ApiVersion = "v1";
    public const string AtomicSessionUploadRoute = OtmrApiContract.AtomicSessionUploadRoute;
    public const string IdempotencyHeader = "Idempotency-Key";
}

public interface IOtmrRecordingStore : IAsyncDisposable
{
    string DatabasePath { get; }
    bool IsRecording { get; }
    Guid? ActiveSessionId { get; }
    OtmrRecordingStatus GetStatus();

    event EventHandler<OtmrRecordingStatusChangedEventArgs>? StatusChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<Guid> StartSessionAsync(
        OtmrRecordingSessionContext context,
        CancellationToken cancellationToken = default);

    Task StopSessionAsync(
        DateTimeOffset stoppedAtUtc,
        CancellationToken cancellationToken = default);

    void TryRecordRaw(OtmrCaptureEntry entry);
    void TryRecordLiveFrame(DateTimeOffset timestamp, OtmrLiveFrame frame);
    void TryRecordRcmCapture(RcmPinProfile pin, RcmElectricalTestState state, RcmStateEvidence evidence);
    void TryRecordRcmComparison(RcmPinProfile pin);

    Task<IReadOnlyList<OtmrPendingUpload>> GetPendingUploadsAsync(
        int maximum = 100,
        CancellationToken cancellationToken = default);

    Task<OtmrSessionUploadPackage> BuildUploadPackageAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<OtmrApiV1UploadRequest> BuildApiV1UploadPackageAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<int> RecoverStaleUploadLeasesAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<OtmrUploadLease?> TryAcquireUploadLeaseAsync(
        Guid outboxId,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task MarkUploadSucceededAsync(
        Guid outboxId,
        Guid leaseId,
        string remoteSessionId,
        CancellationToken cancellationToken = default);

    Task MarkUploadFailedAsync(
        Guid outboxId,
        Guid leaseId,
        string error,
        CancellationToken cancellationToken = default);
}

public static class OtmrSyncStates
{
    public const string Recording = "RECORDING";
    public const string PendingUpload = "PENDING_UPLOAD";
    public const string Uploading = "UPLOADING";
    public const string Uploaded = "UPLOADED";
    public const string UploadFailed = "UPLOAD_FAILED";
}
