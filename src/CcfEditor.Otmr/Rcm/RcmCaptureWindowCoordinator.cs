using CcfEditor.Otmr.Live;

namespace CcfEditor.Otmr.Rcm;

public enum RcmCapturePhase
{
    Idle,
    ArmedWaitingForFirstFrame,
    CapturingAfterFirstFrame
}

public sealed class RcmCaptureWindowCoordinator
{
    private RcmPinProfile? _activePin;
    private RcmElectricalTestState? _activeState;

    public bool IsCapturing => _activePin is not null;
    public bool IsArmed => Phase == RcmCapturePhase.ArmedWaitingForFirstFrame;
    public bool IsCollecting => Phase == RcmCapturePhase.CapturingAfterFirstFrame;
    public RcmCapturePhase Phase { get; private set; }
    public Guid? ActiveInputId => _activePin?.Id;
    public string? ActivePinKey => _activePin?.DisplayKey;
    public RcmElectricalTestState? ActiveState => _activeState;

    public event EventHandler<RcmCaptureCompletedEventArgs>? CaptureCompleted;
    public event EventHandler<RcmComparisonCompletedEventArgs>? ComparisonCompleted;

    public void BeginArmed(
        RcmPinProfile pin,
        RcmElectricalTestState state,
        DateTimeOffset armedAt)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (IsCapturing)
            throw new InvalidOperationException("Another RCM capture window is already active.");
        if (!pin.PhysicalMappingAssigned)
            throw new InvalidOperationException("Assign both Connector and Pin before voltage capture.");
        if (!pin.Testable)
            throw new InvalidOperationException($"{pin.DisplayKey} is not a voltage-testable input.");

        _activePin = pin;
        _activeState = state;
        Phase = RcmCapturePhase.ArmedWaitingForFirstFrame;
        ArmedAt = armedAt;
    }

    // Kept as a source-compatible alias for callers that do not need to name the
    // two phases. It arms; it does not start the collection window.
    public void Begin(RcmPinProfile pin, RcmElectricalTestState state, DateTimeOffset armedAt) =>
        BeginArmed(pin, state, armedAt);

    public DateTimeOffset? ArmedAt { get; private set; }

    public bool AddFrame(DateTimeOffset timestamp, OtmrLiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_activePin is null || _activeState is null)
            return false;

        RcmStateEvidence evidence;
        if (Phase == RcmCapturePhase.ArmedWaitingForFirstFrame)
        {
            // Evidence is replaced only when a genuine complete frame arrives.
            // Merely arming or timing out must not create test evidence.
            evidence = new RcmStateEvidence { CaptureStart = timestamp };
            SetEvidence(_activePin, _activeState.Value, evidence);
            _activePin.Comparison = new RcmStateComparison();
            _activePin.RcmResult = ResultForCapturedStates(_activePin);
            Phase = RcmCapturePhase.CapturingAfterFirstFrame;
        }
        else if (Phase == RcmCapturePhase.CapturingAfterFirstFrame)
        {
            evidence = GetEvidence(_activePin, _activeState.Value);
        }
        else
        {
            return false;
        }

        byte[] bytes = frame.GetDataSnapshot();
        evidence.CompleteRawFrames.Add(new RcmRawFrameEvidence
        {
            SequenceNumber = evidence.CompleteRawFrames.Count + 1,
            Timestamp = timestamp,
            RawFrameHex = frame.Hex,
            RawFrameBytes = bytes.Select(value => (int)value).ToList()
        });
        return true;
    }

    public RcmStateEvidence Stop(DateTimeOffset captureStop)
    {
        if (_activePin is null || _activeState is null)
            throw new InvalidOperationException("No RCM capture window is active.");
        if (Phase != RcmCapturePhase.CapturingAfterFirstFrame)
            throw new InvalidOperationException("RCM capture is armed but no complete OTMR live frame has been received.");

        RcmPinProfile pin = _activePin;
        RcmElectricalTestState state = _activeState.Value;
        RcmStateEvidence evidence = GetEvidence(pin, state);
        if (captureStop < evidence.CaptureStart)
            throw new ArgumentOutOfRangeException(nameof(captureStop));

        evidence.CaptureStop = captureStop;
        evidence.Tested = evidence.FrameCount > 0;
        evidence.NoOtmrData = evidence.FrameCount == 0;
        RcmRawEvidenceAnalyzer.Analyze(evidence);
        pin.Comparison = new RcmStateComparison();
        pin.RcmResult = ResultForCapturedStates(pin);
        _activePin = null;
        _activeState = null;
        Phase = RcmCapturePhase.Idle;
        ArmedAt = null;

        CaptureCompleted?.Invoke(this, new RcmCaptureCompletedEventArgs(pin, state, evidence));
        return evidence;
    }

    public bool CancelArmed()
    {
        if (Phase != RcmCapturePhase.ArmedWaitingForFirstFrame)
            return false;

        _activePin = null;
        _activeState = null;
        Phase = RcmCapturePhase.Idle;
        ArmedAt = null;
        return true;
    }

    public void Compare(RcmPinProfile pin, DateTimeOffset comparedAt)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (IsCapturing)
            throw new InvalidOperationException("Stop the active capture window before comparing states.");
        RcmStateComparer.Compare(pin, comparedAt);
        ComparisonCompleted?.Invoke(this, new RcmComparisonCompletedEventArgs(pin));
    }

    public void Reset(RcmPinProfile pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (ActiveInputId == pin.Id)
            throw new InvalidOperationException("Stop the active capture window before resetting this input.");

        pin.VoltageRemoved = new RcmStateEvidence();
        pin.VoltageApplied24V = new RcmStateEvidence();
        pin.Comparison = new RcmStateComparison();
        pin.RcmResult = ResultForCapturedStates(pin);
    }

    public static string ResultForCapturedStates(RcmPinProfile pin)
    {
        if (!pin.PhysicalMappingAssigned)
            return RcmResultStates.Unassigned;
        if (!pin.Testable)
            return RcmResultStates.NotTestable;
        if (pin.VoltageRemoved.Tested && pin.VoltageApplied24V.Tested)
            return RcmResultStates.BothStatesCaptured;
        if (pin.VoltageRemoved.Tested)
            return RcmResultStates.VoltageRemovedCaptured;
        if (pin.VoltageApplied24V.Tested)
            return RcmResultStates.VoltageApplied24VCaptured;
        return RcmResultStates.NotTested;
    }

    private static RcmStateEvidence GetEvidence(RcmPinProfile pin, RcmElectricalTestState state) =>
        state == RcmElectricalTestState.VoltageRemoved
            ? pin.VoltageRemoved
            : pin.VoltageApplied24V;

    private static void SetEvidence(RcmPinProfile pin, RcmElectricalTestState state, RcmStateEvidence evidence)
    {
        if (state == RcmElectricalTestState.VoltageRemoved)
            pin.VoltageRemoved = evidence;
        else
            pin.VoltageApplied24V = evidence;
    }
}

public sealed class RcmCaptureCompletedEventArgs : EventArgs
{
    public RcmCaptureCompletedEventArgs(
        RcmPinProfile pin,
        RcmElectricalTestState state,
        RcmStateEvidence evidence)
    {
        Pin = pin ?? throw new ArgumentNullException(nameof(pin));
        State = state;
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
    }

    public RcmPinProfile Pin { get; }
    public RcmElectricalTestState State { get; }
    public RcmStateEvidence Evidence { get; }
}

public sealed class RcmComparisonCompletedEventArgs : EventArgs
{
    public RcmComparisonCompletedEventArgs(RcmPinProfile pin) =>
        Pin = pin ?? throw new ArgumentNullException(nameof(pin));

    public RcmPinProfile Pin { get; }
}
