using CcfEditor.Core;
using CcfEditor.Otmr.Bench;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.Tests;

public sealed class RcmProfileTests
{
    [Fact]
    public void CreateProfileFromCcfAndPinMapStoresSourceAndEveryPhysicalPin()
    {
        (CcfDocument document, IReadOnlyList<OtmrBenchPinDefinition> pins, byte[] source) = LoadInputs();
        var timestamp = new DateTimeOffset(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);

        RcmProfile profile = RcmProfileFactory.Create(document, pins, "Class 171", timestamp);

        Assert.Equal(RcmProfile.CurrentSchemaVersion, profile.SchemaVersion);
        Assert.Equal("Class 171", profile.VehicleType);
        Assert.Equal("CLASS171_GUI_TEST.ccf", profile.SourceCcfFilename);
        Assert.Equal(document.OriginalSha256, profile.SourceCcfSha256);
        Assert.Equal(source.Length, profile.SourceCcfSize);
        Assert.Equal(timestamp, profile.CreationTimestamp);
        Assert.Equal(pins.Count, profile.Pins.Count);
        Assert.Equal(source, File.ReadAllBytes(document.SourcePath!));
    }

    [Fact]
    public void TestableAndNonTestablePhysicalPinsAreRepresentedSafely()
    {
        RcmProfile profile = CreateProfile();

        RcmPinProfile j1a = profile.GetPin("J1", "A");
        Assert.True(j1a.Testable);
        Assert.Equal("Throttle 1", j1a.Function);
        Assert.Equal(0, j1a.CcfReference!.RecordA);
        Assert.Equal(12, j1a.CcfReference.RecordB);
        Assert.Equal(0, j1a.CcfReference.LogicalCard);
        Assert.Equal(0, j1a.CcfReference.LogicalChannel);
        Assert.Contains("OFF text:", j1a.CcfReference.RecordAValue, StringComparison.Ordinal);
        Assert.Equal(RcmResultStates.NotTested, j1a.RcmResult);

        RcmPinProfile j1l = profile.GetPin("J1", "L");
        Assert.False(j1l.Testable);
        Assert.Contains("do not apply +V", j1l.SafetyClassification, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RcmResultStates.NotTestable, j1l.RcmResult);
    }

    [Fact]
    public void VoltageRemovedCaptureUpdatesOnlyVoltageRemoved()
    {
        RcmPinProfile pin = CreateProfile().GetPin("J1", "A");
        var coordinator = new RcmCaptureWindowCoordinator();
        DateTimeOffset start = DateTimeOffset.UtcNow;

        coordinator.Begin(pin, RcmElectricalTestState.VoltageRemoved, start);
        Assert.True(coordinator.AddFrame(start.AddMilliseconds(20), Frame(0xFB, 0xFB, 0x38, 0x4A, 0xFF)));
        coordinator.Stop(start.AddSeconds(2));

        Assert.True(pin.VoltageRemoved.Tested);
        Assert.Single(pin.VoltageRemoved.CompleteRawFrames);
        Assert.False(pin.VoltageApplied24V.Tested);
        Assert.Empty(pin.VoltageApplied24V.CompleteRawFrames);
        Assert.Equal(RcmResultStates.VoltageRemovedCaptured, pin.RcmResult);
    }

    [Fact]
    public void VoltageAppliedCaptureUpdatesOnlyVoltageApplied24V()
    {
        RcmPinProfile pin = CreateProfile().GetPin("J1", "A");
        var coordinator = new RcmCaptureWindowCoordinator();
        DateTimeOffset start = DateTimeOffset.UtcNow;

        coordinator.Begin(pin, RcmElectricalTestState.VoltageApplied24V, start);
        coordinator.AddFrame(start.AddMilliseconds(20), Frame(0xFB, 0xFB, 0x38, 0x4B, 0xFF));
        coordinator.Stop(start.AddSeconds(2));

        Assert.True(pin.VoltageApplied24V.Tested);
        Assert.Single(pin.VoltageApplied24V.CompleteRawFrames);
        Assert.False(pin.VoltageRemoved.Tested);
        Assert.Empty(pin.VoltageRemoved.CompleteRawFrames);
        Assert.Equal(RcmResultStates.VoltageApplied24VCaptured, pin.RcmResult);
    }

    [Fact]
    public void FramesCannotEnterTheWrongOrAnInactiveStateWindow()
    {
        RcmPinProfile pin = CreateProfile().GetPin("J1", "A");
        var coordinator = new RcmCaptureWindowCoordinator();
        DateTimeOffset start = DateTimeOffset.UtcNow;

        Assert.False(coordinator.AddFrame(start, Frame(0xFB, 0xFB, 0x01, 0xFF)));
        coordinator.Begin(pin, RcmElectricalTestState.VoltageRemoved, start);
        Assert.Throws<InvalidOperationException>(() =>
            coordinator.Begin(pin, RcmElectricalTestState.VoltageApplied24V, start));
        coordinator.AddFrame(start.AddMilliseconds(1), Frame(0xFB, 0xFB, 0x02, 0xFF));
        coordinator.Stop(start.AddSeconds(2));

        Assert.Single(pin.VoltageRemoved.CompleteRawFrames);
        Assert.Empty(pin.VoltageApplied24V.CompleteRawFrames);
        Assert.False(coordinator.AddFrame(start.AddSeconds(3), Frame(0xFB, 0xFB, 0x03, 0xFF)));
    }

    [Fact]
    public void CompareRequiresBothStatesAndUsesFramePopulations()
    {
        RcmPinProfile pin = CreateProfile().GetPin("J1", "A");
        var coordinator = new RcmCaptureWindowCoordinator();
        DateTimeOffset start = DateTimeOffset.UtcNow;

        coordinator.Begin(pin, RcmElectricalTestState.VoltageRemoved, start);
        coordinator.AddFrame(start.AddMilliseconds(10), Frame(0xFB, 0xFB, 0x38, 0x4A, 0xFF));
        coordinator.AddFrame(start.AddMilliseconds(20), Frame(0xFB, 0xFB, 0x38, 0x4A, 0xFF));
        coordinator.Stop(start.AddSeconds(2));
        Assert.Throws<InvalidOperationException>(() => coordinator.Compare(pin, start.AddSeconds(3)));

        coordinator.Begin(pin, RcmElectricalTestState.VoltageApplied24V, start.AddSeconds(4));
        coordinator.AddFrame(start.AddSeconds(4.01), Frame(0xFB, 0xFB, 0x38, 0x4B, 0xFF));
        coordinator.AddFrame(start.AddSeconds(4.02), Frame(0xFB, 0xFB, 0x38, 0x4B, 0xFF));
        coordinator.Stop(start.AddSeconds(6));
        coordinator.Compare(pin, start.AddSeconds(7));

        Assert.Equal(RcmResultStates.RawDifferenceFound, pin.RcmResult);
        Assert.False(pin.Comparison.DecoderVerified);
        Assert.Contains(pin.Comparison.RepeatableDifferences, difference =>
            difference.Contains("POSITION[003]", StringComparison.Ordinal) &&
            difference.Contains("4A", StringComparison.Ordinal) &&
            difference.Contains("4B", StringComparison.Ordinal));
        Assert.All(pin.Comparison.CandidateTransitionEvidence, evidence =>
            Assert.Contains("no CCF semantic state is inferred", evidence, StringComparison.Ordinal));
    }

    [Fact]
    public void ResetOnlyClearsTheSelectedInput()
    {
        RcmProfile profile = CreateProfile();
        RcmPinProfile j1a = profile.GetPin("J1", "A");
        RcmPinProfile j1b = profile.GetPin("J1", "B");
        var coordinator = new RcmCaptureWindowCoordinator();
        CaptureOneWindow(coordinator, j1a, RcmElectricalTestState.VoltageRemoved, 0x4A);
        CaptureOneWindow(coordinator, j1b, RcmElectricalTestState.VoltageRemoved, 0x4B);

        coordinator.Reset(j1a);

        Assert.False(j1a.VoltageRemoved.Tested);
        Assert.Empty(j1a.VoltageRemoved.CompleteRawFrames);
        Assert.Equal(RcmResultStates.NotTested, j1a.RcmResult);
        Assert.True(j1b.VoltageRemoved.Tested);
        Assert.Single(j1b.VoltageRemoved.CompleteRawFrames);
    }

    [Fact]
    public async Task JsonSaveReloadPreservesProgressAndSourceCcfRemainsByteIdentical()
    {
        (CcfDocument document, IReadOnlyList<OtmrBenchPinDefinition> pins, byte[] source) = LoadInputs();
        RcmProfile profile = RcmProfileFactory.Create(document, pins, "Class 171", DateTimeOffset.UtcNow);
        RcmPinProfile j1a = profile.GetPin("J1", "A");
        var coordinator = new RcmCaptureWindowCoordinator();
        CaptureOneWindow(coordinator, j1a, RcmElectricalTestState.VoltageRemoved, 0x4A);
        string path = Path.Combine(Path.GetTempPath(), $"rcm_{Guid.NewGuid():N}.json");

        try
        {
            await RcmProfileJson.SaveAsync(path, profile, DateTimeOffset.UtcNow);
            RcmProfile reopened = await RcmProfileJson.LoadAsync(path);
            RcmProfileJson.EnsureMatchesSource(reopened, document.OriginalSha256, document.Length);

            RcmPinProfile restored = reopened.GetPin("J1", "A");
            Assert.True(restored.VoltageRemoved.Tested);
            Assert.Single(restored.VoltageRemoved.CompleteRawFrames);
            Assert.False(restored.VoltageApplied24V.Tested);
            Assert.Equal(profile.SourceCcfSha256, reopened.SourceCcfSha256);
            Assert.Equal(source, File.ReadAllBytes(document.SourcePath!));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void PassAndFailAreNotValidUnverifiedRcmResults()
    {
        Assert.DoesNotContain("PASS", RcmResultStates.Allowed);
        Assert.DoesNotContain("FAIL", RcmResultStates.Allowed);

        RcmPinProfile pin = CreateProfile().GetPin("J1", "A");
        var coordinator = new RcmCaptureWindowCoordinator();
        CaptureOneWindow(coordinator, pin, RcmElectricalTestState.VoltageRemoved, 0x4A);
        CaptureOneWindow(coordinator, pin, RcmElectricalTestState.VoltageApplied24V, 0x4B);
        coordinator.Compare(pin, DateTimeOffset.UtcNow);

        Assert.NotEqual("PASS", pin.RcmResult);
        Assert.NotEqual("FAIL", pin.RcmResult);
        Assert.False(pin.Comparison.DecoderVerified);
    }

    private static void CaptureOneWindow(
        RcmCaptureWindowCoordinator coordinator,
        RcmPinProfile pin,
        RcmElectricalTestState state,
        byte candidate)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        coordinator.Begin(pin, state, start);
        coordinator.AddFrame(start.AddMilliseconds(1), Frame(0xFB, 0xFB, 0x38, candidate, 0xFF));
        coordinator.Stop(start.AddSeconds(2));
    }

    private static RcmProfile CreateProfile()
    {
        (CcfDocument document, IReadOnlyList<OtmrBenchPinDefinition> pins, _) = LoadInputs();
        return RcmProfileFactory.Create(document, pins, "Class 171", DateTimeOffset.UtcNow);
    }

    private static (CcfDocument Document, IReadOnlyList<OtmrBenchPinDefinition> Pins, byte[] Source) LoadInputs()
    {
        string ccfPath = FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf");
        string pinMap = FindFromRoot("src", "CcfEditor.WinForms", "Profiles", "Class171", "Class171_Bench_PinMap.tsv");
        byte[] source = File.ReadAllBytes(ccfPath);
        return (CcfParser.Load(ccfPath), OtmrBenchProfileReader.LoadTsv(pinMap), source);
    }

    private static OtmrLiveFrame Frame(params byte[] bytes) =>
        Assert.Single(new OtmrLiveFrameAssembler().Append(bytes));

    private static string FindFromRoot(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new Xunit.Sdk.XunitException($"Fixture not found: {Path.Combine(parts)}");
    }
}
