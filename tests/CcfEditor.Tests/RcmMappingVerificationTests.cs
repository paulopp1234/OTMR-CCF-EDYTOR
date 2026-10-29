using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.Tests;

public sealed class RcmMappingVerificationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OneRunCannotVerifyAndTwoMatchingRunsRemainCandidateOnly()
    {
        RcmPinProfile pin = Pin();
        CompleteRun(pin, 1, removed: 0x0C, applied: 0x0D);

        Assert.Equal(RcmVerificationStates.CandidateFound, pin.DecoderVerification.Status);
        Assert.Equal(1, pin.DecoderVerification.SuccessfulRepetitionCount);
        Assert.False(pin.Comparison.DecoderVerified);
        Assert.Throws<InvalidOperationException>(() =>
            RcmMappingVerificationService.VerifyMapping(pin, Now.AddMinutes(1)));

        CompleteRun(pin, 2, removed: 0x0C, applied: 0x0D);
        Assert.Equal(RcmVerificationStates.CandidateFound, pin.DecoderVerification.Status);
        Assert.Equal(2, pin.DecoderVerification.SuccessfulRepetitionCount);
        Assert.False(pin.Comparison.DecoderVerified);
    }

    [Fact]
    public void ThreeMatchingRunsBecomeEligibleButRequireExplicitOperatorVerification()
    {
        RcmPinProfile pin = Pin();
        CompleteRun(pin, 1, 0x0C, 0x0D);
        CompleteRun(pin, 2, 0x0C, 0x0D);
        CompleteRun(pin, 3, 0x0C, 0x0D);

        Assert.Equal(RcmVerificationStates.Eligible, pin.DecoderVerification.Status);
        Assert.Equal(3, pin.DecoderVerification.SuccessfulRepetitionCount);
        Assert.False(pin.Comparison.DecoderVerified);
        RcmObservedTransition candidate = Assert.IsType<RcmObservedTransition>(pin.DecoderVerification.ObservedMapping);
        Assert.Equal(3, candidate.RawPosition);
        Assert.Equal(0, candidate.Bit);
        Assert.Equal("bit 0: removed 0 -> applied 1", candidate.TransitionPolarity);

        DateTimeOffset verifiedAt = Now.AddHours(1);
        RcmMappingVerificationService.VerifyMapping(pin, verifiedAt);

        Assert.True(pin.Comparison.DecoderVerified);
        Assert.Equal(RcmVerificationStates.Verified, pin.DecoderVerification.Status);
        Assert.Equal(verifiedAt, pin.DecoderVerification.VerifiedAt);
        Assert.Equal("physical stimulation", pin.DecoderVerification.VerificationMethod);
        Assert.Equal("J1", pin.DecoderVerification.Connector);
        Assert.Equal("A", pin.DecoderVerification.Pin);
        Assert.Equal("Throttle 1", pin.DecoderVerification.Function);
        Assert.Equal(0, pin.DecoderVerification.ExpectedCcf.LogicalCard);
        Assert.Equal(0, pin.DecoderVerification.ExpectedCcf.LogicalChannel);
        Assert.Equal(0, pin.DecoderVerification.ExpectedCcf.RecordA);
        Assert.Equal(12, pin.DecoderVerification.ExpectedCcf.RecordB);
        Assert.Equal(3, pin.DecoderVerification.QualifyingRunIds.Count);
        Assert.All(pin.VerificationRuns, run => Assert.Contains(run.RunId, pin.DecoderVerification.QualifyingRunIds));
    }

    [Fact]
    public void ContradictoryRunPreventsEligibility()
    {
        RcmPinProfile pin = Pin();
        CompleteRun(pin, 1, 0x0C, 0x0D);
        CompleteRun(pin, 2, 0x0C, 0x0D);
        CompleteRun(pin, 3, 0x0C, 0x0E);

        Assert.Equal(RcmVerificationStates.CandidateFound, pin.DecoderVerification.Status);
        Assert.Equal(2, pin.DecoderVerification.SuccessfulRepetitionCount);
        Assert.False(pin.Comparison.DecoderVerified);
    }

    [Fact]
    public async Task VerificationRunHistoryAndMetadataPersistAndReload()
    {
        RcmPinProfile pin = Pin();
        CompleteRun(pin, 1, 0x0C, 0x0D);
        CompleteRun(pin, 2, 0x0C, 0x0D);
        CompleteRun(pin, 3, 0x0C, 0x0D);
        RcmMappingVerificationService.VerifyMapping(pin, Now.AddHours(1));
        var profile = new RcmProfile
        {
            SourceCcfFilename = "Class171.ccf",
            SourceCcfSha256 = new string('A', 64),
            SourceCcfSize = 26600,
            CreationTimestamp = Now,
            LastModifiedTimestamp = Now,
            Connectors = new List<RcmConnector> { new() { Name = "J1" } },
            Pins = new List<RcmPinProfile> { pin }
        };
        string path = Path.Combine(Path.GetTempPath(), $"rcm-verification-{Guid.NewGuid():N}.json");
        try
        {
            await RcmProfileJson.SaveAsync(path, profile, Now.AddHours(2));
            RcmPinProfile restored = (await RcmProfileJson.LoadAsync(path)).Pins.Single();

            Assert.Equal(RcmProfile.CurrentSchemaVersion, (await RcmProfileJson.LoadAsync(path)).SchemaVersion);
            Assert.Equal(3, restored.VerificationRuns.Count);
            Assert.All(restored.VerificationRuns, run =>
            {
                Assert.NotEqual(Guid.Empty, run.RunId);
                Assert.NotEmpty(run.VoltageApplied24V.CompleteRawFrames);
                Assert.NotEmpty(run.VoltageRemoved.CompleteRawFrames);
                Assert.NotEmpty(run.CandidateTransitions);
            });
            Assert.True(restored.Comparison.DecoderVerified);
            Assert.Equal(RcmVerificationStates.Verified, restored.DecoderVerification.Status);
            Assert.Equal("physical stimulation", restored.DecoderVerification.VerificationMethod);
            Assert.Equal(3, restored.DecoderVerification.QualifyingRunIds.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MatchingLaterRunKeepsVerificationWhileContradictionCreatesConflict()
    {
        RcmPinProfile pin = Pin();
        CompleteRun(pin, 1, 0x0C, 0x0D);
        CompleteRun(pin, 2, 0x0C, 0x0D);
        CompleteRun(pin, 3, 0x0C, 0x0D);
        RcmMappingVerificationService.VerifyMapping(pin, Now.AddHours(1));

        CompleteRun(pin, 4, 0x0C, 0x0D);
        Assert.Equal(RcmVerificationStates.Verified, pin.DecoderVerification.Status);
        Assert.True(pin.Comparison.DecoderVerified);
        Assert.Equal(4, pin.DecoderVerification.SuccessfulRepetitionCount);

        CompleteRun(pin, 5, 0x0C, 0x0E);
        Assert.Equal(RcmVerificationStates.Conflict, pin.DecoderVerification.Status);
        Assert.False(pin.Comparison.DecoderVerified);
        Assert.NotNull(pin.DecoderVerification.VerifiedAt);
        Assert.NotNull(pin.DecoderVerification.ObservedMapping);
        Assert.Contains(pin.VerificationRuns[^1].RunId, pin.DecoderVerification.ContradictoryRunIds);
        Assert.Contains(pin.DecoderVerification.AuditHistory,
            audit => audit.Action == "VERIFICATION_CONFLICT");
    }

    [Fact]
    public void ResetClearsQualifyingRunsButPreservesAuditTrail()
    {
        RcmPinProfile pin = Pin();
        CompleteRun(pin, 1, 0x0C, 0x0D);
        CompleteRun(pin, 2, 0x0C, 0x0D);
        CompleteRun(pin, 3, 0x0C, 0x0D);
        RcmMappingVerificationService.VerifyMapping(pin, Now.AddHours(1));

        RcmMappingVerificationService.ResetVerification(pin, Now.AddHours(2));

        Assert.Empty(pin.VerificationRuns);
        Assert.Equal(RcmVerificationStates.NotVerified, pin.DecoderVerification.Status);
        Assert.False(pin.Comparison.DecoderVerified);
        Assert.Contains(pin.DecoderVerification.AuditHistory, audit => audit.Action == "VERIFY_MAPPING");
        Assert.Contains(pin.DecoderVerification.AuditHistory, audit => audit.Action == "RESET_VERIFICATION");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void VerificationEligibilityRequiresExpectedCcfAndTestable(bool missingCcf, bool notTestable)
    {
        RcmPinProfile pin = Pin();
        if (missingCcf) pin.CcfReference = null;
        if (notTestable) pin.Testable = false;

        if (notTestable)
        {
            Assert.Throws<InvalidOperationException>(() => CompleteRun(pin, 1, 0x0C, 0x0D));
            return;
        }

        CompleteRun(pin, 1, 0x0C, 0x0D);
        CompleteRun(pin, 2, 0x0C, 0x0D);
        CompleteRun(pin, 3, 0x0C, 0x0D);
        Assert.NotEqual(RcmVerificationStates.Eligible, pin.DecoderVerification.Status);
    }

    private static void CompleteRun(RcmPinProfile pin, int runNumber, byte removed, byte applied)
    {
        DateTimeOffset start = Now.AddMinutes(runNumber * 2);
        var coordinator = new RcmInputTestCoordinator(new RcmCaptureWindowCoordinator());
        coordinator.Start(pin, start);
        coordinator.AddFrame(start.AddSeconds(1), Frame(applied));
        coordinator.CompleteCapture(start.AddSeconds(3));
        coordinator.AddFrame(start.AddSeconds(4), Frame(removed));
        coordinator.CompleteCapture(start.AddSeconds(6), requiredVerificationRuns: 3);
    }

    private static RcmPinProfile Pin() => new()
    {
        Connector = "J1",
        Pin = "A",
        Function = "Throttle 1",
        Testable = true,
        CcfReference = new RcmCcfReference
        {
            LogicalCard = 0,
            LogicalChannel = 0,
            RecordA = 0,
            RecordB = 12
        }
    };

    private static OtmrLiveFrame Frame(byte value) =>
        Assert.Single(new OtmrLiveFrameAssembler().Append(
            new byte[] { 0xFB, 0xFB, 0x38, value, 0xFF }));
}
