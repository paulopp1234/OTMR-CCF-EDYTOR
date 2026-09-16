using System.Globalization;
using System.Text.Json;
using CcfEditor.Otmr.Server;
using CcfEditor.Otmr.Storage;
using CcfEditor.Otmr.Sync;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
OtmrServerOptions startupOptions = builder.Configuration
    .GetSection(OtmrServerOptions.SectionName)
    .Get<OtmrServerOptions>() ?? new OtmrServerOptions();

builder.WebHost.UseUrls(startupOptions.ListenUrl);
builder.WebHost.ConfigureKestrel(server =>
    server.Limits.MaxRequestBodySize = startupOptions.MaximumUploadBodyBytes);
builder.Services.Configure<OtmrServerOptions>(builder.Configuration.GetSection(OtmrServerOptions.SectionName));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = null;
});
builder.Services.AddSingleton<BearerTokenAuthenticator>();
builder.Services.AddSingleton<IOtmrUploadPersistenceHook, NoOpOtmrUploadPersistenceHook>();
builder.Services.AddSingleton<IOtmrServerDatabase, OtmrServerDatabase>();

WebApplication app = builder.Build();

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    IOtmrServerDatabase database = scope.ServiceProvider.GetRequiredService<IOtmrServerDatabase>();
    await database.InitializeAsync().ConfigureAwait(false);
}

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        await next(context).ConfigureAwait(false);
        return;
    }

    BearerTokenAuthenticator authenticator = context.RequestServices.GetRequiredService<BearerTokenAuthenticator>();
    if (!authenticator.IsAuthorized(context.Request))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        var error = new OtmrApiV1ErrorResponse(OtmrApiContract.Version, null, "UNAUTHORIZED", "A valid bearer token is required.", null, null);
        await JsonSerializer.SerializeAsync(context.Response.Body, error, OtmrApiV1Json.Options, context.RequestAborted).ConfigureAwait(false);
        return;
    }

    await next(context).ConfigureAwait(false);
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost(OtmrApiContract.AtomicSessionUploadRoute, async (
    HttpContext context,
    IOtmrServerDatabase database,
    IOptions<OtmrServerOptions> configuredOptions,
    ILogger<Program> logger) =>
{
    OtmrServerOptions options = configuredOptions.Value;
    if (context.Request.ContentLength is long length && length > options.MaximumUploadBodyBytes)
        return ApiError(StatusCodes.Status413PayloadTooLarge, null, "PAYLOAD_TOO_LARGE", "The upload exceeds the configured request size limit.");

    OtmrApiV1UploadRequest? request;
    try
    {
        request = await JsonSerializer.DeserializeAsync<OtmrApiV1UploadRequest>(
            context.Request.Body, OtmrApiV1Json.Options, context.RequestAborted).ConfigureAwait(false);
    }
    catch (JsonException)
    {
        return ApiError(StatusCodes.Status400BadRequest, null, "INVALID_JSON", "The request is not valid API v1 JSON.");
    }

    OtmrUploadValidationResult validation = OtmrApiV1ServerValidator.Validate(request);
    if (!validation.IsValid)
        return ApiError(StatusCodes.Status400BadRequest, request?.Session?.SessionId, validation.ErrorCode, validation.Message, submitted: request?.Manifest?.ContentSha256);

    OtmrApiV1UploadRequest validRequest = request!;
    string idempotencyKey = context.Request.Headers[OtmrServerSyncContract.IdempotencyHeader].ToString();
    if (!Guid.TryParseExact(idempotencyKey, "D", out Guid headerSessionId) || headerSessionId != validRequest.Session.SessionId)
        return ApiError(StatusCodes.Status400BadRequest, validRequest.Session.SessionId, "INVALID_IDEMPOTENCY_KEY", "Idempotency-Key must equal sessionId in UUID D format.");

    try
    {
        ServerUploadResult stored = await database.StoreUploadAsync(validRequest, context.RequestAborted).ConfigureAwait(false);
        if (stored.Disposition == ServerUploadDisposition.Conflict)
        {
            return ApiError(StatusCodes.Status409Conflict, validRequest.Session.SessionId, "SESSION_CONFLICT",
                "The session identity is already stored with different evidence.", stored.Receipt.ManifestSha256, validRequest.Manifest.ContentSha256);
        }

        ServerUploadReceipt receipt = stored.Receipt;
        var acknowledgement = new OtmrApiV1UploadAcknowledgement(
            OtmrApiContract.Version,
            validRequest.Session.SessionId,
            validRequest.Session.SessionId.ToString("D"),
            Accepted: true,
            AlreadyPresent: stored.Disposition == ServerUploadDisposition.AlreadyPresent,
            receipt.ManifestSha256,
            receipt.RawEntryCount,
            receipt.LiveFrameCount,
            receipt.RcmInputCount,
            receipt.RcmCaptureCount,
            receipt.RcmCaptureFrameCount,
            receipt.RcmComparisonCount);
        return Results.Json(acknowledgement, OtmrApiV1Json.Options, statusCode: StatusCodes.Status200OK);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Atomic OTMR upload failed for session {SessionId}.", validRequest.Session.SessionId);
        return ApiError(StatusCodes.Status500InternalServerError, validRequest.Session.SessionId, "UPLOAD_FAILED", "The recording session was not accepted; no partial upload was committed.");
    }
});

app.MapGet("/api/v1/otmr/vehicles", async (
    int? limit,
    IOtmrServerDatabase database,
    IOptions<OtmrServerOptions> configuredOptions,
    CancellationToken cancellationToken) =>
{
    int maximum = Bound(limit, configuredOptions.Value.MaximumQueryResultCount);
    return Results.Ok(await database.GetVehiclesAsync(maximum, cancellationToken).ConfigureAwait(false));
});

app.MapGet("/api/v1/otmr/vehicles/{vehicleIdentifier}/sessions", async (
    string vehicleIdentifier,
    int? offset,
    int? limit,
    IOtmrServerDatabase database,
    IOptions<OtmrServerOptions> configuredOptions,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(vehicleIdentifier))
        return Results.BadRequest(new { error = "vehicleIdentifier is required." });
    int maximum = Bound(limit, configuredOptions.Value.MaximumSessionResultCount);
    int safeOffset = Math.Max(0, offset ?? 0);
    return Results.Ok(await database.GetSessionsAsync(vehicleIdentifier, safeOffset, maximum, cancellationToken).ConfigureAwait(false));
});

app.MapGet("/api/v1/otmr/vehicles/{vehicleIdentifier}/records", async (
    string vehicleIdentifier,
    string? fromUtc,
    string? toUtc,
    int? limit,
    IOtmrServerDatabase database,
    IOptions<OtmrServerOptions> configuredOptions,
    CancellationToken cancellationToken) =>
{
    OtmrServerOptions options = configuredOptions.Value;
    if (string.IsNullOrWhiteSpace(vehicleIdentifier) || !TryUtc(fromUtc, out DateTimeOffset from) || !TryUtc(toUtc, out DateTimeOffset to) || to <= from)
        return Results.BadRequest(new { error = "A vehicle and valid bounded fromUtc/toUtc range are required." });
    if (to - from > TimeSpan.FromDays(options.MaximumRecordRangeDays))
        return Results.BadRequest(new { error = $"The requested range exceeds {options.MaximumRecordRangeDays} days." });
    int maximum = Bound(limit, options.MaximumQueryResultCount);
    return Results.Ok(await database.GetRecordsAsync(vehicleIdentifier, from, to, maximum, cancellationToken).ConfigureAwait(false));
});

app.MapGet("/api/v1/otmr/vehicles/{vehicleIdentifier}/configuration", async (
    string vehicleIdentifier,
    IOtmrServerDatabase database,
    CancellationToken cancellationToken) =>
{
    OtmrVehicleConfiguration? configuration = await database.GetConfigurationAsync(vehicleIdentifier, cancellationToken).ConfigureAwait(false);
    return configuration is null ? Results.NotFound() : Results.Ok(configuration);
});

app.MapPost(OtmrRealtimeContract.LiveRouteTemplate, async (
    string vehicleIdentifier,
    OtmrRealtimeUpdateRequest request,
    IOtmrServerDatabase database,
    IOptions<OtmrServerOptions> configuredOptions,
    CancellationToken cancellationToken) =>
{
    OtmrRealtimeValidationResult validation = OtmrRealtimeServerValidator.Validate(
        vehicleIdentifier, request, configuredOptions.Value.MaximumRealtimeSignalUpdates);
    if (!validation.IsValid)
        return ApiError(StatusCodes.Status400BadRequest, null, validation.ErrorCode, validation.Message);

    OtmrLiveUpdateDisposition disposition = await database.StoreLiveUpdateAsync(
        request, cancellationToken).ConfigureAwait(false);
    if (disposition != OtmrLiveUpdateDisposition.Accepted)
    {
        string message = disposition == OtmrLiveUpdateDisposition.OlderSessionStart
            ? "The live-session start is older than the vehicle's current live generation."
            : "The realtime update does not belong to the vehicle's current sourceConnectionId.";
        return ApiError(StatusCodes.Status409Conflict, null, disposition.ToString().ToUpperInvariant(), message);
    }
    return Results.Ok(new OtmrRealtimeUpdateAcknowledgement(
        OtmrApiContract.Version,
        request.VehicleIdentifier,
        Accepted: true,
        request.TimestampUtc.ToUniversalTime(),
        request.Signals.Count));
});

app.MapPost(OtmrApplicationHeartbeatContract.Route, async (
    OtmrApplicationHeartbeatRequest request,
    IOtmrServerDatabase database,
    CancellationToken cancellationToken) =>
{
    string? validationError = ValidateHeartbeat(request);
    if (validationError is not null)
        return ApiError(StatusCodes.Status400BadRequest, null, "INVALID_HEARTBEAT", validationError);

    OtmrApplicationHeartbeatReceipt receipt = await database
        .StoreApplicationHeartbeatAsync(request, cancellationToken)
        .ConfigureAwait(false);
    return Results.Ok(new OtmrApplicationHeartbeatAcknowledgement(
        OtmrApiContract.Version,
        receipt.AppInstanceId,
        Accepted: true,
        receipt.LastHeartbeatUtc));
});

app.MapGet(OtmrRealtimeContract.LiveRouteTemplate, async (
    string vehicleIdentifier,
    IOtmrServerDatabase database,
    IOptions<OtmrServerOptions> configuredOptions,
    CancellationToken cancellationToken) =>
{
    int staleSeconds = Math.Max(1, configuredOptions.Value.LiveStaleAfterSeconds);
    int windowsOfflineSeconds = Math.Max(1, configuredOptions.Value.WindowsAppOfflineAfterSeconds);
    OtmrLiveAvailability live = await database.GetLiveAsync(
        vehicleIdentifier,
        DateTimeOffset.UtcNow,
        TimeSpan.FromSeconds(staleSeconds),
        TimeSpan.FromSeconds(windowsOfflineSeconds),
        cancellationToken).ConfigureAwait(false);
    return Results.Ok(live);
});

app.Run();

static IResult ApiError(int status, Guid? sessionId, string code, string message, string? existing = null, string? submitted = null) =>
    Results.Json(new OtmrApiV1ErrorResponse(OtmrApiContract.Version, sessionId, code, message, existing, submitted), OtmrApiV1Json.Options, statusCode: status);

static int Bound(int? requested, int configuredMaximum) => Math.Clamp(requested ?? configuredMaximum, 1, Math.Max(1, configuredMaximum));

static bool TryUtc(string? text, out DateTimeOffset value)
{
    if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
    {
        value = value.ToUniversalTime();
        return true;
    }
    return false;
}

static string? ValidateHeartbeat(OtmrApplicationHeartbeatRequest request)
{
    if (request.ApiVersion != OtmrApiContract.Version)
        return $"apiVersion must be {OtmrApiContract.Version}.";
    if (request.AppInstanceId == Guid.Empty)
        return "appInstanceId must be a non-empty GUID.";
    if (string.IsNullOrWhiteSpace(request.ApplicationVersion) || request.ApplicationVersion.Length > 128)
        return "applicationVersion is required and must not exceed 128 characters.";
    if (request.TimestampUtc == default || request.TimestampUtc.Offset != TimeSpan.Zero)
        return "timestampUtc must be an explicit UTC timestamp.";
    if (request.VehicleIdentifier?.Length > 128 || request.SourceConnectionId?.Length > 128)
        return "Heartbeat identifiers must not exceed 128 characters.";
    if (request.OtmrLiveConnected &&
        (string.IsNullOrWhiteSpace(request.VehicleIdentifier) ||
         string.IsNullOrWhiteSpace(request.SourceConnectionId)))
        return "An active OTMR live heartbeat requires vehicleIdentifier and sourceConnectionId.";
    return null;
}

public partial class Program;
