using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.Tests;

public sealed class RcmInputTestCoordinatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FullSequenceSeparatesStatesRetainsBothTriggersAndComparesAutomatically()
    {
        RcmPinProfile pin = Pin();
        var capture = new RcmCaptureWindowCoordinator();
        var test = new RcmInputTestCoordinator(capture);

        test.Start(pin, Now);
        Assert.Equal(RcmInputTestState.WaitingFor24VApplied, test.State);
        Assert.Equal(RcmInputTestFrameResult.TriggeredCapture,
            test.AddFrame(Now.AddSeconds(1), Frame(0xA1)));
        Assert.Equal(RcmInputTestState.Capturing24VApplied, test.State);
        Assert.Equal(RcmInputTestFrameResult.Accepted,
            test.AddFrame(Now.AddSeconds(1.5), Frame(0xA2)));

        Assert.Equal(RcmInputTestState.WaitingForVoltageRemoved,
            test.CompleteCapture(Now.AddSeconds(3)));
        Assert.Equal(2, pin.VoltageApplied24V.FrameCount);
        Assert.Empty(pin.VoltageRemoved.CompleteRawFrames);

        Assert.Equal(RcmInputTestFrameResult.TriggeredCapture,
            test.AddFrame(Now.AddSeconds(4), Frame(0xB1)));
        Assert.Equal(RcmInputTestState.CapturingVoltageRemoved, test.State);
        Assert.Equal(RcmInputTestFrameResult.Accepted,
            test.AddFrame(Now.AddSeconds(4.5), Frame(0xB2)));

        Assert.Equal(RcmInputTestState.Complete, test.CompleteCapture(Now.AddSeconds(6)));
        Assert.Equal(new[] { 0xA1, 0xA2 }, Values(pin.VoltageApplied24V));
        Assert.Equal(new[] { 0xB1, 0xB2 }, Values(pin.VoltageRemoved));
        Assert.NotNull(pin.Comparison.ComparedAt);
        Assert.Equal(RcmResultStates.BothStatesCaptured, pin.RcmResult);
    }

    [Fact]
    public void FramesBeforeStartAreIgnored()
    {
        RcmPinProfile pin = Pin();
        var test = Coordinator();

        Assert.Equal(RcmInputTestFrameResult.Ignored, test.AddFrame(Now, Frame(0x10)));

        Assert.Empty(pin.VoltageApplied24V.CompleteRawFrames);
        Assert.Empty(pin.VoltageRemoved.CompleteRawFrames);
        Assert.Equal(RcmInputTestState.Idle, test.State);
    }

    [Fact]
    public void WaitingWithoutEventDoesNotCreateEvidenceOrEnterCapturing()
    {
        RcmPinProfile pin = Pin();
        var test = Coordinator();

        test.Start(pin, Now);

        Assert.True(test.IsWaiting);
        Assert.False(test.IsCapturing);
        Assert.Null(pin.VoltageApplied24V.CaptureStart);
        Assert.False(pin.VoltageApplied24V.Tested);
        Assert.Empty(pin.VoltageApplied24V.CompleteRawFrames);
    }

    [Fact]
    public void AppliedFrameCannotBecomeRemovedEvidence()
    {
        RcmPinProfile pin = Pin();
        var test = Coordinator();
        test.Start(pin, Now);

        test.AddFrame(Now.AddSeconds(1), Frame(0xA1));
        test.CompleteCapture(Now.AddSeconds(3));

        Assert.Equal(new[] { 0xA1 }, Values(pin.VoltageApplied24V));
        Assert.Empty(pin.VoltageRemoved.CompleteRawFrames);
    }

    [Fact]
    public void RemovedFrameCannotBecomeAppliedEvidence()
    {
        RcmPinProfile pin = Pin();
        var test = Coordinator();
        test.Start(pin, Now);
        test.AddFrame(Now.AddSeconds(1), Frame(0xA1));
        test.CompleteCapture(Now.AddSeconds(3));

        test.AddFrame(Now.AddSeconds(4), Frame(0xB1));

        Assert.Equal(new[] { 0xA1 }, Values(pin.VoltageApplied24V));
        Assert.Equal(new[] { 0xB1 }, Values(pin.VoltageRemoved));
    }

    [Fact]
    public void CancelWhileWaitingCreatesNoEvidence()
    {
        RcmPinProfile pin = Pin();
        RcmStateEvidence originalApplied = pin.VoltageApplied24V;
        RcmStateEvidence originalRemoved = pin.VoltageRemoved;
        var test = Coordinator();
        test.Start(pin, Now);

        Assert.True(test.Cancel());

        Assert.Same(originalApplied, pin.VoltageApplied24V);
        Assert.Same(originalRemoved, pin.VoltageRemoved);
        Assert.Equal(RcmInputTestState.Idle, test.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelDuringEitherCaptureStageRollsBackPartialTest(bool secondStage)
    {
        RcmPinProfile pin = Pin();
        RcmStateEvidence originalApplied = pin.VoltageApplied24V;
        RcmStateEvidence originalRemoved = pin.VoltageRemoved;
        RcmStateComparison originalComparison = pin.Comparison;
        string originalResult = pin.RcmResult;
        var test = Coordinator();
        test.Start(pin, Now);
        test.AddFrame(Now.AddSeconds(1), Frame(0xA1));
        if (secondStage)
        {
            test.CompleteCapture(Now.AddSeconds(3));
            test.AddFrame(Now.AddSeconds(4), Frame(0xB1));
        }

        Assert.True(test.Cancel());

        Assert.Same(originalApplied, pin.VoltageApplied24V);
        Assert.Same(originalRemoved, pin.VoltageRemoved);
        Assert.Same(originalComparison, pin.Comparison);
        Assert.Equal(originalResult, pin.RcmResult);
        Assert.Equal(RcmInputTestState.Idle, test.State);
    }

    [Fact]
    public void MappingTestableAndSingleInputGatesRemainMandatory()
    {
        var capture = new RcmCaptureWindowCoordinator();
        var test = new RcmInputTestCoordinator(capture);
        RcmPinProfile unassigned = Pin();
        unassigned.Connector = string.Empty;
        RcmPinProfile nonTestable = Pin();
        nonTestable.Testable = false;

        Assert.Throws<InvalidOperationException>(() => test.Start(unassigned, Now));
        Assert.Throws<InvalidOperationException>(() => test.Start(nonTestable, Now));
        test.Start(Pin(), Now);
        Assert.Throws<InvalidOperationException>(() => test.Start(Pin(), Now.AddSeconds(1)));
    }

    private static RcmInputTestCoordinator Coordinator() =>
        new(new RcmCaptureWindowCoordinator());

    private static RcmPinProfile Pin() => new()
    {
        Connector = "J1",
        Pin = "A",
        Function = "Throttle 1",
        Testable = true
    };

    private static OtmrLiveFrame Frame(byte value) =>
        Assert.Single(new OtmrLiveFrameAssembler().Append(
            new byte[] { 0xFB, 0xFB, 0x38, value, 0xFF }));

    private static int[] Values(RcmStateEvidence evidence) =>
        evidence.CompleteRawFrames.Select(frame => frame.RawFrameBytes[3]).ToArray();
}
