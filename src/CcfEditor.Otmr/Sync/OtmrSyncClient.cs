using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CcfEditor.Otmr.Storage;

namespace CcfEditor.Otmr.Sync;

public sealed class OtmrSyncOptions
{
    public const string KnownInsecureDigitalOceanTestServer = "http://104.248.226.215/";

    public bool Enabled { get; init; }
    public Uri? BaseUrl { get; init; }
    public string? ApiToken { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public bool AllowInsecureLocalhostForTests { get; init; }
    public bool AllowInsecureKnownTestServer { get; init; }

    public void Validate()
    {
        if (!Enabled)
            throw new InvalidOperationException("OTMR synchronization is disabled.");
        if (BaseUrl is null || !BaseUrl.IsAbsoluteUri)
            throw new InvalidOperationException("An absolute OTMR server base URL is required.");
        bool allowedHttpTest = AllowInsecureLocalhostForTests && BaseUrl.IsLoopback &&
                               BaseUrl.Scheme == Uri.UriSchemeHttp;
        bool allowedKnownTestServer = AllowInsecureKnownTestServer &&
                                      Uri.Compare(
                                          BaseUrl,
                                          new Uri(KnownInsecureDigitalOceanTestServer),
                                          UriComponents.HttpRequestUrl,
                                          UriFormat.SafeUnescaped,
                                          StringComparison.OrdinalIgnoreCase) == 0;
        if (BaseUrl.Scheme != Uri.UriSchemeHttps && !allowedHttpTest && !allowedKnownTestServer)
            throw new InvalidOperationException("The OTMR server base URL must use HTTPS.");
        if (string.IsNullOrWhiteSpace(ApiToken))
            throw new InvalidOperationException("An externally supplied OTMR API token is required.");
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(10))
            throw new InvalidOperationException("OTMR request timeout must be between zero and ten minutes.");
    }
}

public interface IOtmrSyncClient
{
    Task<OtmrApiV1UploadAcknowledgement> UploadSessionAsync(
        OtmrApiV1UploadRequest package,
        CancellationToken cancellationToken = default);
}

public sealed class HttpOtmrSyncClient : IOtmrSyncClient
{
    private readonly HttpClient _httpClient;
    private readonly OtmrSyncOptions _options;

    public HttpOtmrSyncClient(HttpClient httpClient, OtmrSyncOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<OtmrApiV1UploadAcknowledgement> UploadSessionAsync(
        OtmrApiV1UploadRequest package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        _options.Validate();
        if (package.ApiVersion != OtmrApiContract.Version)
            throw new InvalidOperationException($"Unsupported OTMR API contract version {package.ApiVersion}.");

        Uri endpoint = new(_options.BaseUrl!, OtmrApiContract.AtomicSessionUploadRoute);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken!.Trim());
        request.Headers.TryAddWithoutValidation("Idempotency-Key", package.Session.SessionId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-OTMR-Api-Version", OtmrApiContract.Version.ToString());
        request.Content = new ByteArrayContent(OtmrApiV1Json.SerializeToUtf8Bytes(package));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = Encoding.UTF8.WebName
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new OtmrSyncConflictException(LimitResponse(responseBody));
            if (!response.IsSuccessStatusCode)
                throw new OtmrSyncHttpException(response.StatusCode, LimitResponse(responseBody));

            try
            {
                return OtmrApiV1Json.DeserializeAcknowledgement(responseBody);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw new OtmrSyncAcknowledgementException("The OTMR server returned an invalid acknowledgement.", ex);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The OTMR upload exceeded the configured timeout of {_options.RequestTimeout}.", ex);
        }
    }

    private static string LimitResponse(string value) =>
        value.Length <= 2048 ? value : value[..2048];
}

public sealed class OtmrSyncHttpException : Exception
{
    public OtmrSyncHttpException(HttpStatusCode statusCode, string responseBody)
        : base($"OTMR upload failed with HTTP {(int)statusCode} ({statusCode}). {responseBody}".Trim())
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class OtmrSyncConflictException : Exception
{
    public OtmrSyncConflictException(string responseBody)
        : base($"The server already has conflicting data for this OTMR session. {responseBody}".Trim()) { }
}

public sealed class OtmrSyncAcknowledgementException : Exception
{
    public OtmrSyncAcknowledgementException(string message) : base(message) { }
    public OtmrSyncAcknowledgementException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed record OtmrSyncItemResult(Guid SessionId, bool Uploaded, bool AlreadyPresent, string? Error);

public sealed class OtmrSyncCoordinator
{
    private readonly IOtmrRecordingStore _store;
    private readonly IOtmrSyncClient _client;
    private readonly TimeSpan _leaseDuration;
    private readonly Func<DateTimeOffset> _utcNow;

    public OtmrSyncCoordinator(
        IOtmrRecordingStore store,
        IOtmrSyncClient client,
        TimeSpan? leaseDuration = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _leaseDuration = leaseDuration ?? TimeSpan.FromMinutes(5);
        if (_leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<OtmrSyncItemResult>> SynchronizePendingAsync(
        int maximum = 10,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _utcNow().ToUniversalTime();
        await _store.RecoverStaleUploadLeasesAsync(now, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<OtmrPendingUpload> pending =
            await _store.GetPendingUploadsAsync(maximum, cancellationToken).ConfigureAwait(false);
        var results = new List<OtmrSyncItemResult>(pending.Count);

        foreach (OtmrPendingUpload item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OtmrUploadLease? lease = await _store.TryAcquireUploadLeaseAsync(
                item.OutboxId, _utcNow(), _leaseDuration, cancellationToken).ConfigureAwait(false);
            if (lease is null)
                continue;

            try
            {
                OtmrApiV1UploadRequest package = await _store.BuildApiV1UploadPackageAsync(
                    lease.SessionId, cancellationToken).ConfigureAwait(false);
                OtmrApiV1UploadAcknowledgement acknowledgement = await _client.UploadSessionAsync(
                    package, cancellationToken).ConfigureAwait(false);
                ValidateAcknowledgement(package, acknowledgement);
                await _store.MarkUploadSucceededAsync(
                    lease.OutboxId, lease.LeaseId, acknowledgement.RemoteSessionId, cancellationToken)
                    .ConfigureAwait(false);
                results.Add(new OtmrSyncItemResult(
                    lease.SessionId, true, acknowledgement.AlreadyPresent, null));
            }
            catch (OperationCanceledException)
            {
                await SafelyReleaseFailedLeaseAsync(lease, "Upload cancelled.").ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await SafelyReleaseFailedLeaseAsync(lease, ex.Message).ConfigureAwait(false);
                results.Add(new OtmrSyncItemResult(lease.SessionId, false, false, ex.Message));
            }
        }
        return results;
    }

    public static void ValidateAcknowledgement(
        OtmrApiV1UploadRequest package,
        OtmrApiV1UploadAcknowledgement acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(acknowledgement);
        if (acknowledgement.ApiVersion != OtmrApiContract.Version)
            throw new OtmrSyncAcknowledgementException("Server acknowledgement API version does not match v1.");
        if (acknowledgement.SessionId != package.Session.SessionId)
            throw new OtmrSyncAcknowledgementException("Server acknowledgement session ID does not match the upload.");
        if (!acknowledgement.Accepted)
            throw new OtmrSyncAcknowledgementException("Server did not positively accept the OTMR session.");
        if (string.IsNullOrWhiteSpace(acknowledgement.RemoteSessionId))
            throw new OtmrSyncAcknowledgementException("Server acknowledgement omitted its stable remote session ID.");
        if (!string.Equals(acknowledgement.ManifestSha256, package.Manifest.ContentSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new OtmrSyncAcknowledgementException("Server acknowledgement manifest SHA-256 does not match.");
        if (acknowledgement.RawEntryCount != package.Manifest.RawEntryCount ||
            acknowledgement.LiveFrameCount != package.Manifest.LiveFrameCount ||
            acknowledgement.RcmInputCount != package.Manifest.RcmInputCount ||
            acknowledgement.RcmCaptureCount != package.Manifest.RcmCaptureCount ||
            acknowledgement.RcmCaptureFrameCount != package.Manifest.RcmCaptureFrameCount ||
            acknowledgement.RcmComparisonCount != package.Manifest.RcmComparisonCount)
            throw new OtmrSyncAcknowledgementException("Server acknowledgement row counts do not match the upload manifest.");
    }

    private async Task SafelyReleaseFailedLeaseAsync(OtmrUploadLease lease, string error)
    {
        try
        {
            await _store.MarkUploadFailedAsync(
                lease.OutboxId, lease.LeaseId, error, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The lease expires and is recovered on the next coordinator run.
            // Never replace the original upload error with cleanup failure.
        }
    }
}
