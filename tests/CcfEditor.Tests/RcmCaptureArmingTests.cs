using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.Tests;

public sealed class RcmCaptureArmingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ArmWithoutFramesRemainsArmedPastCollectionDurationAndCreatesNoEvidence()
    {
        RcmPinProfile pin = Pin();
        var coordinator = new RcmCaptureWindowCoordinator();

        coordinator.BeginArmed(pin, RcmElectricalTestState.VoltageRemoved, Now);

        Assert.True(coordinator.IsArmed);
        Assert.Equal(RcmCapturePhase.ArmedWaitingForFirstFrame, coordinator.Phase);
        Assert.Null(pin.VoltageRemoved.CaptureStart);
        Assert.False(pin.VoltageRemoved.Tested);
        Assert.False(pin.VoltageRemoved.NoOtmrData);
        Assert.Empty(pin.VoltageRemoved.CompleteRawFrames);
        Assert.Throws<InvalidOperationException>(() => coordinator.Stop(Now.AddSeconds(2)));
        Assert.True(coordinator.IsArmed);
    }

    [Fact]
    public void FirstFrameAfterArmIsRetainedAndStartsCollectionAtItsTimestamp()
    {
        RcmPinProfile pin = Pin();
        var coordinator = new RcmCaptureWindowCoordinator();
        DateTimeOffset firstAt = Now.AddSeconds(3);
        OtmrLiveFrame first = Frame(0x41);

        coordinator.BeginArmed(pin, RcmElectricalTestState.VoltageRemoved, Now);
        Assert.True(coordinator.AddFrame(firstAt, first));

        Assert.Equal(RcmCapturePhase.CapturingAfterFirstFrame, coordinator.Phase);
        Assert.True(coordinator.IsCollecting);
        Assert.Equal(firstAt, pin.VoltageRemoved.CaptureStart);
        RcmRawFrameEvidence retained = Assert.Single(pin.VoltageRemoved.CompleteRawFrames);
        Assert.Equal(first.GetDataSnapshot().Select(value => (int)value), retained.RawFrameBytes);
    }

    [Fact]
    public void AdditionalFramesDuringPostEventWindowAreRetainedInOrder()
    {
        RcmPinProfile pin = Pin();
        var coordinator = new RcmCaptureWindowCoordinator();
        coordinator.BeginArmed(pin, RcmElectricalTestState.VoltageRemoved, Now);

        Assert.True(coordinator.AddFrame(Now.AddSeconds(1), Frame(0x41)));
        Assert.True(coordinator.AddFrame(Now.AddSeconds(1.5), Frame(0x42)));
        RcmStateEvidence evidence = coordinator.Stop(Now.AddSeconds(3));

        Assert.True(evidence.Tested);
        Assert.False(evidence.NoOtmrData);
        Assert.Equal(2, evidence.FrameCount);
        Assert.Equal(new[] { 1, 2 }, evidence.CompleteRawFrames.Select(frame => frame.SequenceNumber));
    }

    [Fact]
    public void RemovedAndAppliedEvidenceRemainSeparate()
    {
        RcmPinProfile pin = Pin();
        var coordinator = new RcmCaptureWindowCoordinator();

        coordinator.BeginArmed(pin, RcmElectricalTestState.VoltageRemoved, Now);
        coordinator.AddFrame(Now.AddSeconds(1), Frame(0x10));
        coordinator.Stop(Now.AddSeconds(3));
        coordinator.BeginArmed(pin, RcmElectricalTestState.VoltageApplied24V, Now.AddSeconds(4));
        coordinator.AddFrame(Now.AddSeconds(5), Frame(0x20));
        coordinator.Stop(Now.AddSeconds(7));

        Assert.Equal(0x10, pin.VoltageRemoved.CompleteRawFrames.Single().RawFrameBytes[3]);
        Assert.Equal(0x20, pin.VoltageApplied24V.CompleteRawFrames.Single().RawFrameBytes[3]);
    }

    [Fact]
    public void FrameBeforeArmingIsNotUsed()
    {
        RcmPinProfile pin = Pin();
        var coordinator = new RcmCaptureWindowCoordinator();

        Assert.False(coordinator.AddFrame(Now, Frame(0x31)));
        coordinator.BeginArmed(pin, RcmElectricalTestState.VoltageRemoved, Now.AddSeconds(1));

        Assert.Empty(pin.VoltageRemoved.CompleteRawFrames);
        Assert.Null(pin.VoltageRemoved.CaptureStart);
    }

    [Fact]
    public void CancellingArmDoesNotReplaceExistingEvidenceOrCreateFakeEvidence()
    {
        RcmPinProfile pin = Pin();
        RcmStateEvidence original = pin.VoltageRemoved;
        var coordinator = new RcmCaptureWindowCoordinator();

        coordinator.BeginArmed(pin, RcmElectricalTestState.VoltageRemoved, Now);
        Assert.True(coordinator.CancelArmed());

        Assert.Same(original, pin.VoltageRemoved);
        Assert.False(pin.VoltageRemoved.Tested);
        Assert.False(pin.VoltageRemoved.NoOtmrData);
        Assert.False(coordinator.IsCapturing);
    }

    [Fact]
    public void BothStatesBecomeComparableOnlyAfterBothContainGenuineFrames()
    {
        RcmPinProfile pin = Pin();
        var coordinator = new RcmCaptureWindowCoordinator();
        coordinator.BeginArmed(pin, RcmElectricalTestState.VoltageRemoved, Now);
        coordinator.AddFrame(Now.AddSeconds(1), Frame(0x10));
        coordinator.Stop(Now.AddSeconds(3));

        Assert.Throws<InvalidOperationException>(() => coordinator.Compare(pin, Now.AddSeconds(4)));

        coordinator.BeginArmed(pin, RcmElectricalTestState.VoltageApplied24V, Now.AddSeconds(5));
        coordinator.AddFrame(Now.AddSeconds(6), Frame(0x20));
        coordinator.Stop(Now.AddSeconds(8));
        coordinator.Compare(pin, Now.AddSeconds(9));

        Assert.True(pin.VoltageRemoved.Tested);
        Assert.True(pin.VoltageApplied24V.Tested);
        Assert.NotNull(pin.Comparison.ComparedAt);
    }

    [Fact]
    public void ProfileMappingTestableAndSingleCaptureGatesRemainEnforced()
    {
        var coordinator = new RcmCaptureWindowCoordinator();
        RcmPinProfile unassigned = Pin(withPhysicalMapping: false);
        RcmPinProfile nonTestable = Pin();
        nonTestable.Testable = false;
        RcmPinProfile active = Pin();

        Assert.Throws<InvalidOperationException>(() =>
            coordinator.BeginArmed(unassigned, RcmElectricalTestState.VoltageRemoved, Now));
        Assert.Throws<InvalidOperationException>(() =>
            coordinator.BeginArmed(nonTestable, RcmElectricalTestState.VoltageRemoved, Now));
        coordinator.BeginArmed(active, RcmElectricalTestState.VoltageRemoved, Now);
        Assert.Throws<InvalidOperationException>(() =>
            coordinator.BeginArmed(Pin(), RcmElectricalTestState.VoltageApplied24V, Now));
    }

    private static RcmPinProfile Pin(bool withPhysicalMapping = true) => new()
    {
        Connector = withPhysicalMapping ? "J1" : string.Empty,
        Pin = withPhysicalMapping ? "A" : string.Empty,
        Function = "Bench input",
        Testable = true
    };

    private static OtmrLiveFrame Frame(byte value) =>
        Assert.Single(new OtmrLiveFrameAssembler().Append(new byte[] { 0xFB, 0xFB, 0x38, value, 0xFF }));
}
