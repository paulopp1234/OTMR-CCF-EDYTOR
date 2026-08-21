using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;
using CcfEditor.Otmr.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CcfEditor.Tests;

public sealed class OtmrRecordingDatabaseTests
{
    [Fact]
    public async Task RecordingSession_PreservesRawFramesRcmEvidenceAndQueuesUpload()
    {
        string folder = Path.Combine(Path.GetTempPath(), "otmr-db-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "OTMR_RCM.db");
        Directory.CreateDirectory(folder);

        try
        {
            await using (var store = new SqliteOtmrRecordingStore(path))
            {
                Guid sessionId = await store.StartSessionAsync(new OtmrRecordingSessionContext
                {
                    SoftwareVersion = "test-1",
                    ComPort = "COM7",
                    SerialSettings = "38400/8/N/1",
                    VehicleIdentifier = "171804",
                    VehicleType = "Class 171",
                    CcfFilename = "class171.ccf",
                    CcfSha256 = "CCFSHA",
                    RcmProfileFilename = "171 OTMR v1.json",
                    RcmProfileSha256 = "JSONSHA",
                    RcmProfileJsonSnapshot = "{\"profile\":\"snapshot\"}"
                });

                store.TryRecordRaw(new OtmrCaptureEntry(
                    DateTimeOffset.UtcNow,
                    OtmrDirection.Rx,
                    new byte[] { 0x01, 0x02, 0x03 },
                    "RX test"));
                store.TryRecordRaw(new OtmrCaptureEntry(
                    DateTimeOffset.UtcNow,
                    OtmrDirection.Tx,
                    new byte[] { 0x01, 0x07 },
                    "TX test"));

                OtmrLiveFrame frame = new OtmrLiveFrameAssembler()
                    .Append(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF })
                    .Single();
                store.TryRecordLiveFrame(DateTimeOffset.UtcNow, frame);

                var pin = new RcmPinProfile
                {
                    Id = Guid.NewGuid(),
                    Connector = "J1",
                    Pin = "A",
                    Function = "Throttle 1",
                    Role = "Digital input",
                    Mio = "MIO1",
                    PhysicalChannel = "1",
                    ReturnOrPair = "J1-L",
                    Testable = true,
                    SafetyClassification = "24V_TEST",
                    RcmResult = RcmResultStates.VoltageRemovedCaptured,
                    CcfReference = new RcmCcfReference
                    {
                        RecordA = 0,
                        RecordB = 12,
                        LogicalCard = 0,
                        LogicalChannel = 0,
                        RecordAText = "Throttle 1 OFF",
                        RecordBText = "Throttle 1 ON",
                        RecordType = 2,
                        PairRelationship = "0<->12"
                    }
                };
                var evidence = new RcmStateEvidence
                {
                    Tested = true,
                    CaptureStart = DateTimeOffset.UtcNow.AddSeconds(-2),
                    CaptureStop = DateTimeOffset.UtcNow,
                    CandidateRawSignature = "candidate"
                };
                evidence.CompleteRawFrames.Add(new RcmRawFrameEvidence
                {
                    SequenceNumber = 1,
                    Timestamp = DateTimeOffset.UtcNow,
                    RawFrameHex = "FB FB 38 4A FF",
                    RawFrameBytes = new List<int> { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }
                });
                store.TryRecordRcmCapture(pin, RcmElectricalTestState.VoltageRemoved, evidence);

                pin.Comparison.ComparedAt = DateTimeOffset.UtcNow;
                pin.Comparison.RepeatableDifferences.Add("candidate-difference");
                pin.RcmResult = RcmResultStates.RawDifferenceFound;
                store.TryRecordRcmComparison(pin);

                await store.StopSessionAsync(DateTimeOffset.UtcNow);

                OtmrRecordingStatus status = store.GetStatus();
                Assert.False(status.IsRecording);
                Assert.Null(status.SessionId);
                Assert.Equal(2, status.RawEntryCount);
                Assert.Equal(1, status.CompleteFrameCount);
                Assert.Equal("PENDING_UPLOAD", status.SyncState);

                IReadOnlyList<OtmrPendingUpload> pending = await store.GetPendingUploadsAsync();
                OtmrPendingUpload upload = Assert.Single(pending);
                Assert.Equal(sessionId, upload.SessionId);
            }

            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM recording_sessions;"));
                Assert.Equal(2L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM raw_serial_entries;"));
                Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM live_frames;"));
                Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM rcm_input_tests;"));
                Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM rcm_capture_windows;"));
                Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM rcm_capture_frames;"));
                Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM rcm_comparisons;"));
                Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM sync_outbox WHERE state='PENDING';"));

                await using SqliteCommand raw = connection.CreateCommand();
                raw.CommandText = "SELECT data FROM raw_serial_entries WHERE sequence=1;";
                byte[] stored = Assert.IsType<byte[]>(await raw.ExecuteScalarAsync());
                Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, stored);

                await using SqliteCommand metadata = connection.CreateCommand();
                metadata.CommandText = "SELECT rcm_profile_json FROM recording_sessions;";
                Assert.Equal("{\"profile\":\"snapshot\"}", await metadata.ExecuteScalarAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Store_DoesNotRecordTrafficOutsideExplicitRecordingSession()
    {
        string folder = Path.Combine(Path.GetTempPath(), "otmr-db-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "OTMR_RCM.db");
        Directory.CreateDirectory(folder);
        try
        {
            await using (var store = new SqliteOtmrRecordingStore(path))
            {
                store.TryRecordRaw(new OtmrCaptureEntry(DateTimeOffset.UtcNow, OtmrDirection.Rx, new byte[] { 0xAA }));
                await store.StartSessionAsync(new OtmrRecordingSessionContext { ComPort = "COM1", SoftwareVersion = "test" });
                store.TryRecordRaw(new OtmrCaptureEntry(DateTimeOffset.UtcNow, OtmrDirection.Rx, new byte[] { 0xBB }));
                await store.StopSessionAsync(DateTimeOffset.UtcNow);
                store.TryRecordRaw(new OtmrCaptureEntry(DateTimeOffset.UtcNow, OtmrDirection.Rx, new byte[] { 0xCC }));
            }

            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            Assert.Equal(1L, await ScalarInt64Async(connection, "SELECT COUNT(*) FROM raw_serial_entries;"));
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT data FROM raw_serial_entries LIMIT 1;";
            byte[] stored = Assert.IsType<byte[]>(await command.ExecuteScalarAsync());
            Assert.Equal(new byte[] { 0xBB }, stored);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task UploadOutbox_CanBeMarkedFailedThenSucceeded()
    {
        string folder = Path.Combine(Path.GetTempPath(), "otmr-db-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "OTMR_RCM.db");
        Directory.CreateDirectory(folder);
        try
        {
            await using var store = new SqliteOtmrRecordingStore(path);
            await store.StartSessionAsync(new OtmrRecordingSessionContext { ComPort = "COM9", SoftwareVersion = "test" });
            await store.StopSessionAsync(DateTimeOffset.UtcNow);
            OtmrPendingUpload pending = Assert.Single(await store.GetPendingUploadsAsync());

            await store.MarkUploadFailedAsync(pending.OutboxId, "offline");
            OtmrPendingUpload retried = Assert.Single(await store.GetPendingUploadsAsync());
            Assert.Equal(1, retried.AttemptCount);
            Assert.Equal("offline", retried.LastError);

            await store.MarkUploadSucceededAsync(retried.OutboxId, "remote-session-123");
            Assert.Empty(await store.GetPendingUploadsAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task<long> ScalarInt64Async(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }
}
