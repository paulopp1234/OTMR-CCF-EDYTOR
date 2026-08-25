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
    public int? BitIndex { get; init; }
    public int? RawObservedValue { get; init; }
    public int? ObservedBitValue { get; init; }
    public RcmDecodedElectricalState State { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string VerificationStatus { get; init; } = string.Empty;
}

public sealed class RcmVerifiedLiveDecoder
{
    public IReadOnlyList<RcmVerifiedLiveSignal> Decode(OtmrLiveFrame frame, RcmProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Decode(frame.Data.Span, profile);
    }

    public IReadOnlyList<RcmVerifiedLiveSignal> Decode(ReadOnlySpan<byte> frame, RcmProfile? profile)
    {
        if (!IsCompleteLiveFrame(frame) || profile is null)
            return Array.Empty<RcmVerifiedLiveSignal>();

        var results = new List<RcmVerifiedLiveSignal>();
        foreach (RcmPinProfile pin in profile.Pins.Where(IsExplicitlyVerified))
        {
            RcmDecoderVerification verification = pin.DecoderVerification;
            RcmObservedTransition mapping = verification.ObservedMapping!;
            if ((uint)mapping.RawPosition >= (uint)frame.Length)
            {
                results.Add(Result(
                    pin, mapping, null, null, RcmDecodedElectricalState.Unknown,
                    $"raw position {mapping.RawPosition} is outside frame length {frame.Length}"));
                continue;
            }

            int observed = frame[mapping.RawPosition];
            if (mapping.Bit is int bit)
            {
                if (bit is < 0 or > 7)
                {
                    results.Add(Result(
                        pin, mapping, observed, null, RcmDecodedElectricalState.Unknown,
                        $"verified bit index {bit} is invalid"));
                    continue;
                }

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
                results.Add(Result(pin, mapping, observed, observedBit, state, detail));
            }
            else
            {
                RcmDecodedElectricalState state = observed == mapping.AppliedValue
                    ? RcmDecodedElectricalState.Active
                    : observed == mapping.RemovedValue
                        ? RcmDecodedElectricalState.Inactive
                        : RcmDecodedElectricalState.Unknown;
                string detail = state == RcmDecodedElectricalState.Unknown
                    ? $"UNEXPECTED RAW VALUE: position {mapping.RawPosition}={observed:X2}; " +
                      $"expected removed={mapping.RemovedValue:X2} or +24V={mapping.AppliedValue:X2}"
                    : $"position {mapping.RawPosition}={observed:X2}";
                results.Add(Result(pin, mapping, observed, null, state, detail));
            }
        }
        return results;
    }

    public static bool IsCompleteLiveFrame(ReadOnlySpan<byte> frame) =>
        frame.Length >= 3 && frame[0] == 0xFB && frame[1] == 0xFB && frame[^1] == 0xFF;

    private static bool IsExplicitlyVerified(RcmPinProfile pin) =>
        pin.PhysicalMappingAssigned && pin.Testable &&
        pin.DecoderVerification.Status == RcmVerificationStates.Verified &&
        pin.DecoderVerification.ObservedMapping is not null &&
        pin.Comparison.DecoderVerified;

    private static RcmVerifiedLiveSignal Result(
        RcmPinProfile pin,
        RcmObservedTransition mapping,
        int? observed,
        int? observedBit,
        RcmDecodedElectricalState state,
        string detail) => new()
    {
        PinId = pin.Id,
        Connector = pin.Connector,
        Pin = pin.Pin,
        Function = pin.Function,
        LogicalCard = pin.CcfReference?.LogicalCard ?? pin.DecoderVerification.ExpectedCcf.LogicalCard,
        LogicalChannel = pin.CcfReference?.LogicalChannel ?? pin.DecoderVerification.ExpectedCcf.LogicalChannel,
        RawPosition = mapping.RawPosition,
        BitIndex = mapping.Bit,
        RawObservedValue = observed,
        ObservedBitValue = observedBit,
        State = state,
        Detail = detail,
        VerificationStatus = RcmVerificationStates.Verified
    };
}
