using CcfEditor.Otmr.Live;

namespace CcfEditor.Otmr.Rcm;

public enum RcmDecodedElectricalState
{
    Active,
    Inactive,
    Unknown
}

public sealed class RcmVerifiedLiveSignal
{
    public Guid PinId { get; init; }
    public string Connector { get; init; } = string.Empty;
    public string Pin { get; init; } = string.Empty;
    public string Function { get; init; } = string.Empty;
    public int? LogicalCard { get; init; }
    public int? LogicalChannel { get; init; }
    public int RawPosition { get; init; }
    public int? ObservedFramePosition { get; init; }
    public IReadOnlyList<int> MatchedFramePositions { get; init; } = Array.Empty<int>();
    public int? BitIndex { get; init; }
    public int? RawObservedValue { get; init; }
    public int? ObservedBitValue { get; init; }
    public RcmDecodedElectricalState State { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string VerificationStatus { get; init; } = string.Empty;
}

public sealed class RcmVerifiedLiveDecoder
{
    public IReadOnlyList<RcmVerifiedLiveSignal> DescribeVerifiedMappings(RcmProfile? profile)
    {
        if (profile is null)
            return Array.Empty<RcmVerifiedLiveSignal>();

        return profile.Pins
            .Where(RcmMappingVerificationService.HasExplicitlyVerifiedDecoderMapping)
            .Select(pin => Result(
                pin,
                pin.DecoderVerification.ObservedMapping!,
                null,
                null,
                RcmDecodedElectricalState.Unknown,
                "Awaiting a genuine complete live frame."))
            .ToArray();
    }

    public IReadOnlyList<RcmVerifiedLiveSignal> Decode(OtmrLiveFrame frame, RcmProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Decode(frame.Data.Span, profile);
    }

    public IReadOnlyList<RcmVerifiedLiveSignal> Decode(ReadOnlySpan<byte> frame, RcmProfile? profile)
    {
        if (!IsCompleteLiveFrame(frame) || profile is null)
            return Array.Empty<RcmVerifiedLiveSignal>();

        RcmPinProfile[] verifiedPins = profile.Pins
            .Where(RcmMappingVerificationService.HasExplicitlyVerifiedDecoderMapping)
            .ToArray();
        var results = new List<RcmVerifiedLiveSignal>();
        foreach (RcmPinProfile pin in verifiedPins.Where(pin => !IsEventRecordMapping(pin)))
        {
            RcmObservedTransition mapping = pin.DecoderVerification.ObservedMapping!;
            if ((uint)mapping.RawPosition >= (uint)frame.Length)
            {
                results.Add(Result(
                    pin, mapping, null, null, RcmDecodedElectricalState.Unknown,
                    $"raw position {mapping.RawPosition} is outside frame length {frame.Length}"));
                continue;
            }

            int observed = frame[mapping.RawPosition];
            DecodedObservation decoded = DecodeObserved(mapping, observed);
            results.Add(Result(
                pin,
                mapping,
                observed,
                decoded.ObservedBit,
                decoded.State,
                decoded.Detail,
                mapping.RawPosition,
                new[] { mapping.RawPosition }));
        }

        RcmPinProfile[] eventPins = verifiedPins.Where(IsEventRecordMapping).ToArray();
        if (eventPins.Length == 0)
            return results;

        // Verified Class 171 event mappings are learned at the first payload
        // byte (raw position 2). Longer FB FB ... FF frames contain a sequence
        // of those same event records. A byte is semantic only when it exactly
        // matches a physically verified removed/applied CCF record value.
        var eventResults = new List<EventRecordAccumulator>();
        var eventResultIndexes = new Dictionary<Guid, int>();
        for (int framePosition = 2; framePosition < frame.Length - 1; framePosition++)
        {
            int observed = frame[framePosition];
            foreach (RcmPinProfile pin in eventPins)
            {
                RcmObservedTransition mapping = pin.DecoderVerification.ObservedMapping!;
                DecodedObservation decoded = DecodeObserved(mapping, observed);
                if (decoded.State is not (RcmDecodedElectricalState.Active or RcmDecodedElectricalState.Inactive))
                    continue;

                if (!eventResultIndexes.TryGetValue(pin.Id, out int resultIndex))
                {
                    resultIndex = eventResults.Count;
                    eventResultIndexes.Add(pin.Id, resultIndex);
                    eventResults.Add(new EventRecordAccumulator(pin, mapping));
                }
                eventResults[resultIndex].Observe(framePosition, observed, decoded);
            }
        }
        results.AddRange(eventResults.Select(accumulator => accumulator.ToResult()));
        return results;
    }

    public static bool IsCompleteLiveFrame(ReadOnlySpan<byte> frame) =>
        frame.Length >= 3 && frame[0] == 0xFB && frame[1] == 0xFB && frame[^1] == 0xFF;

    private static RcmVerifiedLiveSignal Result(
        RcmPinProfile pin,
        RcmObservedTransition mapping,
        int? observed,
        int? observedBit,
        RcmDecodedElectricalState state,
        string detail,
        int? observedFramePosition = null,
        IReadOnlyList<int>? matchedFramePositions = null) => new()
    {
        PinId = pin.Id,
        Connector = pin.Connector,
        Pin = pin.Pin,
        Function = pin.Function,
        LogicalCard = pin.CcfReference?.LogicalCard ?? pin.DecoderVerification.ExpectedCcf.LogicalCard,
        LogicalChannel = pin.CcfReference?.LogicalChannel ?? pin.DecoderVerification.ExpectedCcf.LogicalChannel,
        RawPosition = mapping.RawPosition,
        ObservedFramePosition = observedFramePosition,
        MatchedFramePositions = matchedFramePositions ?? Array.Empty<int>(),
        BitIndex = mapping.Bit,
        RawObservedValue = observed,
        ObservedBitValue = observedBit,
        State = state,
        Detail = detail,
        VerificationStatus = RcmVerificationStates.Verified
    };

    private static bool IsEventRecordMapping(RcmPinProfile pin)
    {
        RcmObservedTransition mapping = pin.DecoderVerification.ObservedMapping!;
        RcmExpectedMappingSnapshot expected = pin.CcfReference is null
            ? pin.DecoderVerification.ExpectedCcf
            : new RcmExpectedMappingSnapshot
            {
                RecordA = pin.CcfReference.RecordA,
                RecordB = pin.CcfReference.RecordB
            };
        return mapping.RawPosition == 2 &&
               mapping.Bit is null &&
               expected.RecordA == mapping.RemovedValue &&
               expected.RecordB == mapping.AppliedValue;
    }

    private static DecodedObservation DecodeObserved(RcmObservedTransition mapping, int observed)
    {
        if (mapping.Bit is int bit)
        {
            if (bit is < 0 or > 7)
                return new(null, RcmDecodedElectricalState.Unknown, $"verified bit index {bit} is invalid");

            int observedBit = (observed >> bit) & 1;
            int removedBit = (mapping.RemovedValue >> bit) & 1;
            int appliedBit = (mapping.AppliedValue >> bit) & 1;
            RcmDecodedElectricalState state = observedBit == appliedBit && appliedBit != removedBit
                ? RcmDecodedElectricalState.Active
                : observedBit == removedBit && removedBit != appliedBit
                    ? RcmDecodedElectricalState.Inactive
                    : RcmDecodedElectricalState.Unknown;
            string detail = state == RcmDecodedElectricalState.Unknown
                ? $"UNEXPECTED RAW VALUE: position {mapping.RawPosition}, bit {bit}={observedBit}"
                : $"position {mapping.RawPosition}, bit {bit}={observedBit}, raw={observed:X2}";
            return new(observedBit, state, detail);
        }

        RcmDecodedElectricalState byteState = observed == mapping.AppliedValue
            ? RcmDecodedElectricalState.Active
            : observed == mapping.RemovedValue
                ? RcmDecodedElectricalState.Inactive
                : RcmDecodedElectricalState.Unknown;
        string byteDetail = byteState == RcmDecodedElectricalState.Unknown
            ? $"UNEXPECTED RAW VALUE: position {mapping.RawPosition}={observed:X2}; " +
              $"expected removed={mapping.RemovedValue:X2} or +24V={mapping.AppliedValue:X2}"
            : $"position {mapping.RawPosition}={observed:X2}";
        return new(null, byteState, byteDetail);
    }

    private sealed class EventRecordAccumulator(
        RcmPinProfile pin,
        RcmObservedTransition mapping)
    {
        private readonly List<int> _positions = new();
        private int _observed;
        private DecodedObservation _decoded = new(null, RcmDecodedElectricalState.Unknown, string.Empty);

        public void Observe(int framePosition, int observed, DecodedObservation decoded)
        {
            _positions.Add(framePosition);
            _observed = observed;
            _decoded = decoded;
        }

        public RcmVerifiedLiveSignal ToResult()
        {
            int lastPosition = _positions[^1];
            string positions = string.Join(", ", _positions);
            return Result(
                pin,
                mapping,
                _observed,
                _decoded.ObservedBit,
                _decoded.State,
                $"verified event record at frame position {lastPosition}={_observed:X2}; " +
                $"matched position(s): {positions}",
                lastPosition,
                _positions.ToArray());
        }
    }

    private sealed record DecodedObservation(
        int? ObservedBit,
        RcmDecodedElectricalState State,
        string Detail);
}
