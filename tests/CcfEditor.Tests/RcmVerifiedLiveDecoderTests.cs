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
}
