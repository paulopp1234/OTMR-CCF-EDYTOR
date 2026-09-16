using CcfEditor.Otmr.Sync;

namespace CcfEditor.Otmr.Server;

public sealed record OtmrRealtimeValidationResult(bool IsValid, string ErrorCode, string Message)
{
    public static OtmrRealtimeValidationResult Valid { get; } = new(true, string.Empty, string.Empty);
    public static OtmrRealtimeValidationResult Invalid(string code, string message) => new(false, code, message);
}

public static class OtmrRealtimeServerValidator
{
    public static OtmrRealtimeValidationResult Validate(
        string routeVehicleIdentifier,
        OtmrRealtimeUpdateRequest? request,
        int maximumSignals)
    {
        if (request is null)
            return OtmrRealtimeValidationResult.Invalid("INVALID_REALTIME_UPDATE", "A realtime update body is required.");
        if (request.ApiVersion != OtmrApiContract.Version)
            return OtmrRealtimeValidationResult.Invalid("UNSUPPORTED_API_VERSION", "Only OTMR API v1 realtime updates are accepted.");
        if (string.IsNullOrWhiteSpace(routeVehicleIdentifier) ||
            !string.Equals(routeVehicleIdentifier, request.VehicleIdentifier, StringComparison.Ordinal))
            return OtmrRealtimeValidationResult.Invalid("VEHICLE_MISMATCH", "The route and body vehicle identifiers must match exactly.");
        if (request.TimestampUtc == default)
            return OtmrRealtimeValidationResult.Invalid("INVALID_TIMESTAMP", "timestampUtc is required.");
        if (request.TimestampUtc.ToUniversalTime() > DateTimeOffset.UtcNow.AddMinutes(5))
            return OtmrRealtimeValidationResult.Invalid("INVALID_TIMESTAMP", "timestampUtc is too far in the future.");
        if (string.IsNullOrWhiteSpace(request.SourceConnectionId) || request.SourceConnectionId.Trim().Length > 128)
            return OtmrRealtimeValidationResult.Invalid("INVALID_SOURCE_CONNECTION", "A valid sourceConnectionId is required.");
        int minimumSignals = request.IsSessionStart ? 0 : 1;
        if (request.Signals is null || request.Signals.Count < minimumSignals || request.Signals.Count > maximumSignals)
            return OtmrRealtimeValidationResult.Invalid("INVALID_SIGNALS",
                request.IsSessionStart
                    ? $"Zero to {maximumSignals} verified signal updates are allowed for a live-session start."
                    : $"One to {maximumSignals} verified signal updates are required.");
        if (request.Signals.GroupBy(signal => signal.SignalId).Any(group => group.Key == Guid.Empty || group.Count() != 1))
            return OtmrRealtimeValidationResult.Invalid("INVALID_SIGNAL_ID", "Signal IDs must be non-empty and unique in each update.");

        foreach (OtmrRealtimeSignalUpdate signal in request.Signals)
        {
            if (!string.Equals(signal.Verification, OtmrRealtimeContract.Verified, StringComparison.Ordinal))
                return OtmrRealtimeValidationResult.Invalid("UNVERIFIED_SIGNAL", "Only explicitly VERIFIED signal mappings are accepted.");
            if (signal.State is not (OtmrRealtimeContract.Active or OtmrRealtimeContract.Inactive))
                return OtmrRealtimeValidationResult.Invalid("INVALID_SIGNAL_STATE", "Only genuine ACTIVE or INACTIVE decoded states are accepted.");
            if (signal.RawValue is < byte.MinValue or > byte.MaxValue || signal.ObservedBitValue is < 0 or > 1)
                return OtmrRealtimeValidationResult.Invalid("INVALID_RAW_VALUE", "Signal raw values are outside their valid range.");
            if (string.IsNullOrWhiteSpace(signal.Connector) || string.IsNullOrWhiteSpace(signal.Pin) ||
                string.IsNullOrWhiteSpace(signal.Function))
                return OtmrRealtimeValidationResult.Invalid("INVALID_SIGNAL_IDENTITY", "Connector, pin, and function are required.");
        }

        return OtmrRealtimeValidationResult.Valid;
    }
}
