using System.Text.Json.Serialization;

namespace CcfEditor.Otmr.Sync;

public static class OtmrRealtimeContract
{
    public const string LiveRouteTemplate = "/api/v1/otmr/vehicles/{vehicleIdentifier}/live";
    public const string Verified = "VERIFIED";
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";

    public static string LiveRoute(string vehicleIdentifier)
    {
        if (string.IsNullOrWhiteSpace(vehicleIdentifier))
            throw new ArgumentException("A vehicle identifier is required.", nameof(vehicleIdentifier));
        return $"/api/v1/otmr/vehicles/{Uri.EscapeDataString(vehicleIdentifier.Trim())}/live";
    }
}

public sealed record OtmrRealtimeUpdateRequest(
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("vehicleIdentifier")] string VehicleIdentifier,
    [property: JsonPropertyName("timestampUtc")] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("sourceConnectionId")] string? SourceConnectionId,
    [property: JsonPropertyName("rcmProfileFilename")] string? RcmProfileFilename,
    [property: JsonPropertyName("rcmProfileSha256")] string? RcmProfileSha256,
    [property: JsonPropertyName("signals")] IReadOnlyList<OtmrRealtimeSignalUpdate> Signals,
    [property: JsonPropertyName("isSessionStart")] bool IsSessionStart = false);

public sealed record OtmrRealtimeSignalUpdate(
    [property: JsonPropertyName("signalId")] Guid SignalId,
    [property: JsonPropertyName("connector")] string Connector,
    [property: JsonPropertyName("pin")] string Pin,
    [property: JsonPropertyName("function")] string Function,
    [property: JsonPropertyName("logicalCard")] int? LogicalCard,
    [property: JsonPropertyName("logicalChannel")] int? LogicalChannel,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("rawValue")] int RawValue,
    [property: JsonPropertyName("observedBitValue")] int? ObservedBitValue,
    [property: JsonPropertyName("verification")] string Verification);

public sealed record OtmrRealtimeUpdateAcknowledgement(
    [property: JsonPropertyName("apiVersion")] int ApiVersion,
    [property: JsonPropertyName("vehicleIdentifier")] string VehicleIdentifier,
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("asOfUtc")] DateTimeOffset AsOfUtc,
    [property: JsonPropertyName("signalUpdateCount")] int SignalUpdateCount);
