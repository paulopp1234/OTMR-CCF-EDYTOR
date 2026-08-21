using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

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

public static class OtmrDatabasePaths
{
    public static string DefaultDatabasePath
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "OTMR CCF Editor", "Data", "OTMR_RCM.db");
        }
    }
}

/// <summary>
/// Stable HTTP contract planned for the later online server. The Windows app
/// does not contact a server yet; completed local sessions are queued in the
/// SQLite outbox so a future uploader can use these endpoints idempotently.
/// </summary>
public static class OtmrServerSyncContract
{
    public const string ApiVersion = "v1";
    public const string CreateSessionRoute = "/api/v1/otmr/recording-sessions";
    public const string RawEntriesBatchRouteTemplate = "/api/v1/otmr/recording-sessions/{sessionId}/raw-entries";
    public const string LiveFramesBatchRouteTemplate = "/api/v1/otmr/recording-sessions/{sessionId}/live-frames";
    public const string RcmResultsRouteTemplate = "/api/v1/otmr/recording-sessions/{sessionId}/rcm-results";
    public const string CompleteSessionRouteTemplate = "/api/v1/otmr/recording-sessions/{sessionId}/complete";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const int RecommendedBatchSize = 500;
}

public interface IOtmrRecordingStore : IAsyncDisposable
{
    string DatabasePath { get; }
    bool IsRecording { get; }
    Guid? ActiveSessionId { get; }
    OtmrRecordingStatus GetStatus();

    event EventHandler<OtmrRecordingStatusChangedEventArgs>? StatusChanged;

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

    Task MarkUploadSucceededAsync(
        Guid outboxId,
        string remoteSessionId,
        CancellationToken cancellationToken = default);

    Task MarkUploadFailedAsync(
        Guid outboxId,
        string error,
        CancellationToken cancellationToken = default);
}
