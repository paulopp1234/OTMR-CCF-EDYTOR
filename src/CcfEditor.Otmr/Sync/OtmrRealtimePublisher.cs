using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.Otmr.Sync;

public sealed class OtmrRealtimePublisherConfiguration
{
    public bool Enabled { get; init; }
    public string ServerUrl { get; init; } = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer;
    public string ApiToken { get; init; } = string.Empty;
    public bool AllowInsecureKnownTestServer { get; init; }
    public bool AllowInsecureLocalhostForTests { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

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
        $"ServerUrl={ServerUrl}; Enabled={Enabled}; AllowInsecureKnownTestServer={AllowInsecureKnownTestServer}; ApiToken=[REDACTED]";
}

public sealed record OtmrRealtimeDecodedState(
    string? VehicleIdentifier,
    DateTimeOffset TimestampUtc,
    string? SourceConnectionId,
    string? RcmProfileFilename,
    string? RcmProfileSha256,
    IReadOnlyList<RcmVerifiedLiveSignal> Signals);

public sealed record OtmrRealtimePublisherStatus(
    bool Enabled,
    DateTimeOffset? LastSendUtc,
    string LastResult,
    string? LastError,
    string? StatusReason);

public sealed record OtmrRealtimePublisherDiagnostics(
    long VerifiedStateChanges,
    long PublishAttempts,
    long PublishSuccesses,
    long PublishFailures);

public sealed class OtmrRealtimePublisherStatusChangedEventArgs(OtmrRealtimePublisherStatus status) : EventArgs
{
    public OtmrRealtimePublisherStatus Status { get; } = status;
}

public interface IOtmrRealtimeClient
{
    Task<OtmrRealtimeUpdateAcknowledgement> PublishAsync(
        OtmrRealtimeUpdateRequest update,
        CancellationToken cancellationToken = default);
}

public sealed class HttpOtmrRealtimeClient(HttpClient httpClient, OtmrSyncOptions options) : IOtmrRealtimeClient
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly OtmrSyncOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<OtmrRealtimeUpdateAcknowledgement> PublishAsync(
        OtmrRealtimeUpdateRequest update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        _options.Validate();
        if (update.ApiVersion != OtmrApiContract.Version)
            throw new InvalidOperationException($"Unsupported OTMR realtime API version {update.ApiVersion}.");

        Uri endpoint = new(_options.BaseUrl!, OtmrRealtimeContract.LiveRoute(update.VehicleIdentifier));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken!.Trim());
        request.Headers.TryAddWithoutValidation("X-OTMR-Api-Version", OtmrApiContract.Version.ToString());
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(update, OtmrApiV1Json.Options));
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
            if (!response.IsSuccessStatusCode)
                throw new OtmrRealtimeHttpException((int)response.StatusCode, LimitResponse(responseBody));

            OtmrRealtimeUpdateAcknowledgement? acknowledgement =
                JsonSerializer.Deserialize<OtmrRealtimeUpdateAcknowledgement>(responseBody, OtmrApiV1Json.Options);
            if (acknowledgement is null || !acknowledgement.Accepted ||
                acknowledgement.ApiVersion != OtmrApiContract.Version ||
                !string.Equals(acknowledgement.VehicleIdentifier, update.VehicleIdentifier, StringComparison.Ordinal))
                throw new InvalidDataException("The OTMR realtime server returned an invalid acknowledgement.");
            return acknowledgement;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The OTMR realtime update exceeded the configured timeout of {_options.RequestTimeout}.", ex);
        }
    }

    private static string LimitResponse(string value) => value.Length <= 2048 ? value : value[..2048];
}

public sealed class OtmrRealtimeHttpException(int statusCode, string responseBody)
    : Exception($"OTMR realtime update failed with HTTP {statusCode}. {responseBody}".Trim())
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Bounded latest-state publisher. Serial/UI callers only perform validation and
/// a non-blocking channel write; all HTTP work runs on the private worker.
/// </summary>
public sealed class OtmrRealtimePublisher : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly HttpClient _httpClient;
    private readonly Func<OtmrSyncOptions, IOtmrRealtimeClient> _clientFactory;
    private readonly Channel<QueuedState> _pending = Channel.CreateBounded<QueuedState>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource _configurationCancellation = new();
    private readonly Task _worker;
    private OtmrRealtimePublisherConfiguration _configuration = new();
    private long _configurationGeneration;
    private string? _publishedStreamKey;
    private Dictionary<Guid, PublishedSignal> _lastPublished = new();
    private OtmrRealtimePublisherStatus _status = new(false, null, "DISABLED", null, "Operator opt-in is disabled.");
    private long _verifiedStateChanges;
    private long _publishAttempts;
    private long _publishSuccesses;
    private long _publishFailures;

    public OtmrRealtimePublisher(
        HttpClient httpClient,
        Func<OtmrSyncOptions, IOtmrRealtimeClient>? clientFactory = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _clientFactory = clientFactory ?? (options => new HttpOtmrRealtimeClient(_httpClient, options));
        _worker = Task.Run(ProcessAsync);
    }

    public event EventHandler<OtmrRealtimePublisherStatusChangedEventArgs>? StatusChanged;

    public OtmrRealtimePublisherStatus Status
    {
        get
        {
            lock (_sync)
                return _status;
        }
    }

    public OtmrRealtimePublisherDiagnostics Diagnostics => new(
        Interlocked.Read(ref _verifiedStateChanges),
        Interlocked.Read(ref _publishAttempts),
        Interlocked.Read(ref _publishSuccesses),
        Interlocked.Read(ref _publishFailures));

    public void Configure(OtmrRealtimePublisherConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_sync)
        {
            _configurationCancellation.Cancel();
            _configurationCancellation.Dispose();
            _configurationCancellation = new CancellationTokenSource();
            _configuration = configuration;
            _configurationGeneration++;
            _publishedStreamKey = null;
            _lastPublished.Clear();
        }
        SetStatus(new(
            configuration.Enabled,
            Status.LastSendUtc,
            configuration.Enabled ? "WAITING FOR GENUINE VERIFIED LIVE STATE" : "DISABLED",
            null,
            configuration.Enabled ? "No genuine verified live state received yet." : "Operator opt-in is disabled."));
    }

    public bool TryPublish(OtmrRealtimeDecodedState decodedState)
    {
        ArgumentNullException.ThrowIfNull(decodedState);
        OtmrRealtimePublisherConfiguration configuration;
        long generation;
        lock (_sync)
        {
            configuration = _configuration;
            generation = _configurationGeneration;
        }

        if (!configuration.Enabled)
            return false;
        if (string.IsNullOrWhiteSpace(decodedState.VehicleIdentifier))
        {
            SetStatus(new(true, Status.LastSendUtc, "NOT SENT", null,
                "REALTIME NOT SENT — VEHICLE ID UNKNOWN"));
            return false;
        }

        OtmrRealtimeSignalUpdate[] safeSignals = decodedState.Signals
            .Where(IsPublishable)
            .Select(ToUpdate)
            .ToArray();
        if (safeSignals.Length == 0)
        {
            SetStatus(new(true, Status.LastSendUtc, "NOT SENT", null,
                "The genuine frame produced no explicitly verified ACTIVE/INACTIVE signal state."));
            return false;
        }

        return _pending.Writer.TryWrite(new QueuedState(
            generation,
            configuration,
            decodedState.VehicleIdentifier.Trim(),
            decodedState.TimestampUtc.ToUniversalTime(),
            decodedState.SourceConnectionId,
            decodedState.RcmProfileFilename,
            decodedState.RcmProfileSha256,
            safeSignals));
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (QueuedState queued in _pending.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                CancellationToken configurationToken;
                lock (_sync)
                {
                    if (queued.Generation != _configurationGeneration || !_configuration.Enabled)
                        continue;
                    configurationToken = _configurationCancellation.Token;
                }

                string streamKey = $"{queued.VehicleIdentifier}\n{queued.ProfileSha256 ?? queued.ProfileFilename ?? string.Empty}";
                OtmrRealtimeSignalUpdate[] changed;
                lock (_sync)
                {
                    if (!string.Equals(_publishedStreamKey, streamKey, StringComparison.Ordinal))
                    {
                        _publishedStreamKey = streamKey;
                        _lastPublished.Clear();
                    }
                    changed = queued.Signals
                        .Where(signal => !_lastPublished.TryGetValue(signal.SignalId, out PublishedSignal? previous) ||
                                         previous is null || !previous.Matches(signal))
                        .ToArray();
                }

                if (changed.Length == 0)
                {
                    SetStatus(new(true, Status.LastSendUtc, "UNCHANGED - NOT SENT", null,
                        "Latest genuine verified states match the last successful realtime update."));
                    continue;
                }

                Interlocked.Add(ref _verifiedStateChanges, changed.Length);

                var request = new OtmrRealtimeUpdateRequest(
                    OtmrApiContract.Version,
                    queued.VehicleIdentifier,
                    queued.TimestampUtc,
                    queued.SourceConnectionId,
                    queued.ProfileFilename,
                    queued.ProfileSha256,
                    changed);
                DateTimeOffset attemptedUtc = DateTimeOffset.UtcNow;
                try
                {
                    Interlocked.Increment(ref _publishAttempts);
                    OtmrSyncOptions options = queued.Configuration.CreateClientOptions();
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        _lifetime.Token, configurationToken);
                    OtmrRealtimeUpdateAcknowledgement acknowledgement = await _clientFactory(options)
                        .PublishAsync(request, linked.Token).ConfigureAwait(false);
                    bool stillCurrent;
                    lock (_sync)
                    {
                        stillCurrent = queued.Generation == _configurationGeneration && _configuration.Enabled;
                        if (stillCurrent)
                        {
                            foreach (OtmrRealtimeSignalUpdate signal in changed)
                                _lastPublished[signal.SignalId] = PublishedSignal.From(signal);
                        }
                    }
                    if (!stillCurrent)
                        continue;
                    Interlocked.Increment(ref _publishSuccesses);
                    SetStatus(new(true, attemptedUtc,
                        $"SENT {acknowledgement.SignalUpdateCount} VERIFIED UPDATE(S)", null, null));
                }
                catch (OperationCanceledException) when (configurationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
                {
                    // Disabling/reconfiguring/disposal cancels only this network operation.
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _publishFailures);
                    SetStatus(new(true, attemptedUtc, "SEND FAILED",
                        OtmrManualSyncService.Sanitize(ex.Message, queued.Configuration.ApiToken),
                        "OTMR live reception and local recording continue independently."));
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Normal disposal.
        }
    }

    private static bool IsPublishable(RcmVerifiedLiveSignal signal) =>
        signal.PinId != Guid.Empty &&
        string.Equals(signal.VerificationStatus, OtmrRealtimeContract.Verified, StringComparison.Ordinal) &&
        signal.State is RcmDecodedElectricalState.Active or RcmDecodedElectricalState.Inactive &&
        signal.RawObservedValue is >= byte.MinValue and <= byte.MaxValue;

    private static OtmrRealtimeSignalUpdate ToUpdate(RcmVerifiedLiveSignal signal) => new(
        signal.PinId,
        signal.Connector,
        signal.Pin,
        signal.Function,
        signal.LogicalCard,
        signal.LogicalChannel,
        signal.State == RcmDecodedElectricalState.Active ? OtmrRealtimeContract.Active : OtmrRealtimeContract.Inactive,
        signal.RawObservedValue!.Value,
        signal.ObservedBitValue,
        OtmrRealtimeContract.Verified);

    private void SetStatus(OtmrRealtimePublisherStatus status)
    {
        lock (_sync)
            _status = status;
        try
        {
            StatusChanged?.Invoke(this, new OtmrRealtimePublisherStatusChangedEventArgs(status));
        }
        catch
        {
            // Status presentation must never terminate the publisher worker.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pending.Writer.TryComplete();
        _lifetime.Cancel();
        lock (_sync)
            _configurationCancellation.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal disposal.
        }
        lock (_sync)
        {
            _configurationCancellation.Dispose();
            _lastPublished.Clear();
        }
        _lifetime.Dispose();
    }

    private sealed record QueuedState(
        long Generation,
        OtmrRealtimePublisherConfiguration Configuration,
        string VehicleIdentifier,
        DateTimeOffset TimestampUtc,
        string? SourceConnectionId,
        string? ProfileFilename,
        string? ProfileSha256,
        IReadOnlyList<OtmrRealtimeSignalUpdate> Signals);

    private sealed record PublishedSignal(string State, int RawValue, int? ObservedBitValue)
    {
        public static PublishedSignal From(OtmrRealtimeSignalUpdate signal) =>
            new(signal.State, signal.RawValue, signal.ObservedBitValue);

        public bool Matches(OtmrRealtimeSignalUpdate signal) =>
            string.Equals(State, signal.State, StringComparison.Ordinal) &&
            RawValue == signal.RawValue &&
            ObservedBitValue == signal.ObservedBitValue;
    }
}
