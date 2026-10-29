using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CcfEditor.Otmr.Server;
using CcfEditor.Otmr.Storage;
using CcfEditor.Otmr.Sync;
using Microsoft.Data.Sqlite;

namespace CcfEditor.Otmr.Server.Tests;

public sealed class OtmrServerIntegrationTests
{
    [Fact]
    public async Task ExistingWindowsHttpClient_AcceptsServerAcknowledgementWithoutContractChange()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient transport = factory.CreateClient();
        var windowsClient = new HttpOtmrSyncClient(transport, new OtmrSyncOptions
        {
            Enabled = true,
            BaseUrl = transport.BaseAddress,
            ApiToken = OtmrServerTestFactory.Token,
            AllowInsecureLocalhostForTests = true,
            RequestTimeout = TimeSpan.FromSeconds(10)
        });
        OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create();

        OtmrApiV1UploadAcknowledgement acknowledgement = await windowsClient.UploadSessionAsync(package);

        Assert.True(acknowledgement.Accepted);
        Assert.False(acknowledgement.AlreadyPresent);
        Assert.Equal(package.Session.SessionId, acknowledgement.SessionId);
        Assert.Equal(package.Manifest.ContentSha256, acknowledgement.ManifestSha256);
    }

    [Fact]
    public async Task ValidPackage_IsAtomicallyAccepted_AndEveryCollectionAndBlobIsPreserved()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create();

        HttpResponseMessage response = await UploadAsync(client, package);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        OtmrApiV1UploadAcknowledgement acknowledgement = OtmrApiV1Json.DeserializeAcknowledgement(await response.Content.ReadAsStringAsync());
        Assert.True(acknowledgement.Accepted);
        Assert.False(acknowledgement.AlreadyPresent);
        Assert.Equal(package.Manifest.ContentSha256, acknowledgement.ManifestSha256);
        Assert.Equal(package.Manifest.RawEntryCount, acknowledgement.RawEntryCount);
        Assert.Equal(package.Manifest.LiveFrameCount, acknowledgement.LiveFrameCount);
        Assert.Equal(package.Manifest.RcmCaptureFrameCount, acknowledgement.RcmCaptureFrameCount);

        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM recording_sessions;"));
        Assert.Equal(2, await ScalarLongAsync(database, "SELECT COUNT(*) FROM raw_serial_entries;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM live_frames;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM rcm_input_tests;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM rcm_capture_windows;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM rcm_capture_frames;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM rcm_comparisons;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM server_upload_receipts;"));
        Assert.Equal(package.RawEntries[1].Data, await ScalarBlobAsync(database, "SELECT data FROM raw_serial_entries WHERE sequence=2;"));
        Assert.Equal(package.LiveFrames[0].Data, await ScalarBlobAsync(database, "SELECT data FROM live_frames WHERE sequence=1;"));
        Assert.Equal(package.Rcm.CaptureFrames[0].Data, await ScalarBlobAsync(database, "SELECT data FROM rcm_capture_frames WHERE sequence=1;"));
        Assert.Equal("preserved notes", await ScalarTextAsync(database, "SELECT notes FROM recording_sessions;"));
        Assert.Equal("Throttle 1", await ScalarTextAsync(database, "SELECT function FROM rcm_input_tests;"));
        Assert.Equal("[\"bit 0\"]", await ScalarTextAsync(database, "SELECT candidate_transition_json FROM rcm_comparisons;"));
    }

    [Fact]
    public async Task ExactRetry_IsIdempotent_AndCreatesNoDuplicateRows()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, package)).StatusCode);

        HttpResponseMessage retry = await UploadAsync(client, package);

        OtmrApiV1UploadAcknowledgement acknowledgement = OtmrApiV1Json.DeserializeAcknowledgement(await retry.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.True(acknowledgement.Accepted);
        Assert.True(acknowledgement.AlreadyPresent);
        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM recording_sessions;"));
        Assert.Equal(2, await ScalarLongAsync(database, "SELECT COUNT(*) FROM raw_serial_entries;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM server_upload_receipts;"));
    }

    [Fact]
    public async Task ExistingSessionWithDifferentCanonicalEvidence_ReturnsConflict()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        OtmrApiV1UploadRequest original = OtmrServerPackageFixture.Create();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, original)).StatusCode);

        HttpResponseMessage response = await UploadAsync(client, OtmrServerPackageFixture.WithDifferentEvidence(original));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal("preserved notes", await ScalarTextAsync(database, "SELECT notes FROM recording_sessions;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM server_upload_receipts;"));
    }

    [Fact]
    public async Task BadManifestHash_IsRejectedWithoutPersistence()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create();
        package = package with { Manifest = package.Manifest with { ContentSha256 = new string('0', 64) } };

        HttpResponseMessage response = await UploadAsync(client, package);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM recording_sessions;"));
    }

    [Fact]
    public async Task BadManifestCount_IsRejectedWithoutPersistence()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create();
        package = package with { Manifest = package.Manifest with { RawEntryCount = 999 } };

        HttpResponseMessage response = await UploadAsync(client, package);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM recording_sessions;"));
    }

    [Fact]
    public async Task UnsupportedApiVersion_IsRejected()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create() with { ApiVersion = 2 };

        HttpResponseMessage response = await UploadAsync(client, package);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("UNSUPPORTED_API_VERSION", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingAndBadBearerTokens_ReturnUnauthorized_WhileValidTokenSucceeds()
    {
        using var factory = new OtmrServerTestFactory();
        OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create();
        using HttpClient missing = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await UploadAsync(missing, package)).StatusCode);

        using HttpClient bad = factory.CreateClient();
        bad.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await UploadAsync(bad, package)).StatusCode);

        using HttpClient valid = factory.CreateAuthenticatedClient();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(valid, package)).StatusCode);
    }

    [Fact]
    public async Task InjectedFailure_RollsBackEveryRowAndLeavesNoPartialSession()
    {
        using var factory = new OtmrServerTestFactory(failBeforeReceipt: true);
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await UploadAsync(client, OtmrServerPackageFixture.Create());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        foreach (string table in new[] { "recording_sessions", "raw_serial_entries", "live_frames", "rcm_input_tests", "rcm_capture_windows", "rcm_capture_frames", "rcm_comparisons", "server_upload_receipts" })
            Assert.Equal(0, await ScalarLongAsync(database, $"SELECT COUNT(*) FROM {table};"));
    }

    [Fact]
    public async Task VehicleSessionRecordAndConfigurationQueries_ReturnOnlyStoredHistoricalEvidence()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, package)).StatusCode);

        JsonElement vehicles = await GetJsonAsync(client, "/api/v1/otmr/vehicles");
        Assert.Equal("171001", vehicles[0].GetProperty("vehicleIdentifier").GetString());
        JsonElement sessions = await GetJsonAsync(client, "/api/v1/otmr/vehicles/171001/sessions");
        Assert.Equal(package.Session.SessionId, sessions[0].GetProperty("sessionId").GetGuid());
        Assert.Equal(2, sessions[0].GetProperty("rawEntryCount").GetInt64());

        string from = Uri.EscapeDataString(package.Session.StartedUtc.ToString("O"));
        string to = Uri.EscapeDataString(package.Session.StartedUtc.AddMinutes(1).ToString("O"));
        JsonElement records = await GetJsonAsync(client, $"/api/v1/otmr/vehicles/171001/records?fromUtc={from}&toUtc={to}");
        Assert.Equal(package.LiveFrames[0].Data, records[0].GetProperty("data").GetBytesFromBase64());
        JsonElement configuration = await GetJsonAsync(client, "/api/v1/otmr/vehicles/171001/configuration");
        Assert.Equal("ccf-hash", configuration.GetProperty("ccfSha256").GetString());
        Assert.Equal("{\"profileVersion\":1}", configuration.GetProperty("rcmProfileJsonSnapshot").GetString());
        JsonElement live = await GetJsonAsync(client, "/api/v1/otmr/vehicles/171001/live");
        Assert.False(live.GetProperty("liveAvailable").GetBoolean());
        Assert.Empty(live.GetProperty("signals").EnumerateArray());
    }

    [Fact]
    public async Task QueriesAreBoundedAndVehicleInputCannotBecomeSql()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, package)).StatusCode);

        JsonElement injected = await GetJsonAsync(client, "/api/v1/otmr/vehicles/%27%20OR%201%3D1--/sessions");
        Assert.Empty(injected.EnumerateArray());
        HttpResponseMessage missingRange = await client.GetAsync("/api/v1/otmr/vehicles/171001/records");
        Assert.Equal(HttpStatusCode.BadRequest, missingRange.StatusCode);
        string from = Uri.EscapeDataString(package.Session.StartedUtc.ToString("O"));
        string to = Uri.EscapeDataString(package.Session.StartedUtc.AddDays(32).ToString("O"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/otmr/vehicles/171001/records?fromUtc={from}&toUtc={to}")).StatusCode);
    }

    [Fact]
    public async Task HealthIsMinimalUnauthenticated_AndDatabaseUsesWal()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await response.Content.ReadAsStringAsync());

        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal("wal", await ScalarTextAsync(database, "PRAGMA journal_mode;"));
        Assert.Equal(OtmrServerDatabase.CurrentSchemaVersion, await ScalarLongAsync(database, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task DatabaseCanReopenAndRetainAcceptedSession()
    {
        string databasePath;
        Guid sessionId;
        using (var factory = new OtmrServerTestFactory())
        {
            databasePath = factory.DatabasePath;
            using HttpClient client = factory.CreateAuthenticatedClient();
            OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create();
            sessionId = package.Session.SessionId;
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, package)).StatusCode);
        }

        SqliteConnection.ClearAllPools();
        await using SqliteConnection reopened = await OpenDatabaseAsync(databasePath);
        Assert.Equal(sessionId.ToString("D"), await ScalarTextAsync(reopened, "SELECT session_id FROM recording_sessions;"));
    }

    [Fact]
    public async Task InvalidIdempotencyKey_IsRejected()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        OtmrApiV1UploadRequest package = OtmrServerPackageFixture.Create();
        using var request = new HttpRequestMessage(HttpMethod.Post, OtmrApiContract.AtomicSessionUploadRoute);
        request.Headers.TryAddWithoutValidation(OtmrServerSyncContract.IdempotencyHeader, Guid.NewGuid().ToString("D"));
        request.Content = JsonContent(package);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task GenuineRealtimePostMakesLiveAvailableAndLaterUpdateReplacesLatestSignal()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        const string vehicle = "999321";
        Guid signalId = Guid.NewGuid();

        JsonElement before = await GetJsonAsync(client, OtmrRealtimeContract.LiveRoute(vehicle));
        Assert.False(before.GetProperty("liveAvailable").GetBoolean());
        Assert.False(before.GetProperty("online").GetBoolean());
        Assert.Empty(before.GetProperty("signals").EnumerateArray());

        DateTimeOffset firstTimestamp = DateTimeOffset.UtcNow.AddSeconds(-2);
        OtmrRealtimeUpdateRequest first = RealtimeUpdate(
            vehicle, signalId, firstTimestamp, OtmrRealtimeContract.Active, rawValue: 1);
        HttpResponseMessage firstResponse = await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(vehicle), RealtimeJsonContent(first));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Guid appInstanceId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await HeartbeatAsync(
            client, appInstanceId, true, vehicle, first.SourceConnectionId)).StatusCode);

        JsonElement live = await GetJsonAsync(client, OtmrRealtimeContract.LiveRoute(vehicle));
        Assert.True(live.GetProperty("liveAvailable").GetBoolean());
        Assert.True(live.GetProperty("online").GetBoolean());
        Assert.False(live.GetProperty("isStale").GetBoolean());
        Assert.Equal(firstTimestamp, live.GetProperty("asOfUtc").GetDateTimeOffset());
        Assert.Equal("verified-profile.json", live.GetProperty("rcmProfileFilename").GetString());
        Assert.Equal(new string('C', 64), live.GetProperty("rcmProfileSha256").GetString());
        JsonElement signal = Assert.Single(live.GetProperty("signals").EnumerateArray());
        Assert.Equal(signalId, signal.GetProperty("signalId").GetGuid());
        Assert.Equal(OtmrRealtimeContract.Active, signal.GetProperty("state").GetString());
        Assert.Equal(1, signal.GetProperty("rawValue").GetInt32());
        Assert.Equal(OtmrRealtimeContract.Verified, signal.GetProperty("verification").GetString());

        DateTimeOffset secondTimestamp = firstTimestamp.AddSeconds(1);
        OtmrRealtimeUpdateRequest second = RealtimeUpdate(
            vehicle, signalId, secondTimestamp, OtmrRealtimeContract.Inactive, rawValue: 0,
            sessionStart: false);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(vehicle), RealtimeJsonContent(second))).StatusCode);

        JsonElement replaced = await GetJsonAsync(client, OtmrRealtimeContract.LiveRoute(vehicle));
        JsonElement replacedSignal = Assert.Single(replaced.GetProperty("signals").EnumerateArray());
        Assert.Equal(secondTimestamp, replaced.GetProperty("asOfUtc").GetDateTimeOffset());
        Assert.Equal(OtmrRealtimeContract.Inactive, replacedSignal.GetProperty("state").GetString());
        Assert.Equal(0, replacedSignal.GetProperty("rawValue").GetInt32());

        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM live_vehicle_state;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM live_signal_state;"));
        Assert.Equal(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM recording_sessions;"));
    }

    [Fact]
    public async Task NewSourceConnectionAtomicallyClearsOnlyCurrentLiveSignalsAndNeverResurrectsPriorGeneration()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        OtmrApiV1UploadRequest historical = OtmrServerPackageFixture.Create();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, historical)).StatusCode);

        const string vehicle = "999654";
        string connectionA = Guid.NewGuid().ToString("D");
        string connectionB = Guid.NewGuid().ToString("D");
        Guid throttle = Guid.NewGuid();
        Guid forward = Guid.NewGuid();
        DateTimeOffset startedA = DateTimeOffset.UtcNow.AddSeconds(-4);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(vehicle),
            RealtimeJsonContent(SessionStart(vehicle, connectionA, startedA)))).StatusCode);
        var sessionASignals = new[]
        {
            RealtimeSignal(throttle, "A", "Throttle 1", OtmrRealtimeContract.Active, 12),
            RealtimeSignal(forward, "D", "Forward", OtmrRealtimeContract.Active, 1)
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(vehicle),
            RealtimeJsonContent(new OtmrRealtimeUpdateRequest(
                OtmrApiContract.Version, vehicle, startedA.AddSeconds(1), connectionA,
                "verified-profile.json", new string('C', 64), sessionASignals)))).StatusCode);
        Guid appInstanceId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await HeartbeatAsync(
            client, appInstanceId, true, vehicle, connectionA)).StatusCode);

        JsonElement liveA = await GetJsonAsync(client, OtmrRealtimeContract.LiveRoute(vehicle));
        Assert.Equal(appInstanceId, liveA.GetProperty("windowsAppInstanceId").GetGuid());
        Assert.Equal(connectionA, liveA.GetProperty("sourceConnectionId").GetString());
        Assert.Equal(2, liveA.GetProperty("signals").GetArrayLength());
        Assert.All(liveA.GetProperty("signals").EnumerateArray(), signal =>
            Assert.Equal(OtmrRealtimeContract.Active, signal.GetProperty("state").GetString()));

        DateTimeOffset startedB = startedA.AddSeconds(2);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(vehicle),
            RealtimeJsonContent(SessionStart(vehicle, connectionB, startedB)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await HeartbeatAsync(
            client, appInstanceId, true, vehicle, connectionB)).StatusCode);

        JsonElement emptyB = await GetJsonAsync(client, OtmrRealtimeContract.LiveRoute(vehicle));
        Assert.True(emptyB.GetProperty("liveAvailable").GetBoolean());
        Assert.True(emptyB.GetProperty("online").GetBoolean());
        Assert.Equal(appInstanceId, emptyB.GetProperty("windowsAppInstanceId").GetGuid());
        Assert.Equal(connectionB, emptyB.GetProperty("sourceConnectionId").GetString());
        Assert.Equal(startedB, emptyB.GetProperty("asOfUtc").GetDateTimeOffset());
        Assert.Empty(emptyB.GetProperty("signals").EnumerateArray());
        Assert.False(emptyB.TryGetProperty("favorites", out _));

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(vehicle),
            RealtimeJsonContent(SessionStart(vehicle, connectionA, startedA)))).StatusCode);

        OtmrRealtimeUpdateRequest lateA = RealtimeUpdate(
            vehicle, forward, startedB.AddSeconds(1), OtmrRealtimeContract.Inactive, 0,
            connectionA, sessionStart: false);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(vehicle), RealtimeJsonContent(lateA))).StatusCode);

        OtmrRealtimeUpdateRequest throttleB = RealtimeUpdate(
            vehicle, throttle, startedB.AddSeconds(2), OtmrRealtimeContract.Inactive, 0,
            connectionB, sessionStart: false);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(vehicle), RealtimeJsonContent(throttleB))).StatusCode);

        JsonElement updatedB = await GetJsonAsync(client, OtmrRealtimeContract.LiveRoute(vehicle));
        Assert.Equal(connectionB, updatedB.GetProperty("sourceConnectionId").GetString());
        JsonElement onlyCurrentThrottle = Assert.Single(updatedB.GetProperty("signals").EnumerateArray());
        Assert.Equal(throttle, onlyCurrentThrottle.GetProperty("signalId").GetGuid());
        Assert.Equal(OtmrRealtimeContract.Inactive, onlyCurrentThrottle.GetProperty("state").GetString());
        Assert.DoesNotContain(updatedB.GetProperty("signals").EnumerateArray(), signal =>
            signal.GetProperty("signalId").GetGuid() == forward);

        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal(connectionB, await ScalarTextAsync(database,
            "SELECT source_connection_id FROM live_vehicle_state WHERE vehicle_identifier='999654';"));
        Assert.Equal(1, await ScalarLongAsync(database,
            "SELECT COUNT(*) FROM live_signal_state WHERE vehicle_identifier='999654';"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM recording_sessions;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM server_upload_receipts;"));
    }

    [Fact]
    public async Task OneRealtimePostAtomicallyStoresAndReturnsAllThreeVerifiedSignals()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        const string vehicle = "999753";
        string sourceConnectionId = Guid.NewGuid().ToString("D");
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddSeconds(-1);
        var signals = new[]
        {
            RealtimeSignal(Guid.NewGuid(), "A", "Throttle 1", OtmrRealtimeContract.Active, 12),
            RealtimeSignal(Guid.NewGuid(), "B", "Throttle 2", OtmrRealtimeContract.Active, 13),
            RealtimeSignal(Guid.NewGuid(), "D", "Forward", OtmrRealtimeContract.Inactive, 3)
        };

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(vehicle),
            RealtimeJsonContent(SessionStart(vehicle, sourceConnectionId, timestamp.AddMilliseconds(-1)))))
            .StatusCode);
        var update = new OtmrRealtimeUpdateRequest(
            OtmrApiContract.Version,
            vehicle,
            timestamp,
            sourceConnectionId,
            "three-signals.json",
            new string('D', 64),
            signals,
            IsSessionStart: false);

        HttpResponseMessage response = await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(vehicle), RealtimeJsonContent(update));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await HeartbeatAsync(
            client, Guid.NewGuid(), true, vehicle, sourceConnectionId)).StatusCode);
        JsonElement live = await GetJsonAsync(client, OtmrRealtimeContract.LiveRoute(vehicle));
        Assert.Equal(sourceConnectionId, live.GetProperty("sourceConnectionId").GetString());
        Assert.Equal(timestamp, live.GetProperty("asOfUtc").GetDateTimeOffset());
        Assert.Equal(3, live.GetProperty("signals").GetArrayLength());
        Assert.Equal(
            new[] { "A:ACTIVE:12", "B:ACTIVE:13", "D:INACTIVE:3" },
            live.GetProperty("signals").EnumerateArray()
                .Select(signal =>
                    $"{signal.GetProperty("pin").GetString()}:" +
                    $"{signal.GetProperty("state").GetString()}:" +
                    $"{signal.GetProperty("rawValue").GetInt32()}")
                .ToArray());

        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal(1, await ScalarLongAsync(database,
            "SELECT COUNT(*) FROM live_vehicle_state WHERE vehicle_identifier='999753';"));
        Assert.Equal(3, await ScalarLongAsync(database,
            "SELECT COUNT(*) FROM live_signal_state WHERE vehicle_identifier='999753';"));
        Assert.Equal(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM recording_sessions;"));
    }

    [Fact]
    public async Task FreshHeartbeatKeepsEventDrivenStateCurrent_ThenTimeoutSuppressesItWithoutDeletingIt()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient client = factory.CreateAuthenticatedClient();
        DateTimeOffset oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-2);
        OtmrRealtimeUpdateRequest update = RealtimeUpdate(
            "800010", Guid.NewGuid(), oldTimestamp, OtmrRealtimeContract.Active, rawValue: 1);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            OtmrRealtimeContract.LiveRoute(update.VehicleIdentifier), RealtimeJsonContent(update))).StatusCode);
        Guid appInstanceId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await HeartbeatAsync(
            client, appInstanceId, true, update.VehicleIdentifier, update.SourceConnectionId)).StatusCode);

        JsonElement live = await GetJsonAsync(client, OtmrRealtimeContract.LiveRoute(update.VehicleIdentifier));
        Assert.True(live.GetProperty("liveAvailable").GetBoolean());
        Assert.False(live.GetProperty("isStale").GetBoolean());
        Assert.True(live.GetProperty("online").GetBoolean());
        Assert.True(live.GetProperty("windowsAppOnline").GetBoolean());
        Assert.Equal(30, live.GetProperty("staleAfterSeconds").GetInt32());
        Assert.Equal(oldTimestamp, live.GetProperty("asOfUtc").GetDateTimeOffset());
        Assert.Equal(OtmrRealtimeContract.Active,
            Assert.Single(live.GetProperty("signals").EnumerateArray()).GetProperty("state").GetString());

        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        await ExecuteAsync(database,
            "UPDATE windows_app_presence SET last_heartbeat_utc='2000-01-01T00:00:00.0000000+00:00';");
        JsonElement offline = await GetJsonAsync(client, OtmrRealtimeContract.LiveRoute(update.VehicleIdentifier));
        Assert.False(offline.GetProperty("windowsAppOnline").GetBoolean());
        Assert.False(offline.GetProperty("liveAvailable").GetBoolean());
        Assert.False(offline.GetProperty("online").GetBoolean());
        Assert.True(offline.GetProperty("isStale").GetBoolean());
        Assert.Empty(offline.GetProperty("signals").EnumerateArray());
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM live_signal_state;"));
    }

    [Fact]
    public async Task RealtimeRequiresBearerAndVerifiedDataAndDoesNotAlterHistoricalUpload()
    {
        using var factory = new OtmrServerTestFactory();
        OtmrRealtimeUpdateRequest update = RealtimeUpdate(
            "700777", Guid.NewGuid(), DateTimeOffset.UtcNow, OtmrRealtimeContract.Active, rawValue: 1);
        using HttpClient anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(
            OtmrRealtimeContract.LiveRoute(update.VehicleIdentifier), RealtimeJsonContent(update))).StatusCode);

        using HttpClient authenticated = factory.CreateAuthenticatedClient();
        OtmrRealtimeUpdateRequest missingConnection = update with { SourceConnectionId = null };
        Assert.Equal(HttpStatusCode.BadRequest, (await authenticated.PostAsync(
            OtmrRealtimeContract.LiveRoute(update.VehicleIdentifier),
            RealtimeJsonContent(missingConnection))).StatusCode);
        OtmrRealtimeUpdateRequest candidate = update with
        {
            Signals = update.Signals.Select(signal => signal with { Verification = "CANDIDATE" }).ToArray()
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await authenticated.PostAsync(
            OtmrRealtimeContract.LiveRoute(update.VehicleIdentifier), RealtimeJsonContent(candidate))).StatusCode);

        Assert.Equal(OtmrApiContract.AtomicSessionUploadRoute, "/api/v1/otmr/recording-sessions");
        OtmrApiV1UploadRequest historical = OtmrServerPackageFixture.Create();
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(authenticated, historical)).StatusCode);
        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM recording_sessions;"));
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM server_upload_receipts;"));
        Assert.Equal(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM live_vehicle_state;"));
    }

    [Fact]
    public async Task HeartbeatExposesWindowsPresenceWithoutClaimingOtmrLiveOrCreatingHistory()
    {
        using var factory = new OtmrServerTestFactory();
        using HttpClient anonymous = factory.CreateClient();
        Guid appA = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await HeartbeatAsync(
            anonymous, appA, false, null, null)).StatusCode);

        using HttpClient client = factory.CreateAuthenticatedClient();
        Assert.Equal(HttpStatusCode.OK, (await HeartbeatAsync(
            client, appA, false, null, null, "0.3.0")).StatusCode);

        JsonElement runningDisconnected = await GetJsonAsync(
            client, OtmrRealtimeContract.LiveRoute("171804"));
        Assert.True(runningDisconnected.GetProperty("windowsAppOnline").GetBoolean());
        Assert.Equal(appA, runningDisconnected.GetProperty("windowsAppInstanceId").GetGuid());
        Assert.Equal("0.3.0", runningDisconnected.GetProperty("windowsAppVersion").GetString());
        Assert.NotEqual(default, runningDisconnected.GetProperty("windowsAppLastSeenUtc").GetDateTimeOffset());
        Assert.Equal(30, runningDisconnected.GetProperty("windowsAppOfflineAfterSeconds").GetInt32());
        Assert.False(runningDisconnected.GetProperty("liveAvailable").GetBoolean());
        Assert.False(runningDisconnected.GetProperty("online").GetBoolean());
        Assert.Empty(runningDisconnected.GetProperty("signals").EnumerateArray());

        Guid appB = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await HeartbeatAsync(
            client, appB, false, null, null, "0.3.0")).StatusCode);
        JsonElement restarted = await GetJsonAsync(client, OtmrRealtimeContract.LiveRoute("171804"));
        Assert.Equal(appB, restarted.GetProperty("windowsAppInstanceId").GetGuid());
        Assert.NotEqual(appA, appB);

        await using SqliteConnection database = await OpenDatabaseAsync(factory.DatabasePath);
        Assert.Equal(1, await ScalarLongAsync(database, "SELECT COUNT(*) FROM windows_app_presence;"));
        Assert.Equal(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM recording_sessions;"));
        Assert.Equal(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM server_upload_receipts;"));
        string schema = await ScalarTextAsync(database,
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='windows_app_presence';");
        Assert.DoesNotContain("token", schema, StringComparison.OrdinalIgnoreCase);
    }

    private static OtmrRealtimeUpdateRequest RealtimeUpdate(
        string vehicleIdentifier,
        Guid signalId,
        DateTimeOffset timestampUtc,
        string state,
        int rawValue,
        string sourceConnectionId = "source-session",
        bool sessionStart = true) => new(
            OtmrApiContract.Version,
            vehicleIdentifier,
            timestampUtc,
            sourceConnectionId,
            "verified-profile.json",
            new string('C', 64),
            new[]
            {
                new OtmrRealtimeSignalUpdate(
                    signalId, "J1", "A", "Throttle 1", 0, 0,
                    state, rawValue, rawValue, OtmrRealtimeContract.Verified)
            },
            sessionStart);

    private static OtmrRealtimeUpdateRequest SessionStart(
        string vehicleIdentifier,
        string sourceConnectionId,
        DateTimeOffset timestampUtc) => new(
            OtmrApiContract.Version,
            vehicleIdentifier,
            timestampUtc,
            sourceConnectionId,
            "verified-profile.json",
            new string('C', 64),
            Array.Empty<OtmrRealtimeSignalUpdate>(),
            IsSessionStart: true);

    private static OtmrRealtimeSignalUpdate RealtimeSignal(
        Guid signalId,
        string pin,
        string function,
        string state,
        int rawValue) => new(
            signalId, "J1", pin, function, 0, 0,
            state, rawValue, null, OtmrRealtimeContract.Verified);

    private static StringContent RealtimeJsonContent(OtmrRealtimeUpdateRequest update) =>
        new(JsonSerializer.Serialize(update, OtmrApiV1Json.Options), Encoding.UTF8, "application/json");

    private static Task<HttpResponseMessage> HeartbeatAsync(
        HttpClient client,
        Guid appInstanceId,
        bool otmrLiveConnected,
        string? vehicleIdentifier,
        string? sourceConnectionId,
        string applicationVersion = "0.3.0")
    {
        var heartbeat = new OtmrApplicationHeartbeatRequest(
            OtmrApiContract.Version,
            appInstanceId,
            applicationVersion,
            DateTimeOffset.UtcNow,
            otmrLiveConnected,
            vehicleIdentifier,
            sourceConnectionId);
        return client.PostAsync(
            OtmrApplicationHeartbeatContract.Route,
            new StringContent(
                JsonSerializer.Serialize(heartbeat, OtmrApiV1Json.Options),
                Encoding.UTF8,
                "application/json"));
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, OtmrApiV1UploadRequest package)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, OtmrApiContract.AtomicSessionUploadRoute);
        request.Headers.TryAddWithoutValidation(OtmrServerSyncContract.IdempotencyHeader, package.Session.SessionId.ToString("D"));
        request.Content = JsonContent(package);
        return await client.SendAsync(request);
    }

    private static StringContent JsonContent(OtmrApiV1UploadRequest package) =>
        new(OtmrApiV1Json.Serialize(package), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync(path));
        return document.RootElement.Clone();
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarTextAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }

    private static async Task<byte[]> ScalarBlobAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        return (byte[])(await command.ExecuteScalarAsync())!;
    }
}
