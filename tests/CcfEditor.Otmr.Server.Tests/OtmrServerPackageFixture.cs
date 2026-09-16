using CcfEditor.Otmr.Sync;

namespace CcfEditor.Otmr.Server.Tests;

internal static class OtmrServerPackageFixture
{
    public static OtmrApiV1UploadRequest Create(Guid? specifiedSessionId = null)
    {
        Guid sessionId = specifiedSessionId ?? Guid.NewGuid();
        Guid inputId = Guid.NewGuid();
        Guid captureId = Guid.NewGuid();
        DateTimeOffset started = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
        var session = new OtmrApiV1Session(sessionId, started, started.AddMinutes(5), started.AddSeconds(-1),
            "1.2.3", "COM2", "38400/8/N/1", "171001", "Class 171", "171.ccf", "ccf-hash",
            "171.rcm.json", "rcm-hash", "{\"profileVersion\":1}", "preserved notes");
        OtmrApiV1RawEntry[] raw =
        [
            new(sessionId, 1, started.AddMilliseconds(1), "TX", [0x01, 0x01], "Query01"),
            new(sessionId, 2, started.AddMilliseconds(2), "RX", [0x00, 0xFF, 0x7E], null)
        ];
        OtmrApiV1LiveFrame[] live =
        [
            new(sessionId, 1, started.AddSeconds(1), [0xFB, 0xFB, 0x10, 0x11, 0xFF], "RAW_NOT_DECODED", null)
        ];
        OtmrApiV1RcmInput[] inputs =
        [
            new(sessionId, inputId, "J1", "A", "Throttle 1", "Input", "MIO1", "1", "Return", true,
                "Safety", "input notes", 0, 12, 0, 0, "R0", "R12", 1, "Pair", "BOTH_STATES_CAPTURED")
        ];
        OtmrApiV1RcmCapture[] captures =
        [
            new(captureId, sessionId, inputId, "VoltageApplied24V", started.AddSeconds(2), started.AddSeconds(4), 1, false, "pos=4", started.AddSeconds(2))
        ];
        OtmrApiV1RcmCaptureFrame[] captureFrames =
        [
            new(captureId, 1, started.AddSeconds(2), [0xFB, 0xFB, 0x20, 0xFF])
        ];
        OtmrApiV1RcmComparison[] comparisons =
        [
            new(sessionId, inputId, started.AddSeconds(5), ["common"], ["removed"], ["applied"], ["repeat"], ["bit 0"], true, "BOTH_STATES_CAPTURED")
        ];
        var rcm = new OtmrApiV1RcmPayload(inputs, captures, captureFrames, comparisons);
        string hash = OtmrApiV1Json.ComputeContentSha256(OtmrApiContract.Version, session, raw, live, rcm);
        var manifest = new OtmrApiV1Manifest(raw.Length, live.Length, inputs.Length, captures.Length, captureFrames.Length, comparisons.Length, hash);
        return new(OtmrApiContract.Version, session, raw, live, rcm, manifest);
    }

    public static OtmrApiV1UploadRequest WithDifferentEvidence(OtmrApiV1UploadRequest request)
    {
        OtmrApiV1Session changedSession = request.Session with { Notes = "different evidence" };
        string hash = OtmrApiV1Json.ComputeContentSha256(request.ApiVersion, changedSession, request.RawEntries, request.LiveFrames, request.Rcm);
        return request with { Session = changedSession, Manifest = request.Manifest with { ContentSha256 = hash } };
    }
}
