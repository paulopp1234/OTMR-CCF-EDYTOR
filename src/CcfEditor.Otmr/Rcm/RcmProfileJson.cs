using System.Text.Json;
using CcfEditor.Core;

namespace CcfEditor.Otmr.Rcm;

public static class RcmProfileJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task SaveAsync(
        string path,
        RcmProfile profile,
        DateTimeOffset modifiedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(profile);
        MigrateAndNormalize(profile);
        Validate(profile);
        profile.SchemaVersion = RcmProfile.CurrentSchemaVersion;
        profile.LastModifiedTimestamp = modifiedAt;

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        await JsonSerializer.SerializeAsync(stream, profile, Options, cancellationToken);
    }

    public static async Task<RcmProfile> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        RcmProfile profile = await JsonSerializer.DeserializeAsync<RcmProfile>(stream, Options, cancellationToken)
            ?? throw new InvalidDataException("The RCM JSON profile is empty.");
        MigrateAndNormalize(profile);
        Validate(profile);
        return profile;
    }

    public static string SerializeSnapshot(RcmProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        MigrateAndNormalize(profile);
        Validate(profile);
        return JsonSerializer.Serialize(profile, Options);
    }

    public static void EnsureMatchesSource(RcmProfile profile, string sha256, long size)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(profile.SourceCcfSha256, sha256, StringComparison.OrdinalIgnoreCase) ||
            profile.SourceCcfSize != size)
        {
            throw new InvalidDataException("The RCM profile does not belong to the currently loaded source CCF.");
        }
    }

    public static string GetCcfStatus(RcmProfile profile, CcfDocument? document)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (document is null)
            return "CCF NOT LOADED";
        return string.Equals(profile.SourceCcfSha256, document.OriginalSha256, StringComparison.OrdinalIgnoreCase) &&
               profile.SourceCcfSize == document.Length
            ? "CCF MATCH"
            : "CCF MISMATCH";
    }

    public static void MigrateAndNormalize(RcmProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion is not ("1.0" or "1.1" or "1.2" or RcmProfile.CurrentSchemaVersion))
            throw new InvalidDataException($"Unsupported RCM schema version '{profile.SchemaVersion}'.");

        if (profile.RequiredVerificationRuns < 2)
            profile.RequiredVerificationRuns = 3;

        profile.Connectors ??= new List<RcmConnector>();
        foreach (RcmConnector connector in profile.Connectors)
            connector.OrderedPins ??= new List<string>();
        profile.Pins ??= new List<RcmPinProfile>();
        foreach (RcmPinProfile pin in profile.Pins)
        {
            if (pin.Id == Guid.Empty)
                pin.Id = Guid.NewGuid();
            pin.VoltageRemoved ??= new RcmStateEvidence();
            pin.VoltageApplied24V ??= new RcmStateEvidence();
            pin.Comparison ??= new RcmStateComparison();
            pin.VerificationRuns ??= new List<RcmPhysicalVerificationRun>();
            pin.DecoderVerification ??= new RcmDecoderVerification();
            NormalizeEvidence(pin.VoltageRemoved);
            NormalizeEvidence(pin.VoltageApplied24V);
            NormalizeComparison(pin.Comparison);
            foreach (RcmPhysicalVerificationRun run in pin.VerificationRuns)
            {
                if (run.RunId == Guid.Empty)
                    run.RunId = Guid.NewGuid();
                run.ExpectedCcf ??= new RcmExpectedMappingSnapshot();
                run.VoltageApplied24V ??= new RcmStateEvidence();
                run.VoltageRemoved ??= new RcmStateEvidence();
                run.Comparison ??= new RcmStateComparison();
                run.CandidateTransitions ??= new List<RcmObservedTransition>();
                NormalizeEvidence(run.VoltageApplied24V);
                NormalizeEvidence(run.VoltageRemoved);
                NormalizeComparison(run.Comparison);
            }
            RcmDecoderVerification verification = pin.DecoderVerification;
            verification.ExpectedCcf ??= new RcmExpectedMappingSnapshot();
            verification.QualifyingRunIds ??= new List<Guid>();
            verification.ContradictoryRunIds ??= new List<Guid>();
            verification.AuditHistory ??= new List<RcmVerificationAuditEvent>();
            foreach (RcmVerificationAuditEvent audit in verification.AuditHistory)
                audit.RunIds ??= new List<Guid>();
            if (verification.RequiredRunCount < 2)
                verification.RequiredRunCount = profile.RequiredVerificationRuns;
            if (!RcmVerificationStates.Allowed.Contains(verification.Status))
                verification.Status = RcmVerificationStates.NotVerified;

            if ((!pin.VoltageRemoved.Tested || !pin.VoltageApplied24V.Tested) && pin.Comparison.ComparedAt is not null)
                pin.Comparison = new RcmStateComparison();
            if (!pin.PhysicalMappingAssigned)
                pin.RcmResult = RcmResultStates.Unassigned;
            else if (pin.Comparison.ComparedAt is null)
                pin.RcmResult = RcmCaptureWindowCoordinator.ResultForCapturedStates(pin);

            RcmMappingVerificationService.Evaluate(
                pin,
                profile.RequiredVerificationRuns,
                profile.LastModifiedTimestamp == default ? DateTimeOffset.UtcNow : profile.LastModifiedTimestamp);
        }

        foreach (string connector in profile.Pins.Select(pin => pin.Connector)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!profile.Connectors.Any(item => string.Equals(item.Name, connector, StringComparison.OrdinalIgnoreCase)))
                profile.Connectors.Add(new RcmConnector { Name = connector });
        }

        profile.SchemaVersion = RcmProfile.CurrentSchemaVersion;
    }

    private static void NormalizeEvidence(RcmStateEvidence evidence)
    {
        evidence.CompleteRawFrames ??= new List<RcmRawFrameEvidence>();
        evidence.FeatureFrequencies ??= new Dictionary<string, int>(StringComparer.Ordinal);
        evidence.CandidateStableFeatures ??= new Dictionary<string, int>(StringComparer.Ordinal);
        if (evidence.FrameCount == 0)
        {
            evidence.NoOtmrData |= evidence.Tested || evidence.CaptureStart.HasValue;
            evidence.Tested = false;
            evidence.FeatureFrequencies.Clear();
            evidence.CandidateStableFeatures.Clear();
            evidence.CandidateRawSignature = string.Empty;
        }
    }

    private static void NormalizeComparison(RcmStateComparison comparison)
    {
        comparison.CommonFeatures ??= new List<string>();
        comparison.UniqueFeaturesVoltageRemoved ??= new List<string>();
        comparison.UniqueFeaturesVoltageApplied24V ??= new List<string>();
        comparison.RepeatableDifferences ??= new List<string>();
        comparison.CandidateTransitionEvidence ??= new List<string>();
    }

    private static void Validate(RcmProfile profile)
    {
        if (!string.Equals(profile.SchemaVersion, RcmProfile.CurrentSchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported RCM schema version '{profile.SchemaVersion}'.");
        if (profile.Pins.GroupBy(pin => pin.Id).Any(group => group.Key == Guid.Empty || group.Count() > 1))
            throw new InvalidDataException("The RCM profile contains missing or duplicate stable input IDs.");
        if (profile.Connectors.Any(connector => string.IsNullOrWhiteSpace(connector.Name)) ||
            profile.Connectors.GroupBy(connector => connector.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("The RCM profile contains blank or duplicate connector names.");
        if (profile.Connectors.Any(connector => connector.OrderedPins.Any(string.IsNullOrWhiteSpace) ||
                                               connector.OrderedPins.Distinct(StringComparer.Ordinal).Count() != connector.OrderedPins.Count))
            throw new InvalidDataException("Connector ordered pin lists cannot contain blank or duplicate pins.");
        if (profile.RequiredVerificationRuns < 2)
            throw new InvalidDataException("RCM physical verification must require at least two complete runs.");

        foreach (RcmPinProfile pin in profile.Pins)
        {
            if (!RcmResultStates.Allowed.Contains(pin.RcmResult))
                throw new InvalidDataException($"Unsupported RCM result '{pin.RcmResult}' for {pin.DisplayKey}.");
            if (!RcmVerificationStates.Allowed.Contains(pin.DecoderVerification.Status))
                throw new InvalidDataException($"Unsupported decoder verification status for {pin.DisplayKey}.");
            if (pin.VerificationRuns.GroupBy(run => run.RunId).Any(group => group.Key == Guid.Empty || group.Count() > 1))
                throw new InvalidDataException($"Verification runs for {pin.DisplayKey} contain missing or duplicate IDs.");
            if (pin.Comparison.DecoderVerified &&
                (pin.DecoderVerification.Status != RcmVerificationStates.Verified ||
                 pin.DecoderVerification.VerifiedAt is null ||
                 pin.DecoderVerification.ObservedMapping is null))
                throw new InvalidDataException($"Verified decoder metadata is incomplete for {pin.DisplayKey}.");
            if (pin.VerificationRuns.SelectMany(run => run.CandidateTransitions).Any(transition =>
                    transition.RawPosition < 0 || transition.RemovedValue is < 0 or > 255 ||
                    transition.AppliedValue is < 0 or > 255 || transition.Bit is < 0 or > 7))
                throw new InvalidDataException($"Verification transitions contain invalid raw values for {pin.DisplayKey}.");
            if (pin.DecoderVerification.Status == RcmVerificationStates.Verified)
            {
                RcmDecoderVerification verification = pin.DecoderVerification;
                bool runIdsExist = verification.QualifyingRunIds.All(id =>
                    pin.VerificationRuns.Any(run => run.RunId == id));
                if (!string.Equals(verification.VerificationMethod, "physical stimulation", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(verification.Connector) || string.IsNullOrWhiteSpace(verification.Pin) ||
                    !verification.ExpectedCcf.IsComplete ||
                    verification.SuccessfulRepetitionCount < verification.RequiredRunCount ||
                    verification.QualifyingRunIds.Count < verification.RequiredRunCount || !runIdsExist)
                    throw new InvalidDataException($"Verified physical-stimulation metadata is incomplete for {pin.DisplayKey}.");
            }
        }
    }
}
