using System.Text.Json;
using CcfEditor.Otmr.Storage;

namespace CcfEditor.Otmr.Sync;

public static class OtmrApiV1PackageFactory
{
    public static OtmrApiV1UploadRequest Create(OtmrSessionUploadPackage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Session.FinishedUtc is null)
            throw new InvalidOperationException("An active recording session cannot be frozen for upload.");

        Guid sessionId = source.Session.SessionId;
        var session = new OtmrApiV1Session(
            sessionId,
            source.Session.StartedUtc.ToUniversalTime(),
            source.Session.FinishedUtc?.ToUniversalTime(),
            source.Session.CreatedUtc.ToUniversalTime(),
            source.Session.SoftwareVersion,
            source.Session.ComPort,
            source.Session.SerialSettings,
            source.Session.VehicleIdentifier,
            source.Session.VehicleType,
            source.Session.CcfFilename,
            source.Session.CcfSha256,
            source.Session.RcmProfileFilename,
            source.Session.RcmProfileSha256,
            source.Session.RcmProfileJsonSnapshot,
            source.Session.Notes);

        OtmrApiV1RawEntry[] raw = source.RawEntries
            .OrderBy(item => item.Sequence)
            .Select(item => new OtmrApiV1RawEntry(
                sessionId, item.Sequence, item.TimestampUtc.ToUniversalTime(), item.Direction,
                item.Data.ToArray(), item.Interpretation))
            .ToArray();
        OtmrApiV1LiveFrame[] live = source.LiveFrames
            .OrderBy(item => item.Sequence)
            .Select(item => new OtmrApiV1LiveFrame(
                sessionId, item.Sequence, item.TimestampUtc.ToUniversalTime(), item.Data.ToArray(),
                item.DecodeStatus, item.DecoderVersion))
            .ToArray();

        ValidateUniquePositiveSequences(raw.Select(item => item.Sequence), "raw serial");
        ValidateUniquePositiveSequences(live.Select(item => item.Sequence), "live frame");

        OtmrApiV1RcmPayload rcm = ParseRcm(source.RcmPayloadJson, sessionId);
        string hash = OtmrApiV1Json.ComputeContentSha256(OtmrApiContract.Version, session, raw, live, rcm);
        var manifest = new OtmrApiV1Manifest(
            raw.LongLength,
            live.LongLength,
            rcm.Inputs.Count,
            rcm.Captures.Count,
            rcm.CaptureFrames.Count,
            rcm.Comparisons.Count,
            hash);
        return new OtmrApiV1UploadRequest(OtmrApiContract.Version, session, raw, live, rcm, manifest);
    }

    private static OtmrApiV1RcmPayload ParseRcm(string json, Guid expectedSessionId)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        OtmrApiV1RcmInput[] inputs = RequiredArray(root, "inputs")
            .EnumerateArray()
            .Select(row => new OtmrApiV1RcmInput(
                Session(row, expectedSessionId), GuidValue(row, "input_guid"),
                Text(row, "connector"), Text(row, "pin"), Text(row, "function"),
                Text(row, "role"), Text(row, "mio"), Text(row, "physical_channel"),
                Text(row, "return_or_pair"), Bool(row, "testable"),
                Text(row, "safety_classification"), Text(row, "notes"),
                NullableInt(row, "record_a"), NullableInt(row, "record_b"),
                NullableInt(row, "logical_card"), NullableInt(row, "logical_channel"),
                NullableText(row, "record_a_text"), NullableText(row, "record_b_text"),
                NullableInt(row, "record_type"), NullableText(row, "pair_relationship"),
                Text(row, "latest_result")))
            .OrderBy(row => row.InputGuid)
            .ToArray();

        OtmrApiV1RcmCapture[] captures = RequiredArray(root, "captures")
            .EnumerateArray()
            .Select(row => new OtmrApiV1RcmCapture(
                GuidValue(row, "capture_id"), Session(row, expectedSessionId),
                GuidValue(row, "input_guid"), Text(row, "electrical_state"),
                NullableTimestamp(row, "started_utc"), NullableTimestamp(row, "finished_utc"),
                Long(row, "frame_count"), Bool(row, "no_otmr_data"),
                NullableText(row, "candidate_raw_signature"), Timestamp(row, "created_utc")))
            .OrderBy(row => row.CreatedUtc)
            .ThenBy(row => row.CaptureId)
            .ToArray();

        HashSet<Guid> captureIds = captures.Select(item => item.CaptureId).ToHashSet();
        OtmrApiV1RcmCaptureFrame[] captureFrames = RequiredArray(root, "captureFrames")
            .EnumerateArray()
            .Select(row => new OtmrApiV1RcmCaptureFrame(
                GuidValue(row, "capture_id"), Long(row, "sequence"),
                Timestamp(row, "timestamp_utc"), Bytes(row, "data")))
            .OrderBy(row => row.CaptureId)
            .ThenBy(row => row.Sequence)
            .ToArray();
        foreach (IGrouping<Guid, OtmrApiV1RcmCaptureFrame> group in captureFrames.GroupBy(frame => frame.CaptureId))
        {
            if (!captureIds.Contains(group.Key))
                throw new InvalidDataException($"RCM capture frame references missing capture {group.Key:D}.");
            ValidateUniquePositiveSequences(group.Select(frame => frame.Sequence), $"RCM capture {group.Key:D}");
        }

        OtmrApiV1RcmComparison[] comparisons = RequiredArray(root, "comparisons")
            .EnumerateArray()
            .Select(row => new OtmrApiV1RcmComparison(
                Session(row, expectedSessionId), GuidValue(row, "input_guid"),
                NullableTimestamp(row, "compared_utc"),
                StringList(row, "common_features_json"),
                StringList(row, "unique_voltage_removed_json"),
                StringList(row, "unique_voltage_applied_json"),
                StringList(row, "repeatable_differences_json"),
                StringList(row, "candidate_transition_json"),
                Bool(row, "decoder_verified"), Text(row, "result")))
            .OrderBy(row => row.InputGuid)
            .ToArray();

        HashSet<Guid> inputIds = inputs.Select(input => input.InputGuid).ToHashSet();
        if (captures.Any(capture => !inputIds.Contains(capture.InputGuid)) ||
            comparisons.Any(comparison => !inputIds.Contains(comparison.InputGuid)))
            throw new InvalidDataException("RCM payload contains evidence for an input missing from its input snapshot.");

        return new OtmrApiV1RcmPayload(inputs, captures, captureFrames, comparisons);
    }

    private static JsonElement RequiredArray(JsonElement root, string name)
    {
        JsonElement value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"RCM payload property '{name}' is not an array.");
        return value;
    }

    private static Guid Session(JsonElement row, Guid expected)
    {
        Guid actual = GuidValue(row, "session_id");
        if (actual != expected)
            throw new InvalidDataException($"RCM row session {actual:D} does not match package session {expected:D}.");
        return actual;
    }

    private static Guid GuidValue(JsonElement row, string name) =>
        Guid.Parse(Text(row, name));

    private static string Text(JsonElement row, string name) =>
        row.GetProperty(name).GetString()
        ?? throw new InvalidDataException($"RCM field '{name}' is null.");

    private static string? NullableText(JsonElement row, string name)
    {
        JsonElement value = row.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static long Long(JsonElement row, string name) => row.GetProperty(name).GetInt64();
    private static int? NullableInt(JsonElement row, string name)
    {
        JsonElement value = row.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null ? null : checked((int)value.GetInt64());
    }

    private static bool Bool(JsonElement row, string name) => row.GetProperty(name).GetInt64() != 0;
    private static DateTimeOffset Timestamp(JsonElement row, string name) =>
        DateTimeOffset.Parse(Text(row, name)).ToUniversalTime();

    private static DateTimeOffset? NullableTimestamp(JsonElement row, string name)
    {
        string? value = NullableText(row, name);
        return value is null ? null : DateTimeOffset.Parse(value).ToUniversalTime();
    }

    private static byte[] Bytes(JsonElement row, string name) => row.GetProperty(name).GetBytesFromBase64();

    private static IReadOnlyList<string> StringList(JsonElement row, string name)
    {
        string json = Text(row, name);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }

    private static void ValidateUniquePositiveSequences(IEnumerable<long> source, string label)
    {
        long[] values = source.ToArray();
        if (values.Any(value => value <= 0) || values.Distinct().Count() != values.Length)
            throw new InvalidDataException($"The {label} sequence contains a non-positive or duplicate value.");
    }
}
