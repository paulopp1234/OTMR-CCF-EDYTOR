using System.Security.Cryptography;
using CcfEditor.Otmr.Sync;

namespace CcfEditor.Otmr.Server;

public sealed record OtmrUploadValidationResult(bool IsValid, string ErrorCode, string Message)
{
    public static OtmrUploadValidationResult Valid { get; } = new(true, string.Empty, string.Empty);
    public static OtmrUploadValidationResult Invalid(string code, string message) => new(false, code, message);
}

public static class OtmrApiV1ServerValidator
{
    public static OtmrUploadValidationResult Validate(OtmrApiV1UploadRequest? request)
    {
        if (request is null)
            return OtmrUploadValidationResult.Invalid("INVALID_REQUEST", "The upload request is empty.");
        if (request.ApiVersion != OtmrApiContract.Version)
            return OtmrUploadValidationResult.Invalid("UNSUPPORTED_API_VERSION", $"API version {request.ApiVersion} is not supported.");
        if (request.Session is null || request.Session.SessionId == Guid.Empty)
            return OtmrUploadValidationResult.Invalid("INVALID_SESSION_ID", "A non-empty sessionId is required.");
        if (request.Session.FinishedUtc is null || request.Session.FinishedUtc < request.Session.StartedUtc)
            return OtmrUploadValidationResult.Invalid("INCOMPLETE_SESSION", "Only complete recording sessions with a valid finishedUtc may be uploaded.");
        if (string.IsNullOrWhiteSpace(request.Session.SoftwareVersion) || string.IsNullOrWhiteSpace(request.Session.ComPort) || string.IsNullOrWhiteSpace(request.Session.SerialSettings))
            return OtmrUploadValidationResult.Invalid("INVALID_SESSION_METADATA", "Software version, COM port, and serial settings are required.");
        if (request.RawEntries is null || request.LiveFrames is null || request.Rcm is null || request.Manifest is null)
            return OtmrUploadValidationResult.Invalid("INVALID_REQUEST", "All API v1 package collections and the manifest are required.");
        if (request.Rcm.Inputs is null || request.Rcm.Captures is null || request.Rcm.CaptureFrames is null || request.Rcm.Comparisons is null)
            return OtmrUploadValidationResult.Invalid("INVALID_RCM_PAYLOAD", "All typed RCM collections are required.");

        OtmrApiV1Manifest manifest = request.Manifest;
        if (manifest.RawEntryCount != request.RawEntries.Count ||
            manifest.LiveFrameCount != request.LiveFrames.Count ||
            manifest.RcmInputCount != request.Rcm.Inputs.Count ||
            manifest.RcmCaptureCount != request.Rcm.Captures.Count ||
            manifest.RcmCaptureFrameCount != request.Rcm.CaptureFrames.Count ||
            manifest.RcmComparisonCount != request.Rcm.Comparisons.Count)
        {
            return OtmrUploadValidationResult.Invalid("MANIFEST_COUNT_MISMATCH", "One or more manifest counts do not match the uploaded collections.");
        }

        Guid sessionId = request.Session.SessionId;
        if (request.RawEntries.Any(x => x is null || x.SessionId != sessionId || x.Sequence < 1 || x.Data is null) ||
            request.RawEntries.Select(x => x.Sequence).Distinct().Count() != request.RawEntries.Count)
            return OtmrUploadValidationResult.Invalid("INVALID_RAW_ENTRIES", "Raw entries must have matching session IDs, unique positive sequences, and byte data.");

        if (request.LiveFrames.Any(x => x is null || x.SessionId != sessionId || x.Sequence < 1 || !IsCompleteLiveFrame(x.Data)) ||
            request.LiveFrames.Select(x => x.Sequence).Distinct().Count() != request.LiveFrames.Count)
            return OtmrUploadValidationResult.Invalid("INVALID_LIVE_FRAMES", "Live frames must have matching session IDs, unique positive sequences, and complete FB FB ... FF data.");

        if (request.Rcm.Inputs.Any(x => x is null || x.SessionId != sessionId || x.InputGuid == Guid.Empty) ||
            request.Rcm.Inputs.Select(x => x.InputGuid).Distinct().Count() != request.Rcm.Inputs.Count)
            return OtmrUploadValidationResult.Invalid("INVALID_RCM_INPUTS", "RCM inputs must have matching session IDs and unique non-empty identities.");

        var inputIds = request.Rcm.Inputs.Select(x => x.InputGuid).ToHashSet();
        if (request.Rcm.Captures.Any(x => x is null || x.SessionId != sessionId || x.CaptureId == Guid.Empty || !inputIds.Contains(x.InputGuid)) ||
            request.Rcm.Captures.Select(x => x.CaptureId).Distinct().Count() != request.Rcm.Captures.Count)
            return OtmrUploadValidationResult.Invalid("INVALID_RCM_CAPTURES", "RCM captures must reference this session and a declared input using unique capture identities.");

        var captureIds = request.Rcm.Captures.Select(x => x.CaptureId).ToHashSet();
        if (request.Rcm.CaptureFrames.Any(x => x is null || !captureIds.Contains(x.CaptureId) || x.Sequence < 1 || !IsCompleteLiveFrame(x.Data)) ||
            request.Rcm.CaptureFrames.GroupBy(x => x.CaptureId).Any(g => g.Select(x => x.Sequence).Distinct().Count() != g.Count()))
            return OtmrUploadValidationResult.Invalid("INVALID_RCM_CAPTURE_FRAMES", "RCM capture frames must reference a declared capture, have unique positive sequences, and contain complete frames.");

        foreach (OtmrApiV1RcmCapture capture in request.Rcm.Captures)
        {
            long actualCount = request.Rcm.CaptureFrames.LongCount(x => x.CaptureId == capture.CaptureId);
            if (capture.FrameCount != actualCount || capture.NoOtmrData != (actualCount == 0))
                return OtmrUploadValidationResult.Invalid("RCM_CAPTURE_COUNT_MISMATCH", "An RCM capture frame count or noOtmrData flag is inconsistent with its frames.");
        }

        if (request.Rcm.Comparisons.Any(x => x is null || x.SessionId != sessionId || !inputIds.Contains(x.InputGuid)) ||
            request.Rcm.Comparisons.Select(x => x.InputGuid).Distinct().Count() != request.Rcm.Comparisons.Count)
            return OtmrUploadValidationResult.Invalid("INVALID_RCM_COMPARISONS", "RCM comparisons must reference this session and a declared input with one comparison per input.");

        string computedHash = OtmrApiV1Json.ComputeContentSha256(
            request.ApiVersion, request.Session, request.RawEntries, request.LiveFrames, request.Rcm);
        if (!IsSha256(manifest.ContentSha256) || !FixedTimeHexEquals(computedHash, manifest.ContentSha256))
            return OtmrUploadValidationResult.Invalid("MANIFEST_HASH_MISMATCH", "The submitted manifest hash does not match the canonical API v1 content hash.");

        return OtmrUploadValidationResult.Valid;
    }

    private static bool IsCompleteLiveFrame(byte[]? data) =>
        data is { Length: >= 3 } && data[0] == 0xFB && data[1] == 0xFB && data[^1] == 0xFF;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool FixedTimeHexEquals(string left, string right)
    {
        byte[] a = Convert.FromHexString(left);
        byte[] b = Convert.FromHexString(right);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
