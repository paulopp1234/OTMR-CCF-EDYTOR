using System.Net;
using System.Text;
using System.Text.Json;
using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;
using CcfEditor.Otmr.Storage;
using CcfEditor.Otmr.Sync;
using Microsoft.Data.Sqlite;

namespace CcfEditor.Tests;

public sealed class OtmrSyncProtocolTests
{
    [Fact]
    public async Task ApiV1Package_IsDeterministicTypedAndPreservesAllEvidence()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            OtmrApiV1UploadRequest first = await store.BuildApiV1UploadPackageAsync(sessionId);
            OtmrApiV1UploadRequest second = await store.BuildApiV1UploadPackageAsync(sessionId);
            string firstJson = OtmrApiV1Json.Serialize(first);
            string secondJson = OtmrApiV1Json.Serialize(second);

            Assert.Equal(OtmrApiContract.Version, first.ApiVersion);
            Assert.Equal(firstJson, secondJson);
            Assert.Equal(first.Manifest.ContentSha256, second.Manifest.ContentSha256);
            Assert.Equal(sessionId, first.Session.SessionId);
            Assert.NotEqual(default, first.Session.StartedUtc);
            Assert.NotNull(first.Session.FinishedUtc);
            Assert.NotEqual(default, first.Session.CreatedUtc);
            Assert.Equal("sync-test-1", first.Session.SoftwareVersion);
            Assert.Equal("COM7", first.Session.ComPort);
            Assert.Equal("38400/8/N/1", first.Session.SerialSettings);
            Assert.Equal("operator note", first.Session.Notes);
            Assert.Equal("171804", first.Session.VehicleIdentifier);
            Assert.Equal("Class 171", first.Session.VehicleType);
            Assert.Equal("CLASS171.ccf", first.Session.CcfFilename);
            Assert.Equal("CCF-SHA", first.Session.CcfSha256);
            Assert.Equal("171.json", first.Session.RcmProfileFilename);
            Assert.Equal("RCM-SHA", first.Session.RcmProfileSha256);
            Assert.Equal("{\"schemaVersion\":\"1.3\"}", first.Session.RcmProfileJsonSnapshot);
            Assert.Equal(new byte[] { 0x00, 0xFF, 0x01, 0x07 }, Assert.Single(first.RawEntries).Data);
            Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }, Assert.Single(first.LiveFrames).Data);
            Assert.Single(first.Rcm.Inputs);
            Assert.Single(first.Rcm.Captures);
            Assert.Single(first.Rcm.CaptureFrames);
            Assert.Single(first.Rcm.Comparisons);
            Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }, first.Rcm.CaptureFrames[0].Data);
            Assert.Equal(new[] { "candidate-difference" }, first.Rcm.Comparisons[0].RepeatableDifferences);

            using JsonDocument json = JsonDocument.Parse(firstJson);
            Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("rcm").ValueKind);
            Assert.Equal(JsonValueKind.Array,
                json.RootElement.GetProperty("rcm").GetProperty("comparisons")[0]
                    .GetProperty("repeatableDifferences").ValueKind);
            Assert.Equal(Convert.ToBase64String(new byte[] { 0x00, 0xFF, 0x01, 0x07 }),
                json.RootElement.GetProperty("rawEntries")[0].GetProperty("data").GetString());
        });
    }

    [Fact]
    public async Task ManifestCountsAndHashMatchCanonicalContent()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            OtmrApiV1UploadRequest package = await store.BuildApiV1UploadPackageAsync(sessionId);
            Assert.Equal(1, package.Manifest.RawEntryCount);
            Assert.Equal(1, package.Manifest.LiveFrameCount);
            Assert.Equal(1, package.Manifest.RcmInputCount);
            Assert.Equal(1, package.Manifest.RcmCaptureCount);
            Assert.Equal(1, package.Manifest.RcmCaptureFrameCount);
            Assert.Equal(1, package.Manifest.RcmComparisonCount);
            Assert.Equal(64, package.Manifest.ContentSha256.Length);
            Assert.Equal(package.Manifest.ContentSha256, OtmrApiV1Json.ComputeContentSha256(
                package.ApiVersion, package.Session, package.RawEntries, package.LiveFrames, package.Rcm));
        });
    }

    [Fact]
    public async Task MutableLocalDeliveryStateDoesNotChangeFrozenContentHash()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            OtmrApiV1UploadRequest pendingPackage = await store.BuildApiV1UploadPackageAsync(sessionId);
            OtmrPendingUpload pending = Assert.Single(await store.GetPendingUploadsAsync());
            OtmrUploadLease lease = Assert.IsType<OtmrUploadLease>(await store.TryAcquireUploadLeaseAsync(
                pending.OutboxId, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
            OtmrApiV1UploadRequest uploadingPackage = await store.BuildApiV1UploadPackageAsync(sessionId);
            await store.MarkUploadFailedAsync(lease.OutboxId, lease.LeaseId, "retry proof");
            OtmrApiV1UploadRequest failedPackage = await store.BuildApiV1UploadPackageAsync(sessionId);

            Assert.Equal(pendingPackage.Manifest.ContentSha256, uploadingPackage.Manifest.ContentSha256);
            Assert.Equal(pendingPackage.Manifest.ContentSha256, failedPackage.Manifest.ContentSha256);
        });
    }

    [Fact]
    public async Task MatchingAcknowledgementMarksSessionUploaded()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            OtmrApiV1UploadRequest package = await store.BuildApiV1UploadPackageAsync(sessionId);
            var client = new DelegateSyncClient((_, _) => Task.FromResult(Acknowledgement(package)));
            var coordinator = new OtmrSyncCoordinator(store, client);

            OtmrSyncItemResult result = Assert.Single(await coordinator.SynchronizePendingAsync());

            Assert.True(result.Uploaded);
            Assert.False(result.AlreadyPresent);
            Assert.Empty(await store.GetPendingUploadsAsync());
            Assert.Equal(OtmrSyncStates.Uploaded, await SessionStateAsync(store.DatabasePath, sessionId));
        });
    }

    [Fact]
    public async Task AlreadyPresentMatchingAcknowledgementIsIdempotentSuccess()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            OtmrApiV1UploadRequest package = await store.BuildApiV1UploadPackageAsync(sessionId);
            var client = new DelegateSyncClient((_, _) => Task.FromResult(
                Acknowledgement(package) with { AlreadyPresent = true }));

            OtmrSyncItemResult result = Assert.Single(
                await new OtmrSyncCoordinator(store, client).SynchronizePendingAsync());

            Assert.True(result.Uploaded);
            Assert.True(result.AlreadyPresent);
            Assert.Equal(OtmrSyncStates.Uploaded, await SessionStateAsync(store.DatabasePath, sessionId));
        });
    }

    [Fact]
    public async Task BadManifestAcknowledgementRemainsRetryable()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            OtmrApiV1UploadRequest package = await store.BuildApiV1UploadPackageAsync(sessionId);
            var client = new DelegateSyncClient((_, _) => Task.FromResult(
                Acknowledgement(package) with { ManifestSha256 = new string('0', 64) }));

            OtmrSyncItemResult result = Assert.Single(
                await new OtmrSyncCoordinator(store, client).SynchronizePendingAsync());

            Assert.False(result.Uploaded);
            Assert.Contains("manifest", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Single(await store.GetPendingUploadsAsync());
            Assert.Equal(OtmrSyncStates.UploadFailed, await SessionStateAsync(store.DatabasePath, sessionId));
        });
    }

    [Fact]
    public async Task NetworkFailureRetainsDataAndRemainsRetryable()
    {
        await AssertCoordinatorFailureRemainsRetryable((_, _) =>
            throw new HttpRequestException("network unavailable"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task HttpFailureDoesNotMarkSessionUploaded(HttpStatusCode statusCode)
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            using var http = new HttpClient(new DelegateHttpHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(statusCode) { Content = new StringContent("failure") })));
            var client = new HttpOtmrSyncClient(http, TestOptions());

            OtmrSyncItemResult result = Assert.Single(
                await new OtmrSyncCoordinator(store, client).SynchronizePendingAsync());

            Assert.False(result.Uploaded);
            Assert.Single(await store.GetPendingUploadsAsync());
            Assert.Equal(OtmrSyncStates.UploadFailed, await SessionStateAsync(store.DatabasePath, sessionId));
        });
    }

    [Fact]
    public async Task RequestTimeoutIsRecordedAsRetryableFailure()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            using var http = new HttpClient(new DelegateHttpHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("unreachable");
            }));
            OtmrSyncOptions options = WithTimeout(TestOptions(), TimeSpan.FromMilliseconds(50));
            var client = new HttpOtmrSyncClient(http, options);

            OtmrSyncItemResult result = Assert.Single(
                await new OtmrSyncCoordinator(store, client).SynchronizePendingAsync());

            Assert.False(result.Uploaded);
            Assert.Contains("timeout", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Single(await store.GetPendingUploadsAsync());
            Assert.Equal(OtmrSyncStates.UploadFailed, await SessionStateAsync(store.DatabasePath, sessionId));
        });
    }

    [Fact]
    public async Task HttpConflictIsRejectedAndRemainsRetryable()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            using var http = new HttpClient(new DelegateHttpHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("same session ID, different manifest")
                })));
            var client = new HttpOtmrSyncClient(http, TestOptions());

            OtmrSyncItemResult result = Assert.Single(
                await new OtmrSyncCoordinator(store, client).SynchronizePendingAsync());

            Assert.False(result.Uploaded);
            Assert.Contains("conflicting", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Single(await store.GetPendingUploadsAsync());
            Assert.Equal(OtmrSyncStates.UploadFailed, await SessionStateAsync(store.DatabasePath, sessionId));
        });
    }

    [Fact]
    public async Task CancellationReleasesLeaseWithoutLosingEvidence()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            var client = new DelegateSyncClient(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("unreachable");
            });
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new OtmrSyncCoordinator(store, client).SynchronizePendingAsync(cancellationToken: cancellation.Token));

            Assert.Single(await store.GetPendingUploadsAsync());
            OtmrApiV1UploadRequest preserved = await store.BuildApiV1UploadPackageAsync(sessionId);
            Assert.Equal(new byte[] { 0x00, 0xFF, 0x01, 0x07 }, Assert.Single(preserved.RawEntries).Data);
        });
    }

    [Fact]
    public async Task StaleUploadingLeaseIsRecovered()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            OtmrPendingUpload pending = Assert.Single(await store.GetPendingUploadsAsync());
            DateTimeOffset acquired = DateTimeOffset.UtcNow.AddMinutes(-10);
            Assert.NotNull(await store.TryAcquireUploadLeaseAsync(
                pending.OutboxId, acquired, TimeSpan.FromMinutes(1)));

            Assert.Equal(1, await store.RecoverStaleUploadLeasesAsync(DateTimeOffset.UtcNow));

            OtmrPendingUpload recovered = Assert.Single(await store.GetPendingUploadsAsync());
            Assert.Equal(pending.OutboxId, recovered.OutboxId);
            Assert.Contains("stale upload lease", recovered.LastError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(OtmrSyncStates.UploadFailed, await SessionStateAsync(store.DatabasePath, sessionId));
        });
    }

    [Fact]
    public async Task HttpClientUsesAtomicJsonEndpointAuthenticationAndIdempotency()
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            OtmrApiV1UploadRequest package = await store.BuildApiV1UploadPackageAsync(sessionId);
            HttpRequestMessage? captured = null;
            string? body = null;
            using var http = new HttpClient(new DelegateHttpHandler(async (request, cancellationToken) =>
            {
                captured = request;
                body = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(Acknowledgement(package), OtmrApiV1Json.Options),
                        Encoding.UTF8, "application/json")
                };
            }));

            OtmrApiV1UploadAcknowledgement ack = await new HttpOtmrSyncClient(http, TestOptions())
                .UploadSessionAsync(package);

            Assert.True(ack.Accepted);
            Assert.Equal(HttpMethod.Post, captured!.Method);
            Assert.Equal("http://localhost/api/v1/otmr/recording-sessions", captured.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
            Assert.Equal("unit-test-token", captured.Headers.Authorization.Parameter);
            Assert.Equal(sessionId.ToString("D"), Assert.Single(captured.Headers.GetValues("Idempotency-Key")));
            Assert.Equal("application/json", captured.Content!.Headers.ContentType!.MediaType);
            Assert.Equal(OtmrApiV1Json.Serialize(package), body);
        });
    }

    [Fact]
    public void ConfigurationRequiresHttpsAndExternallySuppliedCredentials()
    {
        Assert.Null(new OtmrSyncOptions().ApiToken);
        Assert.Throws<InvalidOperationException>(() => new OtmrSyncOptions
        {
            Enabled = true,
            BaseUrl = new Uri("http://otmr.example.com"),
            ApiToken = "placeholder"
        }.Validate());
        Assert.Throws<InvalidOperationException>(() => new OtmrSyncOptions
        {
            Enabled = true,
            BaseUrl = new Uri("https://otmr.example.com")
        }.Validate());
    }

    [Fact]
    public async Task SchemaV1MigrationPreservesEvidenceAndAddsLeaseAndCaptureIdentityRules()
    {
        string folder = NewFolder();
        string path = Path.Combine(folder, "OTMR_RCM.db");
        Guid sessionId = Guid.NewGuid();
        try
        {
            await CreateVersion1DatabaseAsync(path, sessionId);
            await using (var store = new SqliteOtmrRecordingStore(path))
                await store.InitializeAsync();

            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            Assert.Equal(2, await ScalarAsync(connection, "PRAGMA user_version;"));
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM raw_serial_entries;"));
            await using (SqliteCommand raw = connection.CreateCommand())
            {
                raw.CommandText = "SELECT data FROM raw_serial_entries WHERE session_id=$sessionId;";
                raw.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
                Assert.Equal(new byte[] { 0xAA, 0x00, 0xFF }, Assert.IsType<byte[]>(await raw.ExecuteScalarAsync()));
            }
            Assert.Equal(1, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM pragma_table_info('sync_outbox') WHERE name='lease_expires_utc';"));
            Assert.Equal(1, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ux_rcm_capture_frames_capture_sequence';"));
            Assert.Equal(1, await ScalarAsync(connection,
                "SELECT COUNT(*) FROM sync_outbox WHERE state='PENDING_UPLOAD';"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private static async Task AssertCoordinatorFailureRemainsRetryable(
        Func<OtmrApiV1UploadRequest, CancellationToken, Task<OtmrApiV1UploadAcknowledgement>> upload)
    {
        await WithRecordedSessionAsync(async (store, sessionId) =>
        {
            OtmrSyncItemResult result = Assert.Single(
                await new OtmrSyncCoordinator(store, new DelegateSyncClient(upload)).SynchronizePendingAsync());
            Assert.False(result.Uploaded);
            Assert.Single(await store.GetPendingUploadsAsync());
            Assert.Equal(OtmrSyncStates.UploadFailed, await SessionStateAsync(store.DatabasePath, sessionId));
            Assert.Single((await store.BuildApiV1UploadPackageAsync(sessionId)).RawEntries);
        });
    }

    private static OtmrApiV1UploadAcknowledgement Acknowledgement(OtmrApiV1UploadRequest package) => new(
        OtmrApiContract.Version,
        package.Session.SessionId,
        "remote-" + package.Session.SessionId.ToString("N"),
        true,
        false,
        package.Manifest.ContentSha256,
        package.Manifest.RawEntryCount,
        package.Manifest.LiveFrameCount,
        package.Manifest.RcmInputCount,
        package.Manifest.RcmCaptureCount,
        package.Manifest.RcmCaptureFrameCount,
        package.Manifest.RcmComparisonCount);

    private static OtmrSyncOptions TestOptions() => new()
    {
        Enabled = true,
        BaseUrl = new Uri("http://localhost/"),
        ApiToken = "unit-test-token",
        RequestTimeout = TimeSpan.FromSeconds(5),
        AllowInsecureLocalhostForTests = true
    };

    private static OtmrSyncOptions WithTimeout(OtmrSyncOptions source, TimeSpan timeout) => new()
    {
        Enabled = source.Enabled,
        BaseUrl = source.BaseUrl,
        ApiToken = source.ApiToken,
        RequestTimeout = timeout,
        AllowInsecureLocalhostForTests = source.AllowInsecureLocalhostForTests
    };

    private static async Task WithRecordedSessionAsync(Func<SqliteOtmrRecordingStore, Guid, Task> action)
    {
        string folder = NewFolder();
        string path = Path.Combine(folder, "OTMR_RCM.db");
        try
        {
            await using var store = new SqliteOtmrRecordingStore(path);
            Guid sessionId = await store.StartSessionAsync(new OtmrRecordingSessionContext
            {
                SoftwareVersion = "sync-test-1",
                ComPort = "COM7",
                SerialSettings = "38400/8/N/1",
                VehicleIdentifier = "171804",
                VehicleType = "Class 171",
                CcfFilename = "CLASS171.ccf",
                CcfSha256 = "CCF-SHA",
                RcmProfileFilename = "171.json",
                RcmProfileSha256 = "RCM-SHA",
                RcmProfileJsonSnapshot = "{\"schemaVersion\":\"1.3\"}",
                Notes = "operator note"
            });
            DateTimeOffset timestamp = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
            store.TryRecordRaw(new OtmrCaptureEntry(
                timestamp, OtmrDirection.Rx, new byte[] { 0x00, 0xFF, 0x01, 0x07 }, "exact raw"));
            OtmrLiveFrame frame = Assert.Single(new OtmrLiveFrameAssembler().Append(
                new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }));
            store.TryRecordLiveFrame(timestamp.AddMilliseconds(1), frame);

            var pin = new RcmPinProfile
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Connector = "J1", Pin = "A", Function = "Throttle 1", Role = "Digital input",
                Mio = "MIO1", PhysicalChannel = "1", ReturnOrPair = "J1-L", Testable = true,
                SafetyClassification = "24V_TEST", Notes = "input note",
                RcmResult = RcmResultStates.RawDifferenceFound,
                CcfReference = new RcmCcfReference
                {
                    RecordA = 0, RecordB = 12, LogicalCard = 0, LogicalChannel = 0,
                    RecordAText = "OFF", RecordBText = "ON", RecordType = 2, PairRelationship = "0<->12"
                }
            };
            var evidence = new RcmStateEvidence
            {
                Tested = true,
                CaptureStart = timestamp,
                CaptureStop = timestamp.AddSeconds(2),
                CandidateRawSignature = "position 3"
            };
            evidence.CompleteRawFrames.Add(new RcmRawFrameEvidence
            {
                SequenceNumber = 1,
                Timestamp = timestamp.AddMilliseconds(1),
                RawFrameHex = frame.Hex,
                RawFrameBytes = frame.Data.ToArray().Select(value => (int)value).ToList()
            });
            store.TryRecordRcmCapture(pin, RcmElectricalTestState.VoltageApplied24V, evidence);
            pin.Comparison.ComparedAt = timestamp.AddSeconds(3);
            pin.Comparison.RepeatableDifferences.Add("candidate-difference");
            store.TryRecordRcmComparison(pin);
            await store.StopSessionAsync(timestamp.AddSeconds(5));
            await action(store, sessionId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private static async Task<string> SessionStateAsync(string path, Guid sessionId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sync_state FROM recording_sessions WHERE session_id=$sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task CreateVersion1DatabaseAsync(string path, Guid sessionId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA user_version=1;
            CREATE TABLE recording_sessions (
                session_id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, finished_utc TEXT NULL,
                software_version TEXT NOT NULL, com_port TEXT NOT NULL, serial_settings TEXT NOT NULL,
                vehicle_identifier TEXT NULL, vehicle_type TEXT NULL, ccf_filename TEXT NULL,
                ccf_sha256 TEXT NULL, rcm_profile_filename TEXT NULL, rcm_profile_sha256 TEXT NULL,
                rcm_profile_json TEXT NULL, notes TEXT NULL, sync_state TEXT NOT NULL,
                remote_session_id TEXT NULL, created_utc TEXT NOT NULL);
            CREATE TABLE raw_serial_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT NOT NULL, sequence INTEGER NOT NULL,
                timestamp_utc TEXT NOT NULL, direction TEXT NOT NULL, data BLOB NOT NULL,
                interpretation TEXT NULL, UNIQUE(session_id, sequence));
            CREATE TABLE sync_outbox (
                outbox_id TEXT PRIMARY KEY, session_id TEXT NOT NULL, entity_type TEXT NOT NULL,
                state TEXT NOT NULL, created_utc TEXT NOT NULL, attempt_count INTEGER NOT NULL DEFAULT 0,
                last_attempt_utc TEXT NULL, last_error TEXT NULL, uploaded_utc TEXT NULL);
            INSERT INTO recording_sessions VALUES (
                $sessionId, $started, $finished, 'v1', 'COM2', '38400/8/N/1', '171804', 'Class 171',
                NULL, NULL, NULL, NULL, NULL, 'preserve', 'PENDING_UPLOAD', NULL, $started);
            INSERT INTO raw_serial_entries(session_id, sequence, timestamp_utc, direction, data, interpretation)
                VALUES ($sessionId, 1, $started, 'RX', $data, 'preserved');
            INSERT INTO sync_outbox(outbox_id, session_id, entity_type, state, created_utc, attempt_count)
                VALUES ($outboxId, $sessionId, 'recording_session', 'PENDING', $started, 0);
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$outboxId", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$started", new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue("$finished", new DateTimeOffset(2026, 8, 27, 8, 5, 0, TimeSpan.Zero).ToString("O"));
        command.Parameters.Add("$data", SqliteType.Blob).Value = new byte[] { 0xAA, 0x00, 0xFF };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "otmr-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed class DelegateSyncClient : IOtmrSyncClient
    {
        private readonly Func<OtmrApiV1UploadRequest, CancellationToken, Task<OtmrApiV1UploadAcknowledgement>> _upload;
        public DelegateSyncClient(
            Func<OtmrApiV1UploadRequest, CancellationToken, Task<OtmrApiV1UploadAcknowledgement>> upload) =>
            _upload = upload;
        public Task<OtmrApiV1UploadAcknowledgement> UploadSessionAsync(
            OtmrApiV1UploadRequest package, CancellationToken cancellationToken = default) =>
            _upload(package, cancellationToken);
    }

    private sealed class DelegateHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public DelegateHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
            _send = send;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => _send(request, cancellationToken);
    }
}
