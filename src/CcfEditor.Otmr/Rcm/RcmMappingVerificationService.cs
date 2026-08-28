namespace CcfEditor.Otmr.Rcm;

public static class RcmMappingVerificationService
{
    /// <summary>
    /// Returns whether the profile contains an explicitly operator-verified mapping
    /// that the live decoder can safely evaluate. Bench testability/result fields
    /// describe the capture workflow and are deliberately not part of this policy.
    /// </summary>
    public static bool HasExplicitlyVerifiedDecoderMapping(RcmPinProfile? pin)
    {
        RcmObservedTransition? mapping = pin?.DecoderVerification?.ObservedMapping;
        if (pin?.DecoderVerification?.Status != RcmVerificationStates.Verified || mapping is null ||
            mapping.RawPosition < 0 || mapping.RemovedValue is < 0 or > 255 ||
            mapping.AppliedValue is < 0 or > 255 || mapping.Bit is < 0 or > 7)
            return false;

        return mapping.Bit is int bit
            ? ((mapping.RemovedValue >> bit) & 1) != ((mapping.AppliedValue >> bit) & 1)
            : mapping.RemovedValue != mapping.AppliedValue;
    }

    public static RcmPhysicalVerificationRun RecordCompletedRun(
        RcmPinProfile pin,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        int requiredRuns = 3)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (!pin.PhysicalMappingAssigned || !pin.Testable)
            throw new InvalidOperationException("Physical Connector/Pin mapping and Testable are required.");
        if (!HasGenuineEvidence(pin.VoltageApplied24V) || !HasGenuineEvidence(pin.VoltageRemoved) ||
            pin.Comparison.ComparedAt is null)
            throw new InvalidOperationException("A complete guided test with both genuine states is required.");
        if (requiredRuns < 2)
            throw new ArgumentOutOfRangeException(nameof(requiredRuns), "At least two physical runs are required.");

        var run = new RcmPhysicalVerificationRun
        {
            RunNumber = pin.VerificationRuns.Count + 1,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Connector = pin.Connector,
            Pin = pin.Pin,
            Function = pin.Function,
            ExpectedCcf = Expected(pin.CcfReference),
            VoltageApplied24V = Clone(pin.VoltageApplied24V),
            VoltageRemoved = Clone(pin.VoltageRemoved),
            Comparison = Clone(pin.Comparison),
            CandidateTransitions = FindStructuredTransitions(pin.VoltageRemoved, pin.VoltageApplied24V).ToList()
        };
        pin.VerificationRuns.Add(run);
        Evaluate(pin, requiredRuns, completedAt);
        return run;
    }

    public static void Evaluate(RcmPinProfile pin, int requiredRuns = 3, DateTimeOffset? evaluatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(pin);
        RcmDecoderVerification verification = pin.DecoderVerification ??= new RcmDecoderVerification();
        verification.RequiredRunCount = requiredRuns;
        verification.QualifyingRunIds ??= new List<Guid>();
        verification.ContradictoryRunIds ??= new List<Guid>();
        verification.AuditHistory ??= new List<RcmVerificationAuditEvent>();

        if (verification.Status == RcmVerificationStates.Conflict)
        {
            pin.Comparison.DecoderVerified = false;
            return;
        }

        if (verification.Status == RcmVerificationStates.Verified && verification.ObservedMapping is not null)
        {
            string verifiedKey = verification.ObservedMapping.StableKey;
            bool identityChanged = !string.Equals(verification.Connector, pin.Connector, StringComparison.OrdinalIgnoreCase) ||
                                   !string.Equals(verification.Pin, pin.Pin, StringComparison.Ordinal) ||
                                   !string.Equals(verification.Function, pin.Function, StringComparison.Ordinal) ||
                                   !ExpectedEquals(verification.ExpectedCcf, Expected(pin.CcfReference));
            List<RcmPhysicalVerificationRun> contradictions = pin.VerificationRuns
                .Where(run => !run.CandidateTransitions.Any(candidate => candidate.StableKey == verifiedKey))
                .ToList();
            if (contradictions.Count > 0 || identityChanged)
            {
                verification.Status = RcmVerificationStates.Conflict;
                verification.ConflictDetectedAt = evaluatedAt ?? DateTimeOffset.UtcNow;
                verification.ContradictoryRunIds = contradictions.Select(run => run.RunId).ToList();
                verification.AuditHistory.Add(new RcmVerificationAuditEvent
                {
                    Timestamp = verification.ConflictDetectedAt.Value,
                    Action = "VERIFICATION_CONFLICT",
                    Detail = identityChanged
                        ? "The physical identity, function, or expected CCF mapping changed after verification."
                        : "A later physical-stimulation run did not contain the verified raw transition.",
                    RunIds = verification.ContradictoryRunIds.ToList()
                });
                pin.Comparison.DecoderVerified = false;
            }
            else
            {
                pin.Comparison.DecoderVerified = true;
                verification.SuccessfulRepetitionCount = pin.VerificationRuns.Count;
                verification.QualifyingRunIds = pin.VerificationRuns.Select(run => run.RunId).ToList();
            }
            return;
        }

        verification.Status = RcmVerificationStates.NotVerified;
        verification.ObservedMapping = null;
        verification.SuccessfulRepetitionCount = 0;
        verification.QualifyingRunIds.Clear();
        pin.Comparison.DecoderVerified = false;
        if (pin.VerificationRuns.Count == 0)
            return;

        var occurrences = pin.VerificationRuns
            .SelectMany(run => run.CandidateTransitions.Select(candidate => (Run: run, Candidate: candidate)))
            .GroupBy(item => item.Candidate.StableKey, StringComparer.Ordinal)
            .Select(group => new
            {
                Candidate = group.First().Candidate,
                Runs = group.Select(item => item.Run).DistinctBy(run => run.RunId).OrderBy(run => run.RunNumber).ToList()
            })
            .OrderByDescending(item => item.Runs.Count)
            .ThenBy(item => item.Candidate.RawPosition)
            .ThenBy(item => item.Candidate.Bit)
            .ToList();
        if (occurrences.Count == 0)
            return;

        int bestCount = occurrences[0].Runs.Count;
        var best = occurrences.Where(item => item.Runs.Count == bestCount).ToList();
        verification.Status = RcmVerificationStates.CandidateFound;
        verification.SuccessfulRepetitionCount = bestCount;
        verification.QualifyingRunIds = best.Count == 1
            ? best[0].Runs.Select(run => run.RunId).ToList()
            : new List<Guid>();
        verification.ObservedMapping = best.Count == 1 ? Clone(best[0].Candidate) : null;

        bool oneUnambiguousTransitionInEveryRun = best.Count == 1 && bestCount == pin.VerificationRuns.Count;
        bool identityAndExpectedMappingValid = pin.PhysicalMappingAssigned && pin.Testable && Expected(pin.CcfReference).IsComplete;
        RcmExpectedMappingSnapshot currentExpected = Expected(pin.CcfReference);
        bool allRunsMatchCurrentMapping = pin.VerificationRuns.All(run =>
            string.Equals(run.Connector, pin.Connector, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(run.Pin, pin.Pin, StringComparison.Ordinal) &&
            string.Equals(run.Function, pin.Function, StringComparison.Ordinal) &&
            ExpectedEquals(run.ExpectedCcf, currentExpected));
        if (oneUnambiguousTransitionInEveryRun && bestCount >= requiredRuns &&
            identityAndExpectedMappingValid && allRunsMatchCurrentMapping)
            verification.Status = RcmVerificationStates.Eligible;
    }

    public static void VerifyMapping(RcmPinProfile pin, DateTimeOffset verifiedAt)
    {
        ArgumentNullException.ThrowIfNull(pin);
        RcmDecoderVerification verification = pin.DecoderVerification;
        if (verification.Status != RcmVerificationStates.Eligible || verification.ObservedMapping is null)
            throw new InvalidOperationException("The mapping is not eligible for operator verification.");

        verification.Status = RcmVerificationStates.Verified;
        verification.VerifiedAt = verifiedAt;
        verification.VerificationMethod = "physical stimulation";
        verification.Connector = pin.Connector;
        verification.Pin = pin.Pin;
        verification.Function = pin.Function;
        verification.ExpectedCcf = Expected(pin.CcfReference);
        verification.ConflictDetectedAt = null;
        verification.ContradictoryRunIds.Clear();
        verification.AuditHistory.Add(new RcmVerificationAuditEvent
        {
            Timestamp = verifiedAt,
            Action = "VERIFY_MAPPING",
            Detail = "Operator confirmed the repeatable physical-stimulation candidate.",
            RunIds = verification.QualifyingRunIds.ToList()
        });
        pin.Comparison.DecoderVerified = true;
    }

    public static void ResetVerification(RcmPinProfile pin, DateTimeOffset resetAt)
    {
        ArgumentNullException.ThrowIfNull(pin);
        List<RcmVerificationAuditEvent> history = pin.DecoderVerification.AuditHistory?.ToList()
            ?? new List<RcmVerificationAuditEvent>();
        history.Add(new RcmVerificationAuditEvent
        {
            Timestamp = resetAt,
            Action = "RESET_VERIFICATION",
            Detail = "Operator rejected/reset accumulated mapping verification evidence.",
            RunIds = pin.VerificationRuns.Select(run => run.RunId).ToList()
        });
        int required = Math.Max(2, pin.DecoderVerification.RequiredRunCount);
        pin.VerificationRuns.Clear();
        pin.DecoderVerification = new RcmDecoderVerification
        {
            RequiredRunCount = required,
            AuditHistory = history
        };
        pin.Comparison.DecoderVerified = false;
    }

    public static IReadOnlyList<RcmObservedTransition> FindStructuredTransitions(
        RcmStateEvidence removed,
        RcmStateEvidence applied)
    {
        Dictionary<int, int> removedValues = StablePositionValues(removed);
        Dictionary<int, int> appliedValues = StablePositionValues(applied);
        var transitions = new List<RcmObservedTransition>();
        foreach (int position in removedValues.Keys.Intersect(appliedValues.Keys).OrderBy(value => value))
        {
            int removedValue = removedValues[position];
            int appliedValue = appliedValues[position];
            if (removedValue == appliedValue)
                continue;
            int xor = removedValue ^ appliedValue;
            int? bit = (xor & (xor - 1)) == 0 ? System.Numerics.BitOperations.TrailingZeroCount((uint)xor) : null;
            string polarity = bit.HasValue
                ? $"bit {bit}: removed {(removedValue >> bit.Value) & 1} -> applied {(appliedValue >> bit.Value) & 1}"
                : $"byte: removed {removedValue:X2} -> applied {appliedValue:X2}";
            transitions.Add(new RcmObservedTransition
            {
                RawPosition = position,
                RemovedValue = removedValue,
                AppliedValue = appliedValue,
                Bit = bit,
                TransitionPolarity = polarity
            });
        }
        return transitions;
    }

    private static Dictionary<int, int> StablePositionValues(RcmStateEvidence evidence)
    {
        var values = new Dictionary<int, int>();
        foreach (string feature in evidence.CandidateStableFeatures.Keys)
        {
            if (!feature.StartsWith("POSITION[", StringComparison.Ordinal))
                continue;
            int close = feature.IndexOf(']');
            int equals = feature.IndexOf('=');
            if (close <= 9 || equals <= close ||
                !int.TryParse(feature.AsSpan(9, close - 9), out int position) ||
                !int.TryParse(feature.AsSpan(equals + 1), System.Globalization.NumberStyles.HexNumber, null, out int value))
                continue;
            values[position] = value;
        }
        return values;
    }

    private static bool HasGenuineEvidence(RcmStateEvidence evidence) => evidence.Tested && evidence.FrameCount > 0;

    private static RcmExpectedMappingSnapshot Expected(RcmCcfReference? source) => new()
    {
        LogicalCard = source?.LogicalCard,
        LogicalChannel = source?.LogicalChannel,
        RecordA = source?.RecordA,
        RecordB = source?.RecordB
    };

    private static bool ExpectedEquals(RcmExpectedMappingSnapshot left, RcmExpectedMappingSnapshot right) =>
        left.LogicalCard == right.LogicalCard && left.LogicalChannel == right.LogicalChannel &&
        left.RecordA == right.RecordA && left.RecordB == right.RecordB;

    private static RcmObservedTransition Clone(RcmObservedTransition source) => new()
    {
        RawPosition = source.RawPosition,
        RemovedValue = source.RemovedValue,
        AppliedValue = source.AppliedValue,
        Bit = source.Bit,
        TransitionPolarity = source.TransitionPolarity
    };

    private static RcmStateEvidence Clone(RcmStateEvidence source) => new()
    {
        Tested = source.Tested,
        NoOtmrData = source.NoOtmrData,
        CaptureStart = source.CaptureStart,
        CaptureStop = source.CaptureStop,
        CompleteRawFrames = source.CompleteRawFrames.Select(frame => new RcmRawFrameEvidence
        {
            SequenceNumber = frame.SequenceNumber,
            Timestamp = frame.Timestamp,
            RawFrameHex = frame.RawFrameHex,
            RawFrameBytes = frame.RawFrameBytes.ToList()
        }).ToList(),
        FeatureFrequencies = new Dictionary<string, int>(source.FeatureFrequencies, StringComparer.Ordinal),
        CandidateStableFeatures = new Dictionary<string, int>(source.CandidateStableFeatures, StringComparer.Ordinal),
        CandidateRawSignature = source.CandidateRawSignature
    };

    private static RcmStateComparison Clone(RcmStateComparison source) => new()
    {
        ComparedAt = source.ComparedAt,
        CommonFeatures = source.CommonFeatures.ToList(),
        UniqueFeaturesVoltageRemoved = source.UniqueFeaturesVoltageRemoved.ToList(),
        UniqueFeaturesVoltageApplied24V = source.UniqueFeaturesVoltageApplied24V.ToList(),
        RepeatableDifferences = source.RepeatableDifferences.ToList(),
        CandidateTransitionEvidence = source.CandidateTransitionEvidence.ToList(),
        DecoderVerified = source.DecoderVerified
    };
}
