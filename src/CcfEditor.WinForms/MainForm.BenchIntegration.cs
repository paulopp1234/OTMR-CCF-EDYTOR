using CcfEditor.Core;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;
using CcfEditor.Otmr.Sync;

namespace CcfEditor.WinForms;

public partial class MainForm
{
    private long _realtimeFramesReceived;
    private long _realtimeFramesDecoded;
    private string? _realtimeSourceConnectionId;
    private OtmrRealtimeDecodedState? _pendingRealtimeDecodedState;

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
        if (state is OtmrLiveState.WaitingForLiveFrames or
            OtmrLiveState.ConnectedIdle or
            OtmrLiveState.NotLive or
            OtmrLiveState.Disconnected or
            OtmrLiveState.Error)
        {
            _realtimeSourceConnectionId = null;
            _pendingRealtimeDecodedState = null;
            otmrRcmLiveControl.SetSourceConnectionId(null);
        }
        otmrBenchControl.SetOtmrLiveState(state);
        otmrRcmLiveControl.SetOtmrLiveState(state);
        ReportApplicationPresenceContext(state);
    }

    private void ReportApplicationPresenceContext(OtmrLiveState? state = null)
    {
        OtmrLiveState currentState = state ?? otmrLiveControl.State;
        bool genuineLiveConnected = !string.IsNullOrWhiteSpace(_realtimeSourceConnectionId) &&
            currentState is OtmrLiveState.LiveReady or OtmrLiveState.LiveActive;
        otmrServerSyncControl.ReportApplicationPresenceContext(new(
            genuineLiveConnected,
            ReadRealtimeVehicleIdentifierFromCcf(),
            genuineLiveConnected ? _realtimeSourceConnectionId : null));
    }

    private void OtmrLiveControl_GenuineLiveSessionStarted(
        object? sender,
        OtmrLiveSessionStartedEventArgs e)
    {
        _realtimeSourceConnectionId = e.SourceConnectionId;
        otmrRcmLiveControl.SetSourceConnectionId(e.SourceConnectionId);
        ReportApplicationPresenceContext();
        (string? profileFilename, string? profileSha256) =
            otmrRcmLiveControl.GetActiveProfileIdentity();
        otmrServerSyncControl.StartRealtimeLiveSession(new OtmrRealtimeSessionStart(
            ReadRealtimeVehicleIdentifierFromCcf(),
            e.StartedUtc,
            e.SourceConnectionId,
            profileFilename,
            profileSha256));

        if (_pendingRealtimeDecodedState is not null)
        {
            otmrServerSyncControl.PublishVerifiedLiveState(
                _pendingRealtimeDecodedState with { SourceConnectionId = e.SourceConnectionId });
            _pendingRealtimeDecodedState = null;
        }
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
                e.Frame,
                e.Signals);
        }

        string? vehicleIdentifier = ReadRealtimeVehicleIdentifierFromCcf();
        int decodedStateCount = e.Signals.Count(signal =>
            signal.State is RcmDecodedElectricalState.Active or RcmDecodedElectricalState.Inactive);
        var diagnostics = new OtmrRealtimeSourceDiagnostics(
            _realtimeFramesReceived,
            _realtimeFramesDecoded,
            e.ProfileFilename,
            otmrRcmLiveControl.GetVerifiedMappingCount(),
            vehicleIdentifier,
            decodedStateCount);
        otmrServerSyncControl.ReportRealtimeSourceDiagnostics(diagnostics);

        var realtimeState = new OtmrRealtimeDecodedState(
            vehicleIdentifier,
            e.TimestampUtc,
            _realtimeSourceConnectionId,
            e.ProfileFilename,
            e.ProfileSha256,
            e.Signals);
        if (string.IsNullOrWhiteSpace(_realtimeSourceConnectionId))
        {
            _pendingRealtimeDecodedState = otmrLiveControl.State is
                OtmrLiveState.WaitingForLiveFrames or OtmrLiveState.LiveReady or OtmrLiveState.LiveActive
                ? realtimeState
                : null;
        }
        else
            otmrServerSyncControl.PublishVerifiedLiveState(realtimeState);
    }

    private void OtmrBenchControl_CurrentRcmProfileChanged(
        object? sender,
        CurrentRcmProfileChangedEventArgs e) =>
        otmrRcmLiveControl.SetActiveRcmProfile(
            e.IsLoadedJson ? e.Profile : null,
            e.IsLoadedJson ? e.ProfilePath : null);
}
