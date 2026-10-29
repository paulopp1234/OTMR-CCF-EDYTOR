using CcfEditor.Otmr.Live;

namespace CcfEditor.Otmr.Rcm;

public enum RcmInputTestState
{
    Idle,
    WaitingFor24VApplied,
    Capturing24VApplied,
    WaitingForVoltageRemoved,
    CapturingVoltageRemoved,
    Comparing,
    Complete
}

public enum RcmInputTestFrameResult
{
    Ignored,
    Accepted,
    TriggeredCapture
}

public sealed class RcmInputTestCoordinator
{
    private readonly RcmCaptureWindowCoordinator _capture;
    private RcmPinProfile? _activePin;
    private RcmStateEvidence? _originalRemoved;
    private RcmStateEvidence? _originalApplied;
    private RcmStateComparison? _originalComparison;
    private string? _originalResult;
    private DateTimeOffset _startedAt;

    public RcmInputTestCoordinator(RcmCaptureWindowCoordinator capture) =>
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));

    public RcmInputTestState State { get; private set; }
    public RcmPinProfile? ActivePin => _activePin;
    public Guid? ActiveInputId => _activePin?.Id;
    public string? ActivePinKey => _activePin?.DisplayKey;
    public bool IsRunning => State is not RcmInputTestState.Idle and not RcmInputTestState.Complete;
    public bool IsWaiting => State is RcmInputTestState.WaitingFor24VApplied or RcmInputTestState.WaitingForVoltageRemoved;
    public bool IsCapturing => State is RcmInputTestState.Capturing24VApplied or RcmInputTestState.CapturingVoltageRemoved;

    public void Start(RcmPinProfile pin, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (IsRunning || _capture.IsCapturing)
            throw new InvalidOperationException("Another RCM input test is already active.");

        _originalRemoved = pin.VoltageRemoved;
        _originalApplied = pin.VoltageApplied24V;
        _originalComparison = pin.Comparison;
        _originalResult = pin.RcmResult;
        _capture.BeginArmed(pin, RcmElectricalTestState.VoltageApplied24V, startedAt);
        _startedAt = startedAt;
        _activePin = pin;
        State = RcmInputTestState.WaitingFor24VApplied;
    }

    public RcmInputTestFrameResult AddFrame(DateTimeOffset timestamp, OtmrLiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!IsWaiting && !IsCapturing)
            return RcmInputTestFrameResult.Ignored;

        bool wasWaiting = IsWaiting;
        if (!_capture.AddFrame(timestamp, frame))
            return RcmInputTestFrameResult.Ignored;

        if (!wasWaiting)
            return RcmInputTestFrameResult.Accepted;

        State = State == RcmInputTestState.WaitingFor24VApplied
            ? RcmInputTestState.Capturing24VApplied
            : RcmInputTestState.CapturingVoltageRemoved;
        return RcmInputTestFrameResult.TriggeredCapture;
    }

    public RcmInputTestState CompleteCapture(DateTimeOffset completedAt, int requiredVerificationRuns = 3)
    {
        if (_activePin is null || !IsCapturing)
            throw new InvalidOperationException("No post-event RCM capture window is active.");

        if (State == RcmInputTestState.Capturing24VApplied)
        {
            _capture.Stop(completedAt);
            _capture.BeginArmed(_activePin, RcmElectricalTestState.VoltageRemoved, completedAt);
            State = RcmInputTestState.WaitingForVoltageRemoved;
            return State;
        }

        _capture.Stop(completedAt);
        State = RcmInputTestState.Comparing;
        _capture.Compare(_activePin, completedAt);
        RcmMappingVerificationService.RecordCompletedRun(
            _activePin,
            _startedAt,
            completedAt,
            requiredVerificationRuns);
        // The raw comparison is retained for review, but no decoder semantics are
        // inferred. Completion means exactly that both genuine states were captured.
        _activePin.RcmResult = RcmResultStates.BothStatesCaptured;
        ClearSnapshot();
        State = RcmInputTestState.Complete;
        return State;
    }

    public bool Cancel()
    {
        if (!IsRunning || _activePin is null)
            return false;

        _capture.CancelActive();
        if (_originalRemoved is not null)
            _activePin.VoltageRemoved = _originalRemoved;
        if (_originalApplied is not null)
            _activePin.VoltageApplied24V = _originalApplied;
        if (_originalComparison is not null)
            _activePin.Comparison = _originalComparison;
        if (_originalResult is not null)
            _activePin.RcmResult = _originalResult;

        _activePin = null;
        ClearSnapshot();
        State = RcmInputTestState.Idle;
        return true;
    }

    private void ClearSnapshot()
    {
        _originalRemoved = null;
        _originalApplied = null;
        _originalComparison = null;
        _originalResult = null;
    }
}
