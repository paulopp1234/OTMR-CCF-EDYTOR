using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CcfEditor.Otmr.Sync;

public static class OtmrApiContract
{
    public const int Version = 1;
    public const string AtomicSessionUploadRoute = "/api/v1/otmr/recording-sessions";
}

public sealed record OtmrApiV1UploadRequest(
    [property: JsonPropertyName("apiVersion"), JsonPropertyOrder(0)] int ApiVersion,
    [property: JsonPropertyName("session"), JsonPropertyOrder(1)] OtmrApiV1Session Session,
    [property: JsonPropertyName("rawEntries"), JsonPropertyOrder(2)] IReadOnlyList<OtmrApiV1RawEntry> RawEntries,
    [property: JsonPropertyName("liveFrames"), JsonPropertyOrder(3)] IReadOnlyList<OtmrApiV1LiveFrame> LiveFrames,
    [property: JsonPropertyName("rcm"), JsonPropertyOrder(4)] OtmrApiV1RcmPayload Rcm,
    [property: JsonPropertyName("manifest"), JsonPropertyOrder(5)] OtmrApiV1Manifest Manifest);

public sealed record OtmrApiV1Session(
    [property: JsonPropertyName("sessionId"), JsonPropertyOrder(0)] Guid SessionId,
    [property: JsonPropertyName("startedUtc"), JsonPropertyOrder(1)] DateTimeOffset StartedUtc,
    [property: JsonPropertyName("finishedUtc"), JsonPropertyOrder(2)] DateTimeOffset? FinishedUtc,
    [property: JsonPropertyName("createdUtc"), JsonPropertyOrder(3)] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("softwareVersion"), JsonPropertyOrder(4)] string SoftwareVersion,
    [property: JsonPropertyName("comPort"), JsonPropertyOrder(5)] string ComPort,
    [property: JsonPropertyName("serialSettings"), JsonPropertyOrder(6)] string SerialSettings,
    [property: JsonPropertyName("vehicleIdentifier"), JsonPropertyOrder(7)] string? VehicleIdentifier,
    [property: JsonPropertyName("vehicleType"), JsonPropertyOrder(8)] string? VehicleType,
    [property: JsonPropertyName("ccfFilename"), JsonPropertyOrder(9)] string? CcfFilename,
    [property: JsonPropertyName("ccfSha256"), JsonPropertyOrder(10)] string? CcfSha256,
    [property: JsonPropertyName("rcmProfileFilename"), JsonPropertyOrder(11)] string? RcmProfileFilename,
    [property: JsonPropertyName("rcmProfileSha256"), JsonPropertyOrder(12)] string? RcmProfileSha256,
    [property: JsonPropertyName("rcmProfileJsonSnapshot"), JsonPropertyOrder(13)] string? RcmProfileJsonSnapshot,
    [property: JsonPropertyName("notes"), JsonPropertyOrder(14)] string? Notes);

public sealed record OtmrApiV1RawEntry(
    [property: JsonPropertyName("sessionId"), JsonPropertyOrder(0)] Guid SessionId,
    [property: JsonPropertyName("sequence"), JsonPropertyOrder(1)] long Sequence,
    [property: JsonPropertyName("timestampUtc"), JsonPropertyOrder(2)] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("direction"), JsonPropertyOrder(3)] string Direction,
    [property: JsonPropertyName("data"), JsonPropertyOrder(4)] byte[] Data,
    [property: JsonPropertyName("interpretation"), JsonPropertyOrder(5)] string? Interpretation);

public sealed record OtmrApiV1LiveFrame(
    [property: JsonPropertyName("sessionId"), JsonPropertyOrder(0)] Guid SessionId,
    [property: JsonPropertyName("sequence"), JsonPropertyOrder(1)] long Sequence,
    [property: JsonPropertyName("timestampUtc"), JsonPropertyOrder(2)] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("data"), JsonPropertyOrder(3)] byte[] Data,
    [property: JsonPropertyName("decodeStatus"), JsonPropertyOrder(4)] string DecodeStatus,
    [property: JsonPropertyName("decoderVersion"), JsonPropertyOrder(5)] string? DecoderVersion);

public sealed record OtmrApiV1RcmPayload(
    [property: JsonPropertyName("inputs"), JsonPropertyOrder(0)] IReadOnlyList<OtmrApiV1RcmInput> Inputs,
    [property: JsonPropertyName("captures"), JsonPropertyOrder(1)] IReadOnlyList<OtmrApiV1RcmCapture> Captures,
    [property: JsonPropertyName("captureFrames"), JsonPropertyOrder(2)] IReadOnlyList<OtmrApiV1RcmCaptureFrame> CaptureFrames,
    [property: JsonPropertyName("comparisons"), JsonPropertyOrder(3)] IReadOnlyList<OtmrApiV1RcmComparison> Comparisons);

public sealed record OtmrApiV1RcmInput(
    [property: JsonPropertyName("sessionId"), JsonPropertyOrder(0)] Guid SessionId,
    [property: JsonPropertyName("inputGuid"), JsonPropertyOrder(1)] Guid InputGuid,
    [property: JsonPropertyName("connector"), JsonPropertyOrder(2)] string Connector,
    [property: JsonPropertyName("pin"), JsonPropertyOrder(3)] string Pin,
    [property: JsonPropertyName("function"), JsonPropertyOrder(4)] string Function,
    [property: JsonPropertyName("role"), JsonPropertyOrder(5)] string Role,
    [property: JsonPropertyName("mio"), JsonPropertyOrder(6)] string Mio,
    [property: JsonPropertyName("physicalChannel"), JsonPropertyOrder(7)] string PhysicalChannel,
    [property: JsonPropertyName("returnOrPair"), JsonPropertyOrder(8)] string ReturnOrPair,
    [property: JsonPropertyName("testable"), JsonPropertyOrder(9)] bool Testable,
    [property: JsonPropertyName("safetyClassification"), JsonPropertyOrder(10)] string SafetyClassification,
    [property: JsonPropertyName("notes"), JsonPropertyOrder(11)] string Notes,
    [property: JsonPropertyName("recordA"), JsonPropertyOrder(12)] int? RecordA,
    [property: JsonPropertyName("recordB"), JsonPropertyOrder(13)] int? RecordB,
    [property: JsonPropertyName("logicalCard"), JsonPropertyOrder(14)] int? LogicalCard,
    [property: JsonPropertyName("logicalChannel"), JsonPropertyOrder(15)] int? LogicalChannel,
    [property: JsonPropertyName("recordAText"), JsonPropertyOrder(16)] string? RecordAText,
    [property: JsonPropertyName("recordBText"), JsonPropertyOrder(17)] string? RecordBText,
    [property: JsonPropertyName("recordType"), JsonPropertyOrder(18)] int? RecordType,
    [property: JsonPropertyName("pairRelationship"), JsonPropertyOrder(19)] string? PairRelationship,
    [property: JsonPropertyName("latestResult"), JsonPropertyOrder(20)] string LatestResult);

public sealed record OtmrApiV1RcmCapture(
    [property: JsonPropertyName("captureId"), JsonPropertyOrder(0)] Guid CaptureId,
    [property: JsonPropertyName("sessionId"), JsonPropertyOrder(1)] Guid SessionId,
    [property: JsonPropertyName("inputGuid"), JsonPropertyOrder(2)] Guid InputGuid,
    [property: JsonPropertyName("electricalState"), JsonPropertyOrder(3)] string ElectricalState,
    [property: JsonPropertyName("startedUtc"), JsonPropertyOrder(4)] DateTimeOffset? StartedUtc,
    [property: JsonPropertyName("finishedUtc"), JsonPropertyOrder(5)] DateTimeOffset? FinishedUtc,
    [property: JsonPropertyName("frameCount"), JsonPropertyOrder(6)] long FrameCount,
    [property: JsonPropertyName("noOtmrData"), JsonPropertyOrder(7)] bool NoOtmrData,
    [property: JsonPropertyName("candidateRawSignature"), JsonPropertyOrder(8)] string? CandidateRawSignature,
    [property: JsonPropertyName("createdUtc"), JsonPropertyOrder(9)] DateTimeOffset CreatedUtc);

public sealed record OtmrApiV1RcmCaptureFrame(
    [property: JsonPropertyName("captureId"), JsonPropertyOrder(0)] Guid CaptureId,
    [property: JsonPropertyName("sequence"), JsonPropertyOrder(1)] long Sequence,
    [property: JsonPropertyName("timestampUtc"), JsonPropertyOrder(2)] DateTimeOffset TimestampUtc,
    [property: JsonPropertyName("data"), JsonPropertyOrder(3)] byte[] Data);

public sealed record OtmrApiV1RcmComparison(
    [property: JsonPropertyName("sessionId"), JsonPropertyOrder(0)] Guid SessionId,
    [property: JsonPropertyName("inputGuid"), JsonPropertyOrder(1)] Guid InputGuid,
    [property: JsonPropertyName("comparedUtc"), JsonPropertyOrder(2)] DateTimeOffset? ComparedUtc,
    [property: JsonPropertyName("commonFeatures"), JsonPropertyOrder(3)] IReadOnlyList<string> CommonFeatures,
    [property: JsonPropertyName("uniqueVoltageRemoved"), JsonPropertyOrder(4)] IReadOnlyList<string> UniqueVoltageRemoved,
    [property: JsonPropertyName("uniqueVoltageApplied"), JsonPropertyOrder(5)] IReadOnlyList<string> UniqueVoltageApplied,
    [property: JsonPropertyName("repeatableDifferences"), JsonPropertyOrder(6)] IReadOnlyList<string> RepeatableDifferences,
    [property: JsonPropertyName("candidateTransitions"), JsonPropertyOrder(7)] IReadOnlyList<string> CandidateTransitions,
    [property: JsonPropertyName("decoderVerified"), JsonPropertyOrder(8)] bool DecoderVerified,
    [property: JsonPropertyName("result"), JsonPropertyOrder(9)] string Result);

public sealed record OtmrApiV1Manifest(
    [property: JsonPropertyName("rawEntryCount"), JsonPropertyOrder(0)] long RawEntryCount,
    [property: JsonPropertyName("liveFrameCount"), JsonPropertyOrder(1)] long LiveFrameCount,
    [property: JsonPropertyName("rcmInputCount"), JsonPropertyOrder(2)] long RcmInputCount,
    [property: JsonPropertyName("rcmCaptureCount"), JsonPropertyOrder(3)] long RcmCaptureCount,
    [property: JsonPropertyName("rcmCaptureFrameCount"), JsonPropertyOrder(4)] long RcmCaptureFrameCount,
    [property: JsonPropertyName("rcmComparisonCount"), JsonPropertyOrder(5)] long RcmComparisonCount,
    [property: JsonPropertyName("contentSha256"), JsonPropertyOrder(6)] string ContentSha256);

public sealed record OtmrApiV1UploadAcknowledgement(
    [property: JsonPropertyName("apiVersion"), JsonPropertyOrder(0)] int ApiVersion,
    [property: JsonPropertyName("sessionId"), JsonPropertyOrder(1)] Guid SessionId,
    [property: JsonPropertyName("remoteSessionId"), JsonPropertyOrder(2)] string RemoteSessionId,
    [property: JsonPropertyName("accepted"), JsonPropertyOrder(3)] bool Accepted,
    [property: JsonPropertyName("alreadyPresent"), JsonPropertyOrder(4)] bool AlreadyPresent,
    [property: JsonPropertyName("manifestSha256"), JsonPropertyOrder(5)] string ManifestSha256,
    [property: JsonPropertyName("rawEntryCount"), JsonPropertyOrder(6)] long RawEntryCount,
    [property: JsonPropertyName("liveFrameCount"), JsonPropertyOrder(7)] long LiveFrameCount,
    [property: JsonPropertyName("rcmInputCount"), JsonPropertyOrder(8)] long RcmInputCount,
    [property: JsonPropertyName("rcmCaptureCount"), JsonPropertyOrder(9)] long RcmCaptureCount,
    [property: JsonPropertyName("rcmCaptureFrameCount"), JsonPropertyOrder(10)] long RcmCaptureFrameCount,
    [property: JsonPropertyName("rcmComparisonCount"), JsonPropertyOrder(11)] long RcmComparisonCount);

public sealed record OtmrApiV1ErrorResponse(
    [property: JsonPropertyName("apiVersion"), JsonPropertyOrder(0)] int ApiVersion,
    [property: JsonPropertyName("sessionId"), JsonPropertyOrder(1)] Guid? SessionId,
    [property: JsonPropertyName("errorCode"), JsonPropertyOrder(2)] string ErrorCode,
    [property: JsonPropertyName("message"), JsonPropertyOrder(3)] string Message,
    [property: JsonPropertyName("existingManifestSha256"), JsonPropertyOrder(4)] string? ExistingManifestSha256,
    [property: JsonPropertyName("submittedManifestSha256"), JsonPropertyOrder(5)] string? SubmittedManifestSha256);

public static class OtmrApiV1Json
{
    private sealed record HashContent(
        [property: JsonPropertyName("apiVersion"), JsonPropertyOrder(0)] int ApiVersion,
        [property: JsonPropertyName("session"), JsonPropertyOrder(1)] OtmrApiV1Session Session,
        [property: JsonPropertyName("rawEntries"), JsonPropertyOrder(2)] IReadOnlyList<OtmrApiV1RawEntry> RawEntries,
        [property: JsonPropertyName("liveFrames"), JsonPropertyOrder(3)] IReadOnlyList<OtmrApiV1LiveFrame> LiveFrames,
        [property: JsonPropertyName("rcm"), JsonPropertyOrder(4)] OtmrApiV1RcmPayload Rcm);

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                .ToUniversalTime();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(OtmrApiV1UploadRequest request) =>
        JsonSerializer.Serialize(request, Options);

    public static byte[] SerializeToUtf8Bytes(OtmrApiV1UploadRequest request) =>
        JsonSerializer.SerializeToUtf8Bytes(request, Options);

    public static OtmrApiV1UploadAcknowledgement DeserializeAcknowledgement(string json) =>
        JsonSerializer.Deserialize<OtmrApiV1UploadAcknowledgement>(json, Options)
        ?? throw new InvalidDataException("The OTMR server acknowledgement was empty.");

    public static string ComputeContentSha256(
        int apiVersion,
        OtmrApiV1Session session,
        IReadOnlyList<OtmrApiV1RawEntry> rawEntries,
        IReadOnlyList<OtmrApiV1LiveFrame> liveFrames,
        OtmrApiV1RcmPayload rcm)
    {
        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(
            new HashContent(apiVersion, session, rawEntries, liveFrames, rcm), Options);
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        return options;
    }
}
