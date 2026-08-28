using System.Net;
using System.Reflection;
using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Storage;
using CcfEditor.Otmr.Sync;
using CcfEditor.WinForms;
using Microsoft.Data.Sqlite;

namespace CcfEditor.Tests;

public sealed class OtmrManualSyncTests
{
    [Fact]
    public async Task TestServerCallsAnonymousHealthAndReportsServerOk()
    {
        HttpRequestMessage? captured = null;
        using var http = new HttpClient(new DelegateHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"healthy\"}")
            });
        }));
        await WithStoreAsync(async store =>
        {
            var service = new OtmrManualSyncService(store, http);
            OtmrServerConnectivityResult result = await service.TestServerAsync(TestConfiguration());

            Assert.True(result.IsAvailable);
            Assert.Equal("SERVER OK", result.Message);
            Assert.Equal("http://104.248.226.215/health", captured!.RequestUri!.AbsoluteUri);
            Assert.Null(captured.Headers.Authorization);
        });
    }

    [Fact]
    public async Task PendingUploadCountIncludesPendingAndFailedWork()
    {
        await WithStoreAsync(async store =>
        {
            await RecordCompletedSessionAsync(store);
            await RecordCompletedSessionAsync(store);
            using var http = new HttpClient(new DelegateHandler((_, _) => throw new InvalidOperationException()));
            var service = new OtmrManualSyncService(store, http);

            OtmrManualSyncStatus status = await service.GetStatusAsync();

            Assert.Equal(2, status.PendingUploadCount);
            Assert.Equal(OtmrSyncStates.PendingUpload, status.CurrentSessionSyncState);
        });
    }

    [Fact]
    public async Task ManualSyncInvokesExistingCoordinatorAndMarksSessionUploaded()
    {
        await WithStoreAsync(async store =>
        {
            Guid sessionId = await RecordCompletedSessionAsync(store);
            int uploadCalls = 0;
            var client = new DelegateSyncClient((package, _) =>
            {
                uploadCalls++;
                return Task.FromResult(Acknowledgement(package));
            });
            using var http = new HttpClient(new DelegateHandler((_, _) => throw new InvalidOperationException()));
            var service = new OtmrManualSyncService(store, http, _ => client);

            OtmrSyncItemResult result = Assert.Single(await service.SynchronizePendingAsync(TestConfiguration()));
            OtmrManualSyncStatus status = await service.GetStatusAsync();

            Assert.Equal(1, uploadCalls);
            Assert.True(result.Uploaded);
            Assert.Equal(0, status.PendingUploadCount);
            Assert.Equal(OtmrSyncStates.Uploaded, status.CurrentSessionSyncState);
            Assert.Equal(OtmrSyncStates.Uploaded, await SessionStateAsync(store.DatabasePath, sessionId));
            Assert.Equal(1, await SessionCountAsync(store.DatabasePath, sessionId));
            Assert.Equal(1, await RawEntryCountAsync(store.DatabasePath, sessionId));
        });
    }

    [Fact]
    public async Task FailedManualSyncRemainsRetryableAndRedactsTokenFromStatus()
    {
        await WithStoreAsync(async store =>
        {
            Guid sessionId = await RecordCompletedSessionAsync(store);
            var client = new DelegateSyncClient((_, _) =>
                throw new InvalidOperationException("request failed for token unit-test-token"));
            using var http = new HttpClient(new DelegateHandler((_, _) => throw new InvalidOperationException()));
            var service = new OtmrManualSyncService(store, http, _ => client);

            OtmrSyncItemResult result = Assert.Single(await service.SynchronizePendingAsync(TestConfiguration()));
            OtmrManualSyncStatus status = await service.GetStatusAsync();

            Assert.False(result.Uploaded);
            Assert.Equal(1, status.PendingUploadCount);
            Assert.Equal(OtmrSyncStates.UploadFailed, status.CurrentSessionSyncState);
            Assert.Equal(OtmrSyncStates.UploadFailed, await SessionStateAsync(store.DatabasePath, sessionId));
            Assert.DoesNotContain("unit-test-token", status.LastError ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", status.LastError);
            Assert.DoesNotContain("unit-test-token", await LastOutboxErrorAsync(store.DatabasePath, sessionId), StringComparison.Ordinal);

            var refreshedService = new OtmrManualSyncService(store, http, _ => client);
            OtmrManualSyncStatus persistedStatus = await refreshedService.GetStatusAsync();
            Assert.NotNull(persistedStatus.LastSyncAttemptUtc);
            Assert.Contains("[REDACTED]", persistedStatus.LastError);
        });
    }

    [Fact]
    public async Task ConstructionAndLocalStatusRefreshCauseNoNetworkOrUpload()
    {
        int httpCalls = 0;
        int clientCreations = 0;
        using var http = new HttpClient(new DelegateHandler((_, _) =>
        {
            httpCalls++;
            throw new InvalidOperationException();
        }));
        await WithStoreAsync(async store =>
        {
            await RecordCompletedSessionAsync(store);
            var service = new OtmrManualSyncService(store, http, _ =>
            {
                clientCreations++;
                return new DelegateSyncClient((_, _) => throw new InvalidOperationException());
            });

            OtmrManualSyncStatus status = await service.GetStatusAsync();

            Assert.Equal(1, status.PendingUploadCount);
            Assert.Equal(0, httpCalls);
            Assert.Equal(0, clientCreations);
            Assert.Null(status.LastSyncAttemptUtc);
        });
    }

    [Fact]
    public void ManualSyncComponentsContainNoBackgroundTimer()
    {
        Type[] types = [typeof(OtmrManualSyncService), typeof(OtmrServerSyncControl)];
        foreach (Type type in types)
        {
            FieldInfo[] timerFields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(field => typeof(System.Threading.Timer).IsAssignableFrom(field.FieldType) ||
                                typeof(System.Windows.Forms.Timer).IsAssignableFrom(field.FieldType))
                .ToArray();
            Assert.Empty(timerFields);
        }
    }

    [Fact]
    public void HttpRequiresExplicitKnownTestServerOptInWhileProductionHttpsRemainsAllowed()
    {
        Assert.Throws<InvalidOperationException>(() => new OtmrManualSyncConfiguration
        {
            ServerUrl = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer,
            ApiToken = "placeholder",
            SyncEnabled = true
        }.CreateClientOptions());

        OtmrSyncOptions test = TestConfiguration().CreateClientOptions();
        Assert.True(test.AllowInsecureKnownTestServer);

        Assert.Throws<InvalidOperationException>(() => new OtmrManualSyncConfiguration
        {
            ServerUrl = "http://example.com",
            ApiToken = "placeholder",
            SyncEnabled = true,
            AllowInsecureKnownTestServer = true
        }.CreateClientOptions());

        new OtmrManualSyncConfiguration
        {
            ServerUrl = "https://otmr.example.com",
            ApiToken = "placeholder",
            SyncEnabled = true
        }.CreateClientOptions().Validate();
    }

    [Fact]
    public void UserLocalSettingsProtectTokenAndTokenModelsAreRedacted()
    {
        string folder = Path.Combine(Path.GetTempPath(), "otmr-sync-settings-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "sync-settings.json");
        const string token = "test-token-must-not-be-plaintext";
        try
        {
            var store = new OtmrSyncUserSettingsStore(path);
            var settings = new OtmrSyncUserSettings
            {
                ServerUrl = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer,
                ApiToken = token,
                SyncEnabled = true,
                RealtimePublishingEnabled = true,
                AllowInsecureKnownTestServer = true
            };

            store.Save(settings);
            string persisted = File.ReadAllText(path);
            OtmrSyncUserSettings loaded = store.Load();

            Assert.DoesNotContain(token, persisted, StringComparison.Ordinal);
            Assert.DoesNotContain(token, settings.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(token, settings.ToManualConfiguration().ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(token, settings.ToRealtimeConfiguration().ToString(), StringComparison.Ordinal);
            Assert.Equal(token, loaded.ApiToken);
            Assert.True(loaded.RealtimePublishingEnabled);
            Assert.Equal(token, loaded.ToRealtimeConfiguration().ApiToken);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SyncTokenTextBoxIsMaskedAndDoesNotDisplaySavedToken()
    {
        using var control = new OtmrServerSyncControl();
        var field = typeof(OtmrServerSyncControl).GetField("syncApiTokenTextBox", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var textBox = Assert.IsType<TextBox>(field.GetValue(control));
        Assert.True(textBox.UseSystemPasswordChar);
        Assert.Equal(string.Empty, textBox.Text);
    }

    private static OtmrManualSyncConfiguration TestConfiguration() => new()
    {
        ServerUrl = "http://104.248.226.215",
        ApiToken = "unit-test-token",
        SyncEnabled = true,
        AllowInsecureKnownTestServer = true,
        RequestTimeout = TimeSpan.FromSeconds(5)
    };

    private static OtmrApiV1UploadAcknowledgement Acknowledgement(OtmrApiV1UploadRequest package) => new(
        OtmrApiContract.Version, package.Session.SessionId, package.Session.SessionId.ToString("D"), true, false,
        package.Manifest.ContentSha256, package.Manifest.RawEntryCount, package.Manifest.LiveFrameCount,
        package.Manifest.RcmInputCount, package.Manifest.RcmCaptureCount, package.Manifest.RcmCaptureFrameCount,
        package.Manifest.RcmComparisonCount);

    private static async Task<Guid> RecordCompletedSessionAsync(SqliteOtmrRecordingStore store)
    {
        Guid sessionId = await store.StartSessionAsync(new OtmrRecordingSessionContext
        {
            SoftwareVersion = "manual-sync-test",
            ComPort = "COM2",
            SerialSettings = "38400/8/N/1",
            VehicleIdentifier = "171001"
        });
        store.TryRecordRaw(new OtmrCaptureEntry(
            DateTimeOffset.UtcNow,
            OtmrDirection.Rx,
            new byte[] { 0x01, 0x13, 0x00, 0xFF },
            "manual sync evidence"));
        await store.StopSessionAsync(DateTimeOffset.UtcNow);
        return sessionId;
    }

    private static async Task WithStoreAsync(Func<SqliteOtmrRecordingStore, Task> action)
    {
        string folder = Path.Combine(Path.GetTempPath(), "otmr-manual-sync-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new SqliteOtmrRecordingStore(Path.Combine(folder, "OTMR_RCM.db"));
            await store.InitializeAsync();
            await action(store);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<string> SessionStateAsync(string path, Guid sessionId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sync_state FROM recording_sessions WHERE session_id=$sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> SessionCountAsync(string path, Guid sessionId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM recording_sessions WHERE session_id=$sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> RawEntryCountAsync(string path, Guid sessionId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM raw_serial_entries WHERE session_id=$sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> LastOutboxErrorAsync(string path, Guid sessionId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT last_error FROM sync_outbox WHERE session_id=$sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private sealed class DelegateSyncClient(
        Func<OtmrApiV1UploadRequest, CancellationToken, Task<OtmrApiV1UploadAcknowledgement>> callback)
        : IOtmrSyncClient
    {
        public Task<OtmrApiV1UploadAcknowledgement> UploadSessionAsync(
            OtmrApiV1UploadRequest package,
            CancellationToken cancellationToken = default) => callback(package, cancellationToken);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }
}
