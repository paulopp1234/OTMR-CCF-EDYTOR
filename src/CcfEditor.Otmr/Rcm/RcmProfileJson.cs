using System.Text.Json;

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
        Validate(profile);
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
        Validate(profile);
        return profile;
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

    private static void Validate(RcmProfile profile)
    {
        if (!string.Equals(profile.SchemaVersion, RcmProfile.CurrentSchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported RCM schema version '{profile.SchemaVersion}'.");
        if (profile.Pins.GroupBy(pin => pin.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidDataException("The RCM profile contains duplicate physical pin entries.");

        foreach (RcmPinProfile pin in profile.Pins)
        {
            if (!RcmResultStates.Allowed.Contains(pin.RcmResult))
                throw new InvalidDataException($"Unsupported RCM result '{pin.RcmResult}' for {pin.Key}.");
            if (pin.Comparison.DecoderVerified)
                throw new InvalidDataException("This RCM schema version cannot mark a semantic decoder as verified.");
        }
    }
}
