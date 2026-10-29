using System.Net;
using System.Text.Json;
using CcfEditor.Otmr.Storage;

namespace CcfEditor.Otmr.Sync;

public sealed class OtmrManualSyncConfiguration
{
    public string ServerUrl { get; init; } = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer;
    public string ApiToken { get; init; } = string.Empty;
    public bool SyncEnabled { get; init; }
    public bool AllowInsecureKnownTestServer { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public Uri ValidateServerUri()
    {
        if (!Uri.TryCreate(ServerUrl?.Trim(), UriKind.Absolute, out Uri? baseUrl))
            throw new InvalidOperationException("An absolute OTMR server URL is required.");
        if (!string.IsNullOrEmpty(baseUrl.Query) || !string.IsNullOrEmpty(baseUrl.Fragment))
            throw new InvalidOperationException("The OTMR server URL must not contain a query or fragment.");

        bool secure = baseUrl.Scheme == Uri.UriSchemeHttps;
        bool knownTestServer = AllowInsecureKnownTestServer &&
                               Uri.Compare(
                                   baseUrl,
                                   new Uri(OtmrSyncOptions.KnownInsecureDigitalOceanTestServer),
                                   UriComponents.HttpRequestUrl,
                                   UriFormat.SafeUnescaped,
                                   StringComparison.OrdinalIgnoreCase) == 0;
        if (!secure && !knownTestServer)
            throw new InvalidOperationException("HTTPS is required. HTTP is permitted only for the explicitly enabled current DigitalOcean test server.");
        return baseUrl;
    }

    public OtmrSyncOptions CreateClientOptions()
    {
        Uri baseUrl = ValidateServerUri();
        var options = new OtmrSyncOptions
        {
            Enabled = SyncEnabled,
            BaseUrl = baseUrl,
            ApiToken = ApiToken,
            RequestTimeout = RequestTimeout,
            AllowInsecureKnownTestServer = AllowInsecureKnownTestServer
        };
        options.Validate();
        return options;
    }

    public override string ToString() =>
        $"ServerUrl={ServerUrl}; SyncEnabled={SyncEnabled}; AllowInsecureKnownTestServer={AllowInsecureKnownTestServer}; ApiToken=[REDACTED]";
}

public sealed record OtmrManualSyncStatus(
    int PendingUploadCount,
    DateTimeOffset? LastSyncAttemptUtc,
    string LastSyncResult,
    string CurrentSessionSyncState,
    string? LastError);

public sealed record OtmrServerConnectivityResult(bool IsAvailable, string Message);

/// <summary>
/// Explicit operator-driven synchronization facade. Construction and status refresh
/// perform local SQLite work only. Network access occurs only in TestServerAsync or
/// SynchronizePendingAsync, both of which must be called by an operator action.
/// </summary>
public sealed class OtmrManualSyncService
{
    private readonly IOtmrRecordingStore _store;
    private readonly HttpClient _httpClient;
    private readonly Func<OtmrSyncOptions, IOtmrSyncClient> _syncClientFactory;
    private DateTimeOffset? _lastSyncAttemptUtc;
    private string _lastSyncResult = "Not attempted";
    private string? _lastError;

    public OtmrManualSyncService(
        IOtmrRecordingStore store,
        HttpClient httpClient,
        Func<OtmrSyncOptions, IOtmrSyncClient>? syncClientFactory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _syncClientFactory = syncClientFactory ?? (options => new HttpOtmrSyncClient(_httpClient, options));
    }

    public async Task<OtmrManualSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OtmrPendingUpload> pending = await _store.GetPendingUploadsAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);
        return CreateStatus(pending);
    }

    public async Task<OtmrServerConnectivityResult> TestServerAsync(
        OtmrManualSyncConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Uri baseUrl = configuration.ValidateServerUri();
        Uri healthUrl = new(baseUrl, "/health");
        using var request = new HttpRequestMessage(HttpMethod.Get, healthUrl);
        // /health is intentionally anonymous. Never attach the saved bearer token.
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(configuration.RequestTimeout);
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                return new(false, $"Server returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");

            await using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using JsonDocument json = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token).ConfigureAwait(false);
            bool healthy = json.RootElement.TryGetProperty("status", out JsonElement status) &&
                           string.Equals(status.GetString(), "healthy", StringComparison.OrdinalIgnoreCase);
            return healthy ? new(true, "SERVER OK") : new(false, "The health response did not report healthy status.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new(false, Sanitize(ex.Message, configuration.ApiToken));
        }
    }

    public async Task<IReadOnlyList<OtmrSyncItemResult>> SynchronizePendingAsync(
        OtmrManualSyncConfiguration configuration,
        int maximum = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _lastSyncAttemptUtc = DateTimeOffset.UtcNow;
        _lastError = null;
        try
        {
            OtmrSyncOptions options = configuration.CreateClientOptions();
            IOtmrSyncClient client = new RedactingSyncClient(_syncClientFactory(options), configuration.ApiToken);
            var coordinator = new OtmrSyncCoordinator(_store, client);
            IReadOnlyList<OtmrSyncItemResult> results = await coordinator
                .SynchronizePendingAsync(maximum, cancellationToken).ConfigureAwait(false);

            int uploaded = results.Count(x => x.Uploaded);
            OtmrSyncItemResult[] failed = results.Where(x => !x.Uploaded).ToArray();
            if (failed.Length == 0)
            {
                _lastSyncResult = results.Count == 0
                    ? "No pending recordings"
                    : $"UPLOADED: {uploaded} recording(s)";
            }
            else
            {
                _lastSyncResult = $"UPLOAD_FAILED: {failed.Length} of {results.Count}";
                _lastError = Sanitize(string.Join(" | ", failed.Select(x => x.Error).Where(x => !string.IsNullOrWhiteSpace(x))), configuration.ApiToken);
            }
            return results;
        }
        catch (Exception ex)
        {
            _lastSyncResult = "UPLOAD_FAILED";
            _lastError = Sanitize(ex.Message, configuration.ApiToken);
            throw new OtmrManualSyncException(_lastError, ex);
        }
    }

    private OtmrManualSyncStatus CreateStatus(IReadOnlyList<OtmrPendingUpload> pending) => new(
        pending.Count,
        _lastSyncAttemptUtc ?? pending.Where(x => x.LastAttemptUtc.HasValue).Select(x => x.LastAttemptUtc).Max(),
        _lastSyncResult,
        _store.GetStatus().SyncState,
        _lastError ?? pending.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.LastError))?.LastError);

    internal static string Sanitize(string? value, string? token)
    {
        string safe = string.IsNullOrWhiteSpace(value) ? "Unknown synchronization error." : value;
        return string.IsNullOrEmpty(token)
            ? safe
            : safe.Replace(token, "[REDACTED]", StringComparison.Ordinal);
    }

    private sealed class RedactingSyncClient(IOtmrSyncClient inner, string token) : IOtmrSyncClient
    {
        public async Task<OtmrApiV1UploadAcknowledgement> UploadSessionAsync(
            OtmrApiV1UploadRequest package,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await inner.UploadSessionAsync(package, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new OtmrManualSyncException(Sanitize(ex.Message, token), ex);
            }
        }
    }
}

public sealed class OtmrManualSyncException : Exception
{
    public OtmrManualSyncException(string message, Exception innerException) : base(message, innerException) { }
}
