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
        if (profile.SchemaVersion is not ("1.0" or "1.1" or RcmProfile.CurrentSchemaVersion))
            throw new InvalidDataException($"Unsupported RCM schema version '{profile.SchemaVersion}'.");

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
            NormalizeEvidence(pin.VoltageRemoved);
            NormalizeEvidence(pin.VoltageApplied24V);

            if ((!pin.VoltageRemoved.Tested || !pin.VoltageApplied24V.Tested) && pin.Comparison.ComparedAt is not null)
                pin.Comparison = new RcmStateComparison();
            if (!pin.PhysicalMappingAssigned)
                pin.RcmResult = RcmResultStates.Unassigned;
            else if (pin.Comparison.ComparedAt is null)
                pin.RcmResult = RcmCaptureWindowCoordinator.ResultForCapturedStates(pin);
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

        foreach (RcmPinProfile pin in profile.Pins)
        {
            if (!RcmResultStates.Allowed.Contains(pin.RcmResult))
                throw new InvalidDataException($"Unsupported RCM result '{pin.RcmResult}' for {pin.DisplayKey}.");
            if (pin.Comparison.DecoderVerified)
                throw new InvalidDataException("This RCM schema version cannot mark a semantic decoder as verified.");
        }
    }
}
