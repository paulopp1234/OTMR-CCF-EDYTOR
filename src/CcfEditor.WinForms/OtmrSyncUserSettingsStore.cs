using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CcfEditor.Otmr.Sync;

namespace CcfEditor.WinForms;

public sealed class OtmrSyncUserSettings
{
    public string ServerUrl { get; init; } = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer;
    public string ApiToken { get; init; } = string.Empty;
    public bool SyncEnabled { get; init; }
    public bool RealtimePublishingEnabled { get; init; }
    public bool AllowInsecureKnownTestServer { get; init; }
    public bool HasApiToken => !string.IsNullOrEmpty(ApiToken);

    public OtmrManualSyncConfiguration ToManualConfiguration() => new()
    {
        ServerUrl = ServerUrl,
        ApiToken = ApiToken,
        SyncEnabled = SyncEnabled,
        AllowInsecureKnownTestServer = AllowInsecureKnownTestServer
    };

    public OtmrRealtimePublisherConfiguration ToRealtimeConfiguration() => new()
    {
        ServerUrl = ServerUrl,
        ApiToken = ApiToken,
        Enabled = RealtimePublishingEnabled,
        AllowInsecureKnownTestServer = AllowInsecureKnownTestServer
    };

    public OtmrApplicationHeartbeatConfiguration ToHeartbeatConfiguration() => new()
    {
        ServerUrl = ServerUrl,
        ApiToken = ApiToken,
        Enabled = SyncEnabled,
        AllowInsecureKnownTestServer = AllowInsecureKnownTestServer
    };

    public override string ToString() =>
        $"ServerUrl={ServerUrl}; SyncEnabled={SyncEnabled}; RealtimePublishingEnabled={RealtimePublishingEnabled}; " +
        $"AllowInsecureKnownTestServer={AllowInsecureKnownTestServer}; ApiToken=[REDACTED]";
}

/// <summary>
/// Stores non-secret sync preferences in the current user's LocalApplicationData
/// directory and protects the API token with Windows DPAPI CurrentUser scope.
/// </summary>
public sealed class OtmrSyncUserSettingsStore
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("OTMR-CCF-Editor/manual-sync/v1");
    private readonly string _settingsPath;

    public OtmrSyncUserSettingsStore(string? settingsPath = null)
    {
        _settingsPath = Path.GetFullPath(settingsPath ?? DefaultSettingsPath);
    }

    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OTMR CCF Editor",
        "sync-settings.json");

    public string SettingsPath => _settingsPath;

    public OtmrSyncUserSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new OtmrSyncUserSettings();

        PersistedSettings? persisted = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(_settingsPath));
        if (persisted is null)
            throw new InvalidDataException("The user-local OTMR synchronization settings are empty.");

        string token = string.Empty;
        if (!string.IsNullOrWhiteSpace(persisted.ProtectedApiToken))
        {
            byte[] protectedBytes = Convert.FromBase64String(persisted.ProtectedApiToken);
            byte[] clearBytes = ProtectedData.Unprotect(protectedBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
            token = Encoding.UTF8.GetString(clearBytes);
            CryptographicOperations.ZeroMemory(clearBytes);
        }

        return new OtmrSyncUserSettings
        {
            ServerUrl = string.IsNullOrWhiteSpace(persisted.ServerUrl)
                ? OtmrSyncOptions.KnownInsecureDigitalOceanTestServer
                : persisted.ServerUrl,
            ApiToken = token,
            SyncEnabled = persisted.SyncEnabled,
            RealtimePublishingEnabled = persisted.RealtimePublishingEnabled,
            AllowInsecureKnownTestServer = persisted.AllowInsecureKnownTestServer
        };
    }

    public void Save(OtmrSyncUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? protectedToken = null;
        if (!string.IsNullOrEmpty(settings.ApiToken))
        {
            byte[] clearBytes = Encoding.UTF8.GetBytes(settings.ApiToken);
            try
            {
                byte[] protectedBytes = ProtectedData.Protect(clearBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
                protectedToken = Convert.ToBase64String(protectedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }

        var persisted = new PersistedSettings
        {
            ServerUrl = settings.ServerUrl,
            SyncEnabled = settings.SyncEnabled,
            RealtimePublishingEnabled = settings.RealtimePublishingEnabled,
            AllowInsecureKnownTestServer = settings.AllowInsecureKnownTestServer,
            ProtectedApiToken = protectedToken
        };
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    private sealed class PersistedSettings
    {
        public string ServerUrl { get; set; } = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer;
        public bool SyncEnabled { get; set; }
        public bool RealtimePublishingEnabled { get; set; }
        public bool AllowInsecureKnownTestServer { get; set; }
        public string? ProtectedApiToken { get; set; }
    }
}
