namespace CcfEditor.Otmr.Rcm;

public static class RcmStateComparer
{
    public static void Compare(RcmPinProfile pin, DateTimeOffset comparedAt)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (!pin.VoltageRemoved.Tested || !pin.VoltageApplied24V.Tested)
            throw new InvalidOperationException("Both electrical-state capture windows are required before comparison.");

        Dictionary<string, int> removed = pin.VoltageRemoved.FeatureFrequencies;
        Dictionary<string, int> applied = pin.VoltageApplied24V.FeatureFrequencies;
        var comparison = new RcmStateComparison
        {
            ComparedAt = comparedAt,
            DecoderVerified = false,
            CommonFeatures = removed.Keys.Intersect(applied.Keys, StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(feature => $"{feature} | removed {removed[feature]} | +24V {applied[feature]}")
                .ToList(),
            UniqueFeaturesVoltageRemoved = removed.Keys.Except(applied.Keys, StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(feature => $"{feature} | count {removed[feature]}")
                .ToList(),
            UniqueFeaturesVoltageApplied24V = applied.Keys.Except(removed.Keys, StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(feature => $"{feature} | count {applied[feature]}")
                .ToList()
        };

        Dictionary<string, string> removedStable = StablePositions(pin.VoltageRemoved);
        Dictionary<string, string> appliedStable = StablePositions(pin.VoltageApplied24V);
        foreach (string position in removedStable.Keys.Intersect(appliedStable.Keys, StringComparer.Ordinal).OrderBy(value => value))
        {
            string removedValue = removedStable[position];
            string appliedValue = appliedStable[position];
            if (string.Equals(removedValue, appliedValue, StringComparison.Ordinal))
                continue;

            string difference =
                $"CANDIDATE RAW EVIDENCE: {position} stable {removedValue} " +
                $"({pin.VoltageRemoved.FrameCount}/{pin.VoltageRemoved.FrameCount}) with test voltage removed; " +
                $"stable {appliedValue} ({pin.VoltageApplied24V.FrameCount}/{pin.VoltageApplied24V.FrameCount}) with +24V applied";
            comparison.RepeatableDifferences.Add(difference);
            comparison.CandidateTransitionEvidence.Add(
                difference + ". Electrical conditions are operator-supplied; no CCF semantic state is inferred.");
        }

        pin.Comparison = comparison;
        pin.RcmResult = comparison.RepeatableDifferences.Count > 0
            ? RcmResultStates.RawDifferenceFound
            : RcmResultStates.NoRepeatableDifference;
    }

    private static Dictionary<string, string> StablePositions(RcmStateEvidence evidence)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string feature in evidence.CandidateStableFeatures.Keys)
        {
            int equals = feature.IndexOf('=');
            if (equals <= 0)
                continue;
            result[feature[..equals]] = feature[(equals + 1)..];
        }
        return result;
    }
}
