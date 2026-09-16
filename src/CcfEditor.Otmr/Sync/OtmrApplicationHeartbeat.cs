using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcfEditor.Otmr.Sync;

public static class OtmrApplicationHeartbeatContract
{
    public const string Route = "/api/v1/otmr/windows-app/heartbeat";
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);
}

public sealed record OtmrApplicationHeartbeatRequest(
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("appInstanceId")] Guid AppInstanceId,
    [property: JsonPropertyName("applicationVersion")] string ApplicationVersion,
    [property: JsonPropertyName("timestampUtc")] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("otmrLiveConnected")] bool OtmrLiveConnected,
    [property: JsonPropertyName("vehicleIdentifier")] string? VehicleIdentifier,
    [property: JsonPropertyName("sourceConnectionId")] string? SourceConnectionId);

public sealed record OtmrApplicationHeartbeatAcknowledgement(
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("appInstanceId")] Guid AppInstanceId,
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("lastHeartbeatUtc")] DateTimeOffset LastHeartbeatUtc);

public sealed record OtmrApplicationPresenceContext(
    bool OtmrLiveConnected,
    string? VehicleIdentifier,
    string? SourceConnectionId);

public sealed class OtmrApplicationHeartbeatConfiguration
{
    public bool Enabled { get; init; }
    public string ServerUrl { get; init; } = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer;
    public string ApiToken { get; init; } = string.Empty;
    public bool AllowInsecureKnownTestServer { get; init; }
    public bool AllowInsecureLocalhostForTests { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public OtmrSyncOptions CreateClientOptions()
    {
        if (!Uri.TryCreate(ServerUrl?.Trim(), UriKind.Absolute, out Uri? baseUrl))
            throw new InvalidOperationException("An absolute OTMR server URL is required.");
        if (!string.IsNullOrEmpty(baseUrl.Query) || !string.IsNullOrEmpty(baseUrl.Fragment))
            throw new InvalidOperationException("The OTMR server URL must not contain a query or fragment.");
        var options = new OtmrSyncOptions
        {
            Enabled = Enabled,
            BaseUrl = baseUrl,
            ApiToken = ApiToken,
            RequestTimeout = RequestTimeout,
            AllowInsecureKnownTestServer = AllowInsecureKnownTestServer,
            AllowInsecureLocalhostForTests = AllowInsecureLocalhostForTests
        };
        options.Validate();
        return options;
    }

    public override string ToString() =>
        $"ServerUrl={ServerUrl}; Enabled={Enabled}; AllowInsecureKnownTestServer={AllowInsecureKnownTestServer}; " +
        "ApiToken=[REDACTED]";
}

public sealed record OtmrApplicationHeartbeatStatus(
    bool Enabled,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    string LastResult,
    string? LastError,
    long Attempts,
    long Successes,
    long Failures);

public interface IOtmrApplicationHeartbeatClient
{
    Task<OtmrApplicationHeartbeatAcknowledgement> SendAsync(
        OtmrApplicationHeartbeatRequest heartbeat,
        CancellationToken cancellationToken = default);
}

public sealed class HttpOtmrApplicationHeartbeatClient(
    HttpClient httpClient,
    OtmrSyncOptions options) : IOtmrApplicationHeartbeatClient
{
    public async Task<OtmrApplicationHeartbeatAcknowledgement> SendAsync(
        OtmrApplicationHeartbeatRequest heartbeat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        options.Validate();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(options.BaseUrl!, OtmrApplicationHeartbeatContract.Route));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken!.Trim());
        request.Headers.TryAddWithoutValidation("X-OTMR-Api-Version", OtmrApiContract.Version.ToString());
        request.Content = new ByteArrayContent(
            JsonSerializer.SerializeToUtf8Bytes(heartbeat, OtmrApiV1Json.Options));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = Encoding.UTF8.WebName
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OTMR application heartbeat failed with HTTP {(int)response.StatusCode}.");
        OtmrApplicationHeartbeatAcknowledgement? acknowledgement =
            JsonSerializer.Deserialize<OtmrApplicationHeartbeatAcknowledgement>(body, OtmrApiV1Json.Options);
        if (acknowledgement is null || !acknowledgement.Accepted ||
            acknowledgement.ApiVersion != OtmrApiContract.Version ||
            acknowledgement.AppInstanceId != heartbeat.AppInstanceId)
        {
            throw new InvalidDataException("The OTMR server returned an invalid heartbeat acknowledgement.");
        }
        return acknowledgement;
    }
}

public sealed class OtmrApplicationHeartbeatService : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly HttpClient _httpClient;
    private readonly Func<OtmrSyncOptions, IOtmrApplicationHeartbeatClient> _clientFactory;
    private readonly TimeSpan _interval;
    private readonly Guid _appInstanceId;
    private readonly string _applicationVersion;
    private CancellationTokenSource _configurationCancellation = new();
    private Task _loop = Task.CompletedTask;
    private long _configurationGeneration;
    private bool _disposed;
    private OtmrApplicationPresenceContext _context = new(false, null, null);
    private OtmrApplicationHeartbeatStatus _status = new(
        false, null, null, "DISABLED", null, 0, 0, 0);

    public OtmrApplicationHeartbeatService(
        HttpClient httpClient,
        Guid appInstanceId,
        string applicationVersion,
        TimeSpan? interval = null,
        Func<OtmrSyncOptions, IOtmrApplicationHeartbeatClient>? clientFactory = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (appInstanceId == Guid.Empty)
            throw new ArgumentException("A non-empty application instance ID is required.", nameof(appInstanceId));
        if (string.IsNullOrWhiteSpace(applicationVersion))
            throw new ArgumentException("An application version is required.", nameof(applicationVersion));
        _appInstanceId = appInstanceId;
        _applicationVersion = applicationVersion.Trim();
        _interval = interval ?? OtmrApplicationHeartbeatContract.DefaultInterval;
        if (_interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        _clientFactory = clientFactory ??
            (options => new HttpOtmrApplicationHeartbeatClient(_httpClient, options));
    }

    public Guid AppInstanceId => _appInstanceId;

    public OtmrApplicationHeartbeatStatus Status
    {
        get { lock (_sync) return _status; }
    }

    public void UpdateContext(OtmrApplicationPresenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_sync)
            _context = context;
    }

    public void Configure(OtmrApplicationHeartbeatConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _configurationCancellation.Cancel();
            _configurationCancellation.Dispose();
            _configurationCancellation = new CancellationTokenSource();
            long generation = ++_configurationGeneration;
            _status = _status with
            {
                Enabled = configuration.Enabled,
                LastResult = configuration.Enabled ? "WAITING TO SEND" : "DISABLED",
                LastError = null
            };
            _loop = configuration.Enabled
                ? Task.Run(() => RunAsync(generation, configuration, _configurationCancellation.Token))
                : Task.CompletedTask;
        }
    }

    private async Task RunAsync(
        long generation,
        OtmrApplicationHeartbeatConfiguration configuration,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            DateTimeOffset attemptUtc = DateTimeOffset.UtcNow;
            OtmrApplicationPresenceContext context;
            lock (_sync)
                context = _context;
            var heartbeat = new OtmrApplicationHeartbeatRequest(
                OtmrApiContract.Version,
                _appInstanceId,
                _applicationVersion,
                attemptUtc,
                context.OtmrLiveConnected,
                context.VehicleIdentifier,
                context.SourceConnectionId);
            try
            {
                OtmrSyncOptions options = configuration.CreateClientOptions();
                OtmrApplicationHeartbeatAcknowledgement acknowledgement =
                    await _clientFactory(options).SendAsync(heartbeat, cancellationToken).ConfigureAwait(false);
                UpdateStatusIfCurrent(generation, status => status with
                {
                    LastAttemptUtc = attemptUtc,
                    LastSuccessUtc = acknowledgement.LastHeartbeatUtc.ToUniversalTime(),
                    LastResult = "HEARTBEAT SENT",
                    LastError = null,
                    Attempts = status.Attempts + 1,
                    Successes = status.Successes + 1
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                UpdateStatusIfCurrent(generation, status => status with
                {
                    LastAttemptUtc = attemptUtc,
                    LastResult = "HEARTBEAT FAILED",
                    LastError = OtmrManualSyncService.Sanitize(ex.Message, configuration.ApiToken),
                    Attempts = status.Attempts + 1,
                    Failures = status.Failures + 1
                });
            }

        }
    }

    private void UpdateStatusIfCurrent(
        long generation,
        Func<OtmrApplicationHeartbeatStatus, OtmrApplicationHeartbeatStatus> update)
    {
        lock (_sync)
        {
            if (generation == _configurationGeneration && !_disposed)
                _status = update(_status);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task loop;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _configurationCancellation.Cancel();
            loop = _loop;
        }
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        lock (_sync)
            _configurationCancellation.Dispose();
    }
}
