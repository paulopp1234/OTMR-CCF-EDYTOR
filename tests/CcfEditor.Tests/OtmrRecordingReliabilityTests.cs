using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;
using CcfEditor.Otmr.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CcfEditor.Tests;

public sealed class OtmrRecordingReliabilityTests
{
    [Fact]
    public void DefaultDatabasePath_IsRcmRoot()
    {
        Assert.Equal(@"C:\OTMR_RCM", OtmrDatabasePaths.DefaultRootDirectory);
        Assert.Equal(@"C:\OTMR_RCM\OTMR_RCM.db", OtmrDatabasePaths.DefaultDatabasePath);
    }

    [Fact]
    public async Task Initialize_SetsVersionedSchema()
    {
        await WithDatabaseAsync(async path =>
        {
            await using (var store = new SqliteOtmrRecordingStore(path))
                await store.InitializeAsync();

            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(SqliteOtmrRecordingStore.CurrentSchemaVersion, Convert.ToInt32(await command.ExecuteScalarAsync()));
        });
    }

    [Fact]
    public async Task Initialize_RejectsUnknownNewerSchema()
    {
        await WithDatabaseAsync(async path =>
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version=999;";
                await command.ExecuteNonQueryAsync();
            }

            await using var store = new SqliteOtmrRecordingStore(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.InitializeAsync());
        });
    }

    [Fact]
    public async Task Initialize_RecoversInterruptedRecordingAndQueuesIt()
    {
        await WithDatabaseAsync(async path =>
        {
            Guid sessionId;
            await using (var store = new SqliteOtmrRecordingStore(path))
            {
                sessionId = await store.StartSessionAsync(new OtmrRecordingSessionContext
                {
                    SoftwareVersion = "recovery-test",
                    ComPort = "COM4",
                    VehicleIdentifier = "171804",
                    VehicleType = "Class 171"
                });
                store.TryRecordRaw(new OtmrCaptureEntry(
                    DateTimeOffset.UtcNow,
                    OtmrDirection.Rx,
                    new byte[] { 0xFB, 0xFB, 0x01, 0xFF }));
                await store.StopSessionAsync(DateTimeOffset.UtcNow);
            }

            // Recreate the database state left by a hard process/power interruption:
            // committed capture rows exist, but the session never reached StopSession.
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    DELETE FROM sync_outbox WHERE session_id = $sessionId;
                    UPDATE recording_sessions
                    SET finished_utc = NULL, sync_state = 'RECORDING'
                    WHERE session_id = $sessionId;
                    """;
                command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
                await command.ExecuteNonQueryAsync();
            }

            await using var recoveredStore = new SqliteOtmrRecordingStore(path);
            await recoveredStore.InitializeAsync();

            Assert.Equal("RECOVERED_INTERRUPTED_SESSION", recoveredStore.GetStatus().SyncState);
            OtmrPendingUpload pending = Assert.Single(await recoveredStore.GetPendingUploadsAsync());
            Assert.Equal(sessionId, pending.SessionId);

            OtmrSessionUploadPackage package = await recoveredStore.BuildUploadPackageAsync(sessionId);
            Assert.Equal(sessionId, package.Session.SessionId);
            Assert.Equal("INTERRUPTED_PENDING_UPLOAD", package.Session.SyncState);
            Assert.Equal("171804", package.Session.VehicleIdentifier);
            Assert.Single(package.RawEntries);
            Assert.Equal(new byte[] { 0xFB, 0xFB, 0x01, 0xFF }, package.RawEntries[0].Data);
        });
    }

    [Fact]
    public async Task TemporarySqliteWriteLock_DoesNotDropQueuedRawBatch()
    {
        await WithDatabaseAsync(async path =>
        {
            await using var store = new SqliteOtmrRecordingStore(path);
            await store.InitializeAsync();
            Guid sessionId = await store.StartSessionAsync(new OtmrRecordingSessionContext
            {
                SoftwareVersion = "retry-test",
                ComPort = "COM8"
            });

            byte[] expected = { 0x01, 0x07, 0xAA, 0x55 };
            Task stopTask;
            await using (var blocker = new SqliteConnection($"Data Source={path}"))
            {
                await blocker.OpenAsync();
                await using (SqliteCommand begin = blocker.CreateCommand())
                {
                    begin.CommandText = "PRAGMA busy_timeout=100; BEGIN IMMEDIATE;";
                    await begin.ExecuteNonQueryAsync();
                }

                store.TryRecordRaw(new OtmrCaptureEntry(DateTimeOffset.UtcNow, OtmrDirection.Tx, expected));
                stopTask = store.StopSessionAsync(DateTimeOffset.UtcNow);

                // The recording store uses a 5-second SQLite busy timeout. Hold the
                // writer lock long enough to force at least one failed write attempt.
                await Task.Delay(TimeSpan.FromMilliseconds(5400));
                Assert.StartsWith("LOCAL_DB_RETRY_", store.GetStatus().SyncState);

                await using SqliteCommand commit = blocker.CreateCommand();
                commit.CommandText = "COMMIT;";
                await commit.ExecuteNonQueryAsync();
            }

            // The competing connection is disposed before waiting for the writer,
            // so Windows cannot retain an avoidable SQLite file handle in cleanup.
            await stopTask.WaitAsync(TimeSpan.FromSeconds(15));

            OtmrSessionUploadPackage package = await store.BuildUploadPackageAsync(sessionId);
            OtmrUploadRawEntry raw = Assert.Single(package.RawEntries);
            Assert.Equal(expected, raw.Data);
            Assert.Equal("PENDING_UPLOAD", package.Session.SyncState);
        });
    }

    [Fact]
    public async Task StopSession_CheckpointsWalAndBuildsCompleteUploadPackage()
    {
        await WithDatabaseAsync(async path =>
        {
            Guid sessionId;
            await using (var store = new SqliteOtmrRecordingStore(path))
            {
                sessionId = await store.StartSessionAsync(new OtmrRecordingSessionContext
                {
                    SoftwareVersion = "upload-test",
                    ComPort = "COM7",
                    VehicleIdentifier = "171804",
                    VehicleType = "Class 171",
                    CcfFilename = "CLASS171.ccf",
                    CcfSha256 = "CCF-SHA",
                    RcmProfileFilename = "171 OTMR v1.json",
                    RcmProfileSha256 = "RCM-SHA",
                    RcmProfileJsonSnapshot = "{\"schemaVersion\":\"1.2\"}"
                });

                store.TryRecordRaw(new OtmrCaptureEntry(
                    DateTimeOffset.UtcNow,
                    OtmrDirection.Rx,
                    new byte[] { 0x10, 0x20, 0x30 },
                    "fixture"));
                OtmrLiveFrame frame = new OtmrLiveFrameAssembler()
                    .Append(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF })
                    .Single();
                store.TryRecordLiveFrame(DateTimeOffset.UtcNow, frame);
                await store.StopSessionAsync(DateTimeOffset.UtcNow);

                OtmrSessionUploadPackage package = await store.BuildUploadPackageAsync(sessionId);
                Assert.Equal("171804", package.Session.VehicleIdentifier);
                Assert.Equal("171 OTMR v1.json", package.Session.RcmProfileFilename);
                Assert.Equal("{\"schemaVersion\":\"1.2\"}", package.Session.RcmProfileJsonSnapshot);
                Assert.Single(package.RawEntries);
                Assert.Single(package.LiveFrames);
                Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, package.RawEntries[0].Data);
                Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }, package.LiveFrames[0].Data);
            }

            SqliteConnection.ClearAllPools();
            string walPath = path + "-wal";
            Assert.True(!File.Exists(walPath) || new FileInfo(walPath).Length == 0,
                "StopSession should checkpoint/truncate the SQLite WAL after flushing the recording.");
        });
    }

    [Fact]
    public void Coordinator_EmitsCaptureAndComparisonCompletionExactlyOnce()
    {
        var coordinator = new RcmCaptureWindowCoordinator();
        var pin = new RcmPinProfile
        {
            Connector = "J1",
            Pin = "A",
            Function = "Throttle 1",
            Testable = true
        };
        OtmrLiveFrame frame = new OtmrLiveFrameAssembler()
            .Append(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF })
            .Single();

        int captureEvents = 0;
        int comparisonEvents = 0;
        coordinator.CaptureCompleted += (_, e) =>
        {
            captureEvents++;
            Assert.Same(pin, e.Pin);
            Assert.True(e.Evidence.Tested);
        };
        coordinator.ComparisonCompleted += (_, e) =>
        {
            comparisonEvents++;
            Assert.Same(pin, e.Pin);
        };

        DateTimeOffset now = DateTimeOffset.UtcNow;
        coordinator.Begin(pin, RcmElectricalTestState.VoltageRemoved, now);
        Assert.True(coordinator.AddFrame(now.AddMilliseconds(10), frame));
        coordinator.Stop(now.AddSeconds(1));

        coordinator.Begin(pin, RcmElectricalTestState.VoltageApplied24V, now.AddSeconds(2));
        Assert.True(coordinator.AddFrame(now.AddSeconds(2.01), frame));
        coordinator.Stop(now.AddSeconds(3));

        coordinator.Compare(pin, now.AddSeconds(4));

        Assert.Equal(2, captureEvents);
        Assert.Equal(1, comparisonEvents);
    }

    private static async Task WithDatabaseAsync(Func<string, Task> action)
    {
        string folder = Path.Combine(Path.GetTempPath(), "otmr-reliability-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "OTMR_RCM.db");
        Directory.CreateDirectory(folder);
        try
        {
            await action(path);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 5)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    SqliteConnection.ClearAllPools();
                    await Task.Delay(100);
                }
            }
        }
    }
}
