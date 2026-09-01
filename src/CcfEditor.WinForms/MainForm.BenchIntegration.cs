using CcfEditor.Core;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;
using CcfEditor.Otmr.Sync;

namespace CcfEditor.WinForms;

public partial class MainForm
{
    private long _realtimeFramesReceived;
    private long _realtimeFramesDecoded;

    internal CcfDocument? GetCurrentCcfForBench() => _document;

    private void OtmrLiveControl_GenuineLiveFrameReceived(object? sender, OtmrLiveFrameEventArgs e)
    {
        _realtimeFramesReceived++;
        ReportOtmrLiveFrame(e.Timestamp, e.Frame, e.CompletingCaptureEntry);
    }

    internal void ReportOtmrLiveFrame(
        DateTimeOffset timestamp,
        OtmrLiveFrame frame,
        CcfEditor.Otmr.Capture.OtmrCaptureEntry? completingCaptureEntry = null)
    {
        otmrBenchControl.ReportRawLiveFrame(timestamp, frame);
        otmrRcmLiveControl.ReportRawLiveFrame(timestamp, frame, completingCaptureEntry);
    }

    internal void ReportOtmrLiveState(OtmrLiveState state)
    {
        otmrBenchControl.SetOtmrLiveState(state);
        otmrRcmLiveControl.SetOtmrLiveState(state);
    }

    private void OtmrRcmLiveControl_VerifiedLiveStateDecoded(
        object? sender,
        VerifiedLiveStateDecodedEventArgs e)
    {
        _realtimeFramesDecoded++;
        if (e.CompletingCaptureEntry is not null)
        {
            otmrLiveControl.ApplyVerifiedLiveInterpretation(
                e.CompletingCaptureEntry,
                e.ProfileSha256 ?? e.ProfileFilename,
                e.Signals);
        }

        string? vehicleIdentifier = ReadRealtimeVehicleIdentifierFromCcf();
        int decodedStateCount = e.Signals.Count(signal =>
            signal.State is RcmDecodedElectricalState.Active or RcmDecodedElectricalState.Inactive);
        var diagnostics = new OtmrRealtimeSourceDiagnostics(
            _realtimeFramesReceived,
            _realtimeFramesDecoded,
            e.ProfileFilename,
            e.Signals.Count,
            vehicleIdentifier,
            decodedStateCount);
        otmrServerSyncControl.ReportRealtimeSourceDiagnostics(diagnostics);

        string? sourceSessionId = _recordingStore?.GetStatus().SessionId?.ToString("D");
        otmrServerSyncControl.PublishVerifiedLiveState(new OtmrRealtimeDecodedState(
            vehicleIdentifier,
            e.TimestampUtc,
            sourceSessionId,
            e.ProfileFilename,
            e.ProfileSha256,
            e.Signals));
    }

    private void OtmrBenchControl_CurrentRcmProfileChanged(
        object? sender,
        CurrentRcmProfileChangedEventArgs e) =>
        otmrRcmLiveControl.SetActiveRcmProfile(
            e.IsLoadedJson ? e.Profile : null,
            e.IsLoadedJson ? e.ProfilePath : null);
}
