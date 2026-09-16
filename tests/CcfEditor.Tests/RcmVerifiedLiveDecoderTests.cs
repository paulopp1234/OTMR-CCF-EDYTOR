using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.Tests;

public sealed class RcmVerifiedLiveDecoderTests
{
    private readonly RcmVerifiedLiveDecoder _decoder = new();

    [Fact]
    public void VerifiedBitMappingDecodesRemovedAndApplied()
    {
        RcmProfile profile = Profile(VerifiedPin("A", 3, bit: 2, removed: 0x00, applied: 0x04));

        RcmVerifiedLiveSignal removed = Assert.Single(_decoder.Decode(Frame(0x00), profile));
        RcmVerifiedLiveSignal applied = Assert.Single(_decoder.Decode(Frame(0x04), profile));

        Assert.Equal(RcmDecodedElectricalState.Inactive, removed.State);
        Assert.Equal(0, removed.ObservedBitValue);
        Assert.Equal(RcmDecodedElectricalState.Active, applied.State);
        Assert.Equal(1, applied.ObservedBitValue);
        Assert.Equal(2, applied.BitIndex);
        Assert.Equal(RcmVerificationStates.Verified, applied.VerificationStatus);
    }

    [Fact]
    public void VerifiedMappingsCanBeDescribedBeforeAnyLiveFrame()
    {
        RcmProfile profile = Profile(
            VerifiedPin("A", 3, bit: 0, removed: 0x00, applied: 0x01),
            VerifiedPin("B", 4, bit: 1, removed: 0x00, applied: 0x02));

        IReadOnlyList<RcmVerifiedLiveSignal> mappings = _decoder.DescribeVerifiedMappings(profile);

        Assert.Equal(2, mappings.Count);
        Assert.All(mappings, mapping =>
        {
            Assert.Equal(RcmDecodedElectricalState.Unknown, mapping.State);
            Assert.Null(mapping.RawObservedValue);
            Assert.Equal(RcmVerificationStates.Verified, mapping.VerificationStatus);
            Assert.Contains("Awaiting", mapping.Detail, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ExplicitVerificationIsIndependentOfBenchTestabilityWorkflow()
    {
        RcmPinProfile pin = VerifiedPin("A", 3, bit: 0, removed: 0x00, applied: 0x01);
        pin.Testable = false;
        pin.RcmResult = RcmResultStates.NotTestable;
        pin.Comparison.DecoderVerified = false;
        RcmProfile profile = Profile(pin);

        RcmVerifiedLiveSignal beforeLiveData = Assert.Single(_decoder.DescribeVerifiedMappings(profile));
        RcmVerifiedLiveSignal afterLiveData = Assert.Single(_decoder.Decode(Frame(0x01), profile));

        Assert.True(RcmMappingVerificationService.HasExplicitlyVerifiedDecoderMapping(pin));
        Assert.Equal(RcmDecodedElectricalState.Unknown, beforeLiveData.State);
        Assert.Null(beforeLiveData.RawObservedValue);
        Assert.Equal(RcmDecodedElectricalState.Active, afterLiveData.State);
        Assert.Equal(0x01, afterLiveData.RawObservedValue);
    }

    [Fact]
    public void MixedProfileExposesOnlyExplicitValidVerifiedMappings()
    {
        RcmPinProfile verified = VerifiedPin("A", 3, bit: 0, removed: 0, applied: 1);
        verified.Testable = false;
        verified.RcmResult = RcmResultStates.NotTestable;
        RcmPinProfile unverified = VerifiedPin("B", 4, bit: 0, removed: 0, applied: 1);
        unverified.DecoderVerification.Status = RcmVerificationStates.NotVerified;
        RcmPinProfile conflict = VerifiedPin("C", 5, bit: 0, removed: 0, applied: 1);
        conflict.DecoderVerification.Status = RcmVerificationStates.Conflict;
        RcmPinProfile invalid = VerifiedPin("D", 6, bit: 0, removed: 0, applied: 0);

        RcmVerifiedLiveSignal mapping = Assert.Single(
            _decoder.DescribeVerifiedMappings(Profile(verified, unverified, conflict, invalid)));

        Assert.Equal("A", mapping.Pin);
    }

    [Fact]
    public async Task LoadedLegacyShapedProfileExposesAllFiftyExplicitVerifiedMappings()
    {
        RcmPinProfile[] pins = Enumerable.Range(0, 50)
            .Select(index =>
            {
                RcmPinProfile pin = VerifiedPin($"P{index + 1:D2}", 3, bit: index % 8, removed: 0, applied: 1 << (index % 8));
                pin.Function = $"Verified function {index + 1}";
                pin.Testable = false;
                pin.RcmResult = RcmResultStates.NotTestable;
                pin.CcfReference!.LogicalChannel = index;
                pin.DecoderVerification.Function = pin.Function;
                pin.DecoderVerification.ExpectedCcf.LogicalChannel = index;
                MakePersistableVerified(pin);
                return pin;
            })
            .ToArray();
        string path = Path.Combine(Path.GetTempPath(), $"legacy-verified-{Guid.NewGuid():N}.json");
        try
        {
            await RcmProfileJson.SaveAsync(path, Profile(pins), DateTimeOffset.UtcNow);
            RcmProfile loaded = await RcmProfileJson.LoadAsync(path);

            IReadOnlyList<RcmVerifiedLiveSignal> mappings = _decoder.DescribeVerifiedMappings(loaded);

            Assert.Equal(50, mappings.Count);
            Assert.All(mappings, mapping => Assert.Equal(RcmDecodedElectricalState.Unknown, mapping.State));
            Assert.All(loaded.Pins, pin =>
            {
                Assert.False(pin.Testable);
                Assert.Equal(RcmResultStates.NotTestable, pin.RcmResult);
            });
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(RcmVerificationStates.CandidateFound)]
    [InlineData(RcmVerificationStates.Eligible)]
    [InlineData(RcmVerificationStates.Conflict)]
    public void MappingWithoutCurrentExplicitVerificationIsIgnored(string status)
    {
        RcmPinProfile pin = VerifiedPin("A", 3, bit: 0, removed: 0, applied: 1);
        pin.DecoderVerification.Status = status;

        Assert.Empty(_decoder.Decode(Frame(0x01), Profile(pin)));
    }

    [Fact]
    public void UnexpectedByteValueBecomesUnknown()
    {
        RcmPinProfile pin = VerifiedPin("A", 3, bit: null, removed: 0x10, applied: 0x20);

        RcmVerifiedLiveSignal signal = Assert.Single(_decoder.Decode(Frame(0x30), Profile(pin)));

        Assert.Equal(RcmDecodedElectricalState.Unknown, signal.State);
        Assert.Equal(0x30, signal.RawObservedValue);
        Assert.Contains("UNEXPECTED RAW VALUE", signal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleVerifiedPinsDecodeFromOneFrame()
    {
        RcmPinProfile first = VerifiedPin("A", 3, bit: 0, removed: 0x00, applied: 0x01);
        RcmPinProfile second = VerifiedPin("B", 4, bit: 1, removed: 0x02, applied: 0x00);
        second.Function = "Brake 1";
        second.CcfReference!.LogicalChannel = 1;

        IReadOnlyList<RcmVerifiedLiveSignal> signals = _decoder.Decode(
            new byte[] { 0xFB, 0xFB, 0x38, 0x01, 0x02, 0xFF }, Profile(first, second));

        Assert.Equal(2, signals.Count);
        Assert.Equal(RcmDecodedElectricalState.Active, signals.Single(signal => signal.Pin == "A").State);
        Assert.Equal(RcmDecodedElectricalState.Inactive, signals.Single(signal => signal.Pin == "B").State);
    }

    [Fact]
    public void ConcatenatedVerifiedEventRecordsDecodeAllThreeSignalsFromOneCompleteFrame()
    {
        RcmPinProfile throttle1 = EventPin("A", "Throttle 1", removed: 0x00, applied: 0x0C);
        RcmPinProfile throttle2 = EventPin("B", "Throttle 2", removed: 0x01, applied: 0x0D);
        RcmPinProfile forward = EventPin("D", "Forward", removed: 0x03, applied: 0x0F);

        IReadOnlyList<RcmVerifiedLiveSignal> signals = _decoder.Decode(
            new byte[] { 0xFB, 0xFB, 0x0C, 0x0D, 0x03, 0xFF },
            Profile(throttle1, throttle2, forward));

        Assert.Collection(
            signals,
            signal => AssertDecodedEvent(signal, "A", "Throttle 1", RcmDecodedElectricalState.Active, 0x0C, 2),
            signal => AssertDecodedEvent(signal, "B", "Throttle 2", RcmDecodedElectricalState.Active, 0x0D, 3),
            signal => AssertDecodedEvent(signal, "D", "Forward", RcmDecodedElectricalState.Inactive, 0x03, 4));
    }

    [Fact]
    public void RepeatedRecordsForOneVerifiedSignalUseTheLastGenuineObservationAndMapEveryRecordPosition()
    {
        RcmPinProfile throttle1 = EventPin("A", "Throttle 1", removed: 0x00, applied: 0x0C);

        RcmVerifiedLiveSignal signal = Assert.Single(_decoder.Decode(
            new byte[] { 0xFB, 0xFB, 0x0C, 0x00, 0x0C, 0x00, 0x0C, 0xFF },
            Profile(throttle1)));

        Assert.Equal(RcmDecodedElectricalState.Active, signal.State);
        Assert.Equal(0x0C, signal.RawObservedValue);
        Assert.Equal(6, signal.ObservedFramePosition);
        Assert.Equal(new[] { 2, 3, 4, 5, 6 }, signal.MatchedFramePositions);
    }

    [Fact]
    public void OutOfRangeRawPositionReturnsUnknownWithoutThrowing()
    {
        RcmPinProfile pin = VerifiedPin("A", 99, bit: 0, removed: 0, applied: 1);

        RcmVerifiedLiveSignal signal = Assert.Single(_decoder.Decode(Frame(0x01), Profile(pin)));

        Assert.Equal(RcmDecodedElectricalState.Unknown, signal.State);
        Assert.Null(signal.RawObservedValue);
        Assert.Contains("outside frame length", signal.Detail, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> MalformedFrames => new[]
    {
        new object[] { new byte[] { 0xFB, 0xFB } },
        new object[] { new byte[] { 0xFB, 0xFB, 0x38, 0x01 } },
        new object[] { new byte[] { 0xFA, 0xFB, 0x38, 0x01, 0xFF } },
        new object[] { new byte[] { 0xFB, 0xFA, 0x38, 0x01, 0xFF } }
    };

    [Theory]
    [MemberData(nameof(MalformedFrames))]
    public void MalformedOrIncompleteFrameIsNotDecoded(byte[] bytes)
    {
        Assert.Empty(_decoder.Decode(bytes, Profile(VerifiedPin("A", 3, 0, 0, 1))));
    }

    [Fact]
    public async Task SaveReloadPreservesVerifiedLiveDecodeCapability()
    {
        RcmProfile profile = CreateServiceVerifiedProfile();
        string path = Path.Combine(Path.GetTempPath(), $"verified-live-{Guid.NewGuid():N}.json");
        try
        {
            await RcmProfileJson.SaveAsync(path, profile, DateTimeOffset.UtcNow);
            RcmProfile restored = await RcmProfileJson.LoadAsync(path);

            RcmVerifiedLiveSignal signal = Assert.Single(_decoder.Decode(Frame(0x0D), restored));

            Assert.Equal(RcmDecodedElectricalState.Active, signal.State);
            Assert.Equal("J1", signal.Connector);
            Assert.Equal("A", signal.Pin);
            Assert.Equal(0, signal.LogicalCard);
            Assert.Equal(0, signal.LogicalChannel);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    internal static RcmProfile Profile(params RcmPinProfile[] pins) => new()
    {
        Pins = pins.ToList(),
        Connectors = new List<RcmConnector> { new() { Name = "J1" } },
        SourceCcfFilename = "Class171.ccf",
        SourceCcfSha256 = new string('A', 64),
        SourceCcfSize = 26600,
        CreationTimestamp = DateTimeOffset.UtcNow,
        LastModifiedTimestamp = DateTimeOffset.UtcNow
    };

    internal static RcmPinProfile VerifiedPin(
        string pinName,
        int position,
        int? bit,
        int removed,
        int applied)
    {
        var mapping = new RcmObservedTransition
        {
            RawPosition = position,
            Bit = bit,
            RemovedValue = removed,
            AppliedValue = applied,
            TransitionPolarity = bit.HasValue ? $"bit {bit}: removed 0 -> applied 1" : "byte transition"
        };
        return new RcmPinProfile
        {
            Connector = "J1",
            Pin = pinName,
            Function = "Throttle 1",
            Testable = true,
            CcfReference = new RcmCcfReference
            {
                LogicalCard = 0, LogicalChannel = 0, RecordA = 0, RecordB = 12
            },
            Comparison = new RcmStateComparison { DecoderVerified = true },
            DecoderVerification = new RcmDecoderVerification
            {
                Status = RcmVerificationStates.Verified,
                ObservedMapping = mapping,
                ExpectedCcf = new RcmExpectedMappingSnapshot
                {
                    LogicalCard = 0, LogicalChannel = 0, RecordA = 0, RecordB = 12
                },
                Connector = "J1",
                Pin = pinName,
                Function = "Throttle 1",
                VerificationMethod = "physical stimulation",
                VerifiedAt = DateTimeOffset.UtcNow,
                RequiredRunCount = 3,
                SuccessfulRepetitionCount = 3
            }
        };
    }

    internal static RcmPinProfile EventPin(
        string pinName,
        string function,
        int removed,
        int applied)
    {
        RcmPinProfile pin = VerifiedPin(pinName, 2, bit: null, removed, applied);
        pin.Function = function;
        pin.CcfReference!.RecordA = removed;
        pin.CcfReference.RecordB = applied;
        pin.DecoderVerification.Function = function;
        pin.DecoderVerification.ExpectedCcf.RecordA = removed;
        pin.DecoderVerification.ExpectedCcf.RecordB = applied;
        return pin;
    }

    private static void AssertDecodedEvent(
        RcmVerifiedLiveSignal signal,
        string pin,
        string function,
        RcmDecodedElectricalState state,
        int rawValue,
        int framePosition)
    {
        Assert.Equal(pin, signal.Pin);
        Assert.Equal(function, signal.Function);
        Assert.Equal(state, signal.State);
        Assert.Equal(rawValue, signal.RawObservedValue);
        Assert.Equal(framePosition, signal.ObservedFramePosition);
        Assert.Equal(new[] { framePosition }, signal.MatchedFramePositions);
        Assert.Equal(RcmVerificationStates.Verified, signal.VerificationStatus);
    }

    private static OtmrLiveFrame Frame(byte value) =>
        Assert.Single(new OtmrLiveFrameAssembler().Append(
            new byte[] { 0xFB, 0xFB, 0x38, value, 0xFF }));

    private static RcmProfile CreateServiceVerifiedProfile()
    {
        RcmPinProfile pin = VerifiedPin("A", 3, bit: 0, removed: 0x0C, applied: 0x0D);
        pin.Comparison = new RcmStateComparison();
        pin.DecoderVerification = new RcmDecoderVerification();
        pin.VerificationRuns.Clear();
        for (int run = 1; run <= 3; run++)
        {
            DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(run);
            var coordinator = new RcmInputTestCoordinator(new RcmCaptureWindowCoordinator());
            coordinator.Start(pin, start);
            coordinator.AddFrame(start.AddSeconds(1), Frame(0x0D));
            coordinator.CompleteCapture(start.AddSeconds(3));
            coordinator.AddFrame(start.AddSeconds(4), Frame(0x0C));
            coordinator.CompleteCapture(start.AddSeconds(6));
        }
        RcmMappingVerificationService.VerifyMapping(pin, DateTimeOffset.UtcNow.AddHours(1));
        return Profile(pin);
    }

    private static void MakePersistableVerified(RcmPinProfile pin)
    {
        RcmObservedTransition observed = pin.DecoderVerification.ObservedMapping!;
        pin.VerificationRuns = Enumerable.Range(1, 3)
            .Select(runNumber => new RcmPhysicalVerificationRun
            {
                RunNumber = runNumber,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(runNumber),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(runNumber).AddSeconds(6),
                Connector = pin.Connector,
                Pin = pin.Pin,
                Function = pin.Function,
                ExpectedCcf = new RcmExpectedMappingSnapshot
                {
                    LogicalCard = pin.CcfReference!.LogicalCard,
                    LogicalChannel = pin.CcfReference.LogicalChannel,
                    RecordA = pin.CcfReference.RecordA,
                    RecordB = pin.CcfReference.RecordB
                },
                CandidateTransitions = new List<RcmObservedTransition>
                {
                    new()
                    {
                        RawPosition = observed.RawPosition,
                        Bit = observed.Bit,
                        RemovedValue = observed.RemovedValue,
                        AppliedValue = observed.AppliedValue,
                        TransitionPolarity = observed.TransitionPolarity
                    }
                }
            })
            .ToList();
        pin.DecoderVerification.RequiredRunCount = 3;
        pin.DecoderVerification.SuccessfulRepetitionCount = 3;
        pin.DecoderVerification.QualifyingRunIds = pin.VerificationRuns.Select(run => run.RunId).ToList();
        pin.Comparison.DecoderVerified = true;
    }
}
