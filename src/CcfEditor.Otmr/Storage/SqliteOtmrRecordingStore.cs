using System.Text.Json;
using System.Threading.Channels;
using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;
using Microsoft.Data.Sqlite;

namespace CcfEditor.Otmr.Storage;

public sealed class SqliteOtmrRecordingStore : IOtmrRecordingStore
{
    private readonly Channel<DbWorkItem> _writeQueue = Channel.CreateUnbounded<DbWorkItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _stateSync = new();
    private readonly Task _writerTask;
    private Guid? _activeSessionId;
    private bool _isRecording;
    private bool _disposed;
    private long _rawSequence;
    private long _frameSequence;
    private long _rawCount;
    private long _frameCount;
    private string _syncState = "IDLE";

    public SqliteOtmrRecordingStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        _writerTask = Task.Run(WriterLoopAsync);
    }

    public string DatabasePath { get; }

    public bool IsRecording
    {
        get
        {
            lock (_stateSync)
                return _isRecording;
        }
    }

    public Guid? ActiveSessionId
    {
        get
        {
            lock (_stateSync)
                return _activeSessionId;
        }
    }

    public event EventHandler<OtmrRecordingStatusChangedEventArgs>? StatusChanged;

    public OtmrRecordingStatus GetStatus()
    {
        lock (_stateSync)
        {
            return new OtmrRecordingStatus(
                _isRecording,
                _activeSessionId,
                Interlocked.Read(ref _rawCount),
                Interlocked.Read(ref _frameCount),
                DatabasePath,
                _syncState);
        }
    }

    public async Task<Guid> StartSessionAsync(
        OtmrRecordingSessionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateSync)
            {
                if (_isRecording)
                    throw new InvalidOperationException("Database recording is already active.");
            }

            await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);

            Guid sessionId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO recording_sessions (
                    session_id, started_utc, software_version, com_port, serial_settings,
                    vehicle_identifier, vehicle_type, ccf_filename, ccf_sha256,
                    rcm_profile_filename, rcm_profile_sha256, rcm_profile_json,
                    notes, sync_state, created_utc)
                VALUES (
                    $sessionId, $startedUtc, $softwareVersion, $comPort, $serialSettings,
                    $vehicleIdentifier, $vehicleType, $ccfFilename, $ccfSha256,
                    $rcmFilename, $rcmSha256, $rcmJson,
                    $notes, 'RECORDING', $createdUtc);
                """;
            command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
            command.Parameters.AddWithValue("$startedUtc", UtcText(now));
            command.Parameters.AddWithValue("$softwareVersion", context.SoftwareVersion ?? string.Empty);
            command.Parameters.AddWithValue("$comPort", context.ComPort ?? string.Empty);
            command.Parameters.AddWithValue("$serialSettings", context.SerialSettings ?? string.Empty);
            AddNullableText(command, "$vehicleIdentifier", context.VehicleIdentifier);
            AddNullableText(command, "$vehicleType", context.VehicleType);
            AddNullableText(command, "$ccfFilename", context.CcfFilename);
            AddNullableText(command, "$ccfSha256", context.CcfSha256);
            AddNullableText(command, "$rcmFilename", context.RcmProfileFilename);
            AddNullableText(command, "$rcmSha256", context.RcmProfileSha256);
            AddNullableText(command, "$rcmJson", context.RcmProfileJsonSnapshot);
            AddNullableText(command, "$notes", context.Notes);
            command.Parameters.AddWithValue("$createdUtc", UtcText(now));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            Interlocked.Exchange(ref _rawSequence, 0);
            Interlocked.Exchange(ref _frameSequence, 0);
            Interlocked.Exchange(ref _rawCount, 0);
            Interlocked.Exchange(ref _frameCount, 0);
            lock (_stateSync)
            {
                _activeSessionId = sessionId;
                _isRecording = true;
                _syncState = "LOCAL_RECORDING";
            }
            RaiseStatusChanged();
            return sessionId;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopSessionAsync(
        DateTimeOffset stoppedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Guid? sessionId;
            lock (_stateSync)
            {
                sessionId = _activeSessionId;
                if (!_isRecording || sessionId is null)
                    return;
                _isRecording = false;
            }

            await FlushQueueAsync(cancellationToken).ConfigureAwait(false);
            await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);

            Guid outboxId = Guid.NewGuid();
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using SqliteTransaction transaction = connection.BeginTransaction();

            await using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE recording_sessions
                    SET finished_utc = $finishedUtc,
                        sync_state = 'PENDING_UPLOAD'
                    WHERE session_id = $sessionId;
                    """;
                update.Parameters.AddWithValue("$finishedUtc", UtcText(stoppedAtUtc));
                update.Parameters.AddWithValue("$sessionId", sessionId.Value.ToString("D"));
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (SqliteCommand outbox = connection.CreateCommand())
            {
                outbox.Transaction = transaction;
                outbox.CommandText = """
                    INSERT INTO sync_outbox (
                        outbox_id, session_id, entity_type, state, created_utc, attempt_count)
                    VALUES ($outboxId, $sessionId, 'recording_session', 'PENDING', $createdUtc, 0);
                    """;
                outbox.Parameters.AddWithValue("$outboxId", outboxId.ToString("D"));
                outbox.Parameters.AddWithValue("$sessionId", sessionId.Value.ToString("D"));
                outbox.Parameters.AddWithValue("$createdUtc", UtcText(DateTimeOffset.UtcNow));
                await outbox.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();

            lock (_stateSync)
            {
                _activeSessionId = null;
                _syncState = "PENDING_UPLOAD";
            }
            RaiseStatusChanged();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void TryRecordRaw(OtmrCaptureEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Guid? sessionId = RecordingSessionOrNull();
        if (sessionId is null)
            return;

        long sequence = Interlocked.Increment(ref _rawSequence);
        byte[] data = entry.GetDataSnapshot();
        if (_writeQueue.Writer.TryWrite(new RawEntryWorkItem(
                sessionId.Value,
                sequence,
                entry.Timestamp.ToUniversalTime(),
                entry.Direction.ToString().ToUpperInvariant(),
                data,
                entry.Interpretation)))
        {
            Interlocked.Increment(ref _rawCount);
            RaiseStatusChanged();
        }
    }

    public void TryRecordLiveFrame(DateTimeOffset timestamp, OtmrLiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Guid? sessionId = RecordingSessionOrNull();
        if (sessionId is null)
            return;

        long sequence = Interlocked.Increment(ref _frameSequence);
        if (_writeQueue.Writer.TryWrite(new LiveFrameWorkItem(
                sessionId.Value,
                sequence,
                timestamp.ToUniversalTime(),
                frame.GetDataSnapshot())))
        {
            Interlocked.Increment(ref _frameCount);
            RaiseStatusChanged();
        }
    }

    public void TryRecordRcmCapture(RcmPinProfile pin, RcmElectricalTestState state, RcmStateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(evidence);
        Guid? sessionId = RecordingSessionOrNull();
        if (sessionId is null)
            return;

        var snapshot = RcmInputSnapshot.From(pin);
        var frames = evidence.CompleteRawFrames
            .Select(frame => new RcmFrameSnapshot(
                frame.SequenceNumber,
                frame.Timestamp.ToUniversalTime(),
                frame.RawFrameBytes.Select(value => checked((byte)value)).ToArray()))
            .ToArray();
        _writeQueue.Writer.TryWrite(new RcmCaptureWorkItem(
            sessionId.Value,
            snapshot,
            state.ToString(),
            evidence.CaptureStart?.ToUniversalTime(),
            evidence.CaptureStop?.ToUniversalTime(),
            evidence.NoOtmrData,
            evidence.CandidateRawSignature,
            frames));
    }

    public void TryRecordRcmComparison(RcmPinProfile pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        Guid? sessionId = RecordingSessionOrNull();
        if (sessionId is null)
            return;

        var snapshot = RcmInputSnapshot.From(pin);
        _writeQueue.Writer.TryWrite(new RcmComparisonWorkItem(
            sessionId.Value,
            snapshot,
            pin.Comparison.ComparedAt?.ToUniversalTime(),
            JsonSerializer.Serialize(pin.Comparison.CommonFeatures),
            JsonSerializer.Serialize(pin.Comparison.UniqueFeaturesVoltageRemoved),
            JsonSerializer.Serialize(pin.Comparison.UniqueFeaturesVoltageApplied24V),
            JsonSerializer.Serialize(pin.Comparison.RepeatableDifferences),
            JsonSerializer.Serialize(pin.Comparison.CandidateTransitionEvidence),
            pin.Comparison.DecoderVerified,
            pin.RcmResult));
    }

    public async Task<IReadOnlyList<OtmrPendingUpload>> GetPendingUploadsAsync(
        int maximum = 100,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (maximum <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);

        var result = new List<OtmrPendingUpload>();
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT outbox_id, session_id, created_utc, attempt_count, last_error
            FROM sync_outbox
            WHERE state = 'PENDING'
            ORDER BY created_utc
            LIMIT $maximum;
            """;
        command.Parameters.AddWithValue("$maximum", maximum);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new OtmrPendingUpload(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return result;
    }

    public async Task MarkUploadSucceededAsync(
        Guid outboxId,
        string remoteSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteSessionId);
        ThrowIfDisposed();
        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();

        string? sessionId = null;
        await using (SqliteCommand find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT session_id FROM sync_outbox WHERE outbox_id = $outboxId;";
            find.Parameters.AddWithValue("$outboxId", outboxId.ToString("D"));
            sessionId = (string?)await find.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        if (sessionId is null)
            throw new InvalidOperationException("Upload outbox item was not found.");

        await using (SqliteCommand updateOutbox = connection.CreateCommand())
        {
            updateOutbox.Transaction = transaction;
            updateOutbox.CommandText = """
                UPDATE sync_outbox
                SET state = 'UPLOADED', uploaded_utc = $uploadedUtc, last_error = NULL
                WHERE outbox_id = $outboxId;
                """;
            updateOutbox.Parameters.AddWithValue("$uploadedUtc", UtcText(DateTimeOffset.UtcNow));
            updateOutbox.Parameters.AddWithValue("$outboxId", outboxId.ToString("D"));
            await updateOutbox.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (SqliteCommand updateSession = connection.CreateCommand())
        {
            updateSession.Transaction = transaction;
            updateSession.CommandText = """
                UPDATE recording_sessions
                SET sync_state = 'SYNCED', remote_session_id = $remoteSessionId
                WHERE session_id = $sessionId;
                """;
            updateSession.Parameters.AddWithValue("$remoteSessionId", remoteSessionId);
            updateSession.Parameters.AddWithValue("$sessionId", sessionId);
            await updateSession.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        transaction.Commit();
    }

    public async Task MarkUploadFailedAsync(
        Guid outboxId,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ThrowIfDisposed();
        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE sync_outbox
            SET attempt_count = attempt_count + 1,
                last_error = $error,
                last_attempt_utc = $attemptedUtc,
                state = 'PENDING'
            WHERE outbox_id = $outboxId;
            """;
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$attemptedUtc", UtcText(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$outboxId", outboxId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private Guid? RecordingSessionOrNull()
    {
        lock (_stateSync)
            return _isRecording ? _activeSessionId : null;
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS recording_sessions (
                session_id TEXT PRIMARY KEY,
                started_utc TEXT NOT NULL,
                finished_utc TEXT NULL,
                software_version TEXT NOT NULL,
                com_port TEXT NOT NULL,
                serial_settings TEXT NOT NULL,
                vehicle_identifier TEXT NULL,
                vehicle_type TEXT NULL,
                ccf_filename TEXT NULL,
                ccf_sha256 TEXT NULL,
                rcm_profile_filename TEXT NULL,
                rcm_profile_sha256 TEXT NULL,
                rcm_profile_json TEXT NULL,
                notes TEXT NULL,
                sync_state TEXT NOT NULL,
                remote_session_id TEXT NULL,
                created_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS raw_serial_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                timestamp_utc TEXT NOT NULL,
                direction TEXT NOT NULL,
                data BLOB NOT NULL,
                interpretation TEXT NULL,
                UNIQUE(session_id, sequence),
                FOREIGN KEY(session_id) REFERENCES recording_sessions(session_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS live_frames (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                timestamp_utc TEXT NOT NULL,
                data BLOB NOT NULL,
                decoder_version TEXT NULL,
                decode_status TEXT NOT NULL DEFAULT 'RAW_NOT_DECODED',
                UNIQUE(session_id, sequence),
                FOREIGN KEY(session_id) REFERENCES recording_sessions(session_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS rcm_input_tests (
                session_id TEXT NOT NULL,
                input_guid TEXT NOT NULL,
                connector TEXT NOT NULL,
                pin TEXT NOT NULL,
                function TEXT NOT NULL,
                role TEXT NOT NULL,
                mio TEXT NOT NULL,
                physical_channel TEXT NOT NULL,
                return_or_pair TEXT NOT NULL,
                testable INTEGER NOT NULL,
                safety_classification TEXT NOT NULL,
                notes TEXT NOT NULL,
                record_a INTEGER NULL,
                record_b INTEGER NULL,
                logical_card INTEGER NULL,
                logical_channel INTEGER NULL,
                record_a_text TEXT NULL,
                record_b_text TEXT NULL,
                record_type INTEGER NULL,
                pair_relationship TEXT NULL,
                latest_result TEXT NOT NULL,
                PRIMARY KEY(session_id, input_guid),
                FOREIGN KEY(session_id) REFERENCES recording_sessions(session_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS rcm_capture_windows (
                capture_id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                input_guid TEXT NOT NULL,
                electrical_state TEXT NOT NULL,
                started_utc TEXT NULL,
                finished_utc TEXT NULL,
                frame_count INTEGER NOT NULL,
                no_otmr_data INTEGER NOT NULL,
                candidate_raw_signature TEXT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY(session_id, input_guid) REFERENCES rcm_input_tests(session_id, input_guid) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS rcm_capture_frames (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                capture_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                timestamp_utc TEXT NOT NULL,
                data BLOB NOT NULL,
                FOREIGN KEY(capture_id) REFERENCES rcm_capture_windows(capture_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS rcm_comparisons (
                session_id TEXT NOT NULL,
                input_guid TEXT NOT NULL,
                compared_utc TEXT NULL,
                common_features_json TEXT NOT NULL,
                unique_voltage_removed_json TEXT NOT NULL,
                unique_voltage_applied_json TEXT NOT NULL,
                repeatable_differences_json TEXT NOT NULL,
                candidate_transition_json TEXT NOT NULL,
                decoder_verified INTEGER NOT NULL,
                result TEXT NOT NULL,
                PRIMARY KEY(session_id, input_guid),
                FOREIGN KEY(session_id, input_guid) REFERENCES rcm_input_tests(session_id, input_guid) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS sync_outbox (
                outbox_id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                state TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                last_attempt_utc TEXT NULL,
                last_error TEXT NULL,
                uploaded_utc TEXT NULL,
                FOREIGN KEY(session_id) REFERENCES recording_sessions(session_id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_raw_serial_session_time
                ON raw_serial_entries(session_id, timestamp_utc);
            CREATE INDEX IF NOT EXISTS ix_live_frames_session_time
                ON live_frames(session_id, timestamp_utc);
            CREATE INDEX IF NOT EXISTS ix_sync_outbox_state_created
                ON sync_outbox(state, created_utc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            while (await _writeQueue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                var batch = new List<DbWorkItem>(256);
                TaskCompletionSource<bool>? barrier = null;
                while (batch.Count < 256 && _writeQueue.Reader.TryRead(out DbWorkItem? item))
                {
                    if (item is BarrierWorkItem barrierItem)
                    {
                        barrier = barrierItem.Completion;
                        break;
                    }
                    batch.Add(item);
                }

                if (batch.Count > 0)
                {
                    try
                    {
                        await WriteBatchAsync(batch).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        lock (_stateSync)
                            _syncState = "LOCAL_DB_ERROR";
                        barrier?.TrySetException(ex);
                        RaiseStatusChanged();
                        continue;
                    }
                }

                barrier?.TrySetResult(true);
            }
        }
        catch
        {
            // The live serial path must never be torn down by a background
            // database writer failure. Status reports the local DB error and
            // shutdown/dispose handles the remaining lifecycle.
            lock (_stateSync)
                _syncState = "LOCAL_DB_ERROR";
            RaiseStatusChanged();
        }
    }

    private async Task WriteBatchAsync(IReadOnlyList<DbWorkItem> batch)
    {
        await EnsureDatabaseAsync(CancellationToken.None).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();
        foreach (DbWorkItem item in batch)
        {
            switch (item)
            {
                case RawEntryWorkItem raw:
                    await WriteRawAsync(connection, transaction, raw).ConfigureAwait(false);
                    break;
                case LiveFrameWorkItem frame:
                    await WriteLiveFrameAsync(connection, transaction, frame).ConfigureAwait(false);
                    break;
                case RcmCaptureWorkItem capture:
                    await WriteRcmCaptureAsync(connection, transaction, capture).ConfigureAwait(false);
                    break;
                case RcmComparisonWorkItem comparison:
                    await WriteRcmComparisonAsync(connection, transaction, comparison).ConfigureAwait(false);
                    break;
            }
        }
        transaction.Commit();
    }

    private static async Task WriteRawAsync(SqliteConnection connection, SqliteTransaction transaction, RawEntryWorkItem item)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO raw_serial_entries (
                session_id, sequence, timestamp_utc, direction, data, interpretation)
            VALUES ($sessionId, $sequence, $timestampUtc, $direction, $data, $interpretation);
            """;
        command.Parameters.AddWithValue("$sessionId", item.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", item.Sequence);
        command.Parameters.AddWithValue("$timestampUtc", UtcText(item.TimestampUtc));
        command.Parameters.AddWithValue("$direction", item.Direction);
        command.Parameters.Add("$data", SqliteType.Blob).Value = item.Data;
        AddNullableText(command, "$interpretation", item.Interpretation);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task WriteLiveFrameAsync(SqliteConnection connection, SqliteTransaction transaction, LiveFrameWorkItem item)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO live_frames (session_id, sequence, timestamp_utc, data, decode_status)
            VALUES ($sessionId, $sequence, $timestampUtc, $data, 'RAW_NOT_DECODED');
            """;
        command.Parameters.AddWithValue("$sessionId", item.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$sequence", item.Sequence);
        command.Parameters.AddWithValue("$timestampUtc", UtcText(item.TimestampUtc));
        command.Parameters.Add("$data", SqliteType.Blob).Value = item.Data;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task WriteRcmCaptureAsync(SqliteConnection connection, SqliteTransaction transaction, RcmCaptureWorkItem item)
    {
        await UpsertInputSnapshotAsync(connection, transaction, item.SessionId, item.Input).ConfigureAwait(false);
        Guid captureId = Guid.NewGuid();
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO rcm_capture_windows (
                    capture_id, session_id, input_guid, electrical_state,
                    started_utc, finished_utc, frame_count, no_otmr_data,
                    candidate_raw_signature, created_utc)
                VALUES (
                    $captureId, $sessionId, $inputGuid, $electricalState,
                    $startedUtc, $finishedUtc, $frameCount, $noData,
                    $signature, $createdUtc);
                """;
            command.Parameters.AddWithValue("$captureId", captureId.ToString("D"));
            command.Parameters.AddWithValue("$sessionId", item.SessionId.ToString("D"));
            command.Parameters.AddWithValue("$inputGuid", item.Input.InputGuid.ToString("D"));
            command.Parameters.AddWithValue("$electricalState", item.ElectricalState);
            AddNullableText(command, "$startedUtc", item.StartedUtc is null ? null : UtcText(item.StartedUtc.Value));
            AddNullableText(command, "$finishedUtc", item.FinishedUtc is null ? null : UtcText(item.FinishedUtc.Value));
            command.Parameters.AddWithValue("$frameCount", item.Frames.Count);
            command.Parameters.AddWithValue("$noData", item.NoOtmrData ? 1 : 0);
            AddNullableText(command, "$signature", string.IsNullOrWhiteSpace(item.CandidateRawSignature) ? null : item.CandidateRawSignature);
            command.Parameters.AddWithValue("$createdUtc", UtcText(DateTimeOffset.UtcNow));
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        foreach (RcmFrameSnapshot frame in item.Frames)
        {
            await using SqliteCommand frameCommand = connection.CreateCommand();
            frameCommand.Transaction = transaction;
            frameCommand.CommandText = """
                INSERT INTO rcm_capture_frames (capture_id, sequence, timestamp_utc, data)
                VALUES ($captureId, $sequence, $timestampUtc, $data);
                """;
            frameCommand.Parameters.AddWithValue("$captureId", captureId.ToString("D"));
            frameCommand.Parameters.AddWithValue("$sequence", frame.Sequence);
            frameCommand.Parameters.AddWithValue("$timestampUtc", UtcText(frame.TimestampUtc));
            frameCommand.Parameters.Add("$data", SqliteType.Blob).Value = frame.Data;
            await frameCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteRcmComparisonAsync(SqliteConnection connection, SqliteTransaction transaction, RcmComparisonWorkItem item)
    {
        await UpsertInputSnapshotAsync(connection, transaction, item.SessionId, item.Input).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO rcm_comparisons (
                session_id, input_guid, compared_utc, common_features_json,
                unique_voltage_removed_json, unique_voltage_applied_json,
                repeatable_differences_json, candidate_transition_json,
                decoder_verified, result)
            VALUES (
                $sessionId, $inputGuid, $comparedUtc, $common,
                $uniqueRemoved, $uniqueApplied,
                $repeatable, $transitions,
                $decoderVerified, $result)
            ON CONFLICT(session_id, input_guid) DO UPDATE SET
                compared_utc = excluded.compared_utc,
                common_features_json = excluded.common_features_json,
                unique_voltage_removed_json = excluded.unique_voltage_removed_json,
                unique_voltage_applied_json = excluded.unique_voltage_applied_json,
                repeatable_differences_json = excluded.repeatable_differences_json,
                candidate_transition_json = excluded.candidate_transition_json,
                decoder_verified = excluded.decoder_verified,
                result = excluded.result;
            """;
        command.Parameters.AddWithValue("$sessionId", item.SessionId.ToString("D"));
        command.Parameters.AddWithValue("$inputGuid", item.Input.InputGuid.ToString("D"));
        AddNullableText(command, "$comparedUtc", item.ComparedUtc is null ? null : UtcText(item.ComparedUtc.Value));
        command.Parameters.AddWithValue("$common", item.CommonFeaturesJson);
        command.Parameters.AddWithValue("$uniqueRemoved", item.UniqueVoltageRemovedJson);
        command.Parameters.AddWithValue("$uniqueApplied", item.UniqueVoltageAppliedJson);
        command.Parameters.AddWithValue("$repeatable", item.RepeatableDifferencesJson);
        command.Parameters.AddWithValue("$transitions", item.CandidateTransitionJson);
        command.Parameters.AddWithValue("$decoderVerified", item.DecoderVerified ? 1 : 0);
        command.Parameters.AddWithValue("$result", item.Result);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task UpsertInputSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        RcmInputSnapshot input)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO rcm_input_tests (
                session_id, input_guid, connector, pin, function, role, mio,
                physical_channel, return_or_pair, testable, safety_classification, notes,
                record_a, record_b, logical_card, logical_channel,
                record_a_text, record_b_text, record_type, pair_relationship, latest_result)
            VALUES (
                $sessionId, $inputGuid, $connector, $pin, $function, $role, $mio,
                $physicalChannel, $returnOrPair, $testable, $safety, $notes,
                $recordA, $recordB, $logicalCard, $logicalChannel,
                $recordAText, $recordBText, $recordType, $pairRelationship, $latestResult)
            ON CONFLICT(session_id, input_guid) DO UPDATE SET
                connector = excluded.connector,
                pin = excluded.pin,
                function = excluded.function,
                role = excluded.role,
                mio = excluded.mio,
                physical_channel = excluded.physical_channel,
                return_or_pair = excluded.return_or_pair,
                testable = excluded.testable,
                safety_classification = excluded.safety_classification,
                notes = excluded.notes,
                record_a = excluded.record_a,
                record_b = excluded.record_b,
                logical_card = excluded.logical_card,
                logical_channel = excluded.logical_channel,
                record_a_text = excluded.record_a_text,
                record_b_text = excluded.record_b_text,
                record_type = excluded.record_type,
                pair_relationship = excluded.pair_relationship,
                latest_result = excluded.latest_result;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$inputGuid", input.InputGuid.ToString("D"));
        command.Parameters.AddWithValue("$connector", input.Connector);
        command.Parameters.AddWithValue("$pin", input.Pin);
        command.Parameters.AddWithValue("$function", input.Function);
        command.Parameters.AddWithValue("$role", input.Role);
        command.Parameters.AddWithValue("$mio", input.Mio);
        command.Parameters.AddWithValue("$physicalChannel", input.PhysicalChannel);
        command.Parameters.AddWithValue("$returnOrPair", input.ReturnOrPair);
        command.Parameters.AddWithValue("$testable", input.Testable ? 1 : 0);
        command.Parameters.AddWithValue("$safety", input.SafetyClassification);
        command.Parameters.AddWithValue("$notes", input.Notes);
        AddNullableInt(command, "$recordA", input.RecordA);
        AddNullableInt(command, "$recordB", input.RecordB);
        AddNullableInt(command, "$logicalCard", input.LogicalCard);
        AddNullableInt(command, "$logicalChannel", input.LogicalChannel);
        AddNullableText(command, "$recordAText", input.RecordAText);
        AddNullableText(command, "$recordBText", input.RecordBText);
        AddNullableInt(command, "$recordType", input.RecordType);
        AddNullableText(command, "$pairRelationship", input.PairRelationship);
        command.Parameters.AddWithValue("$latestResult", input.LatestResult);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task FlushQueueAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _writeQueue.Writer.WriteAsync(new BarrierWorkItem(completion), cancellationToken).ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RaiseStatusChanged()
    {
        try
        {
            StatusChanged?.Invoke(this, new OtmrRecordingStatusChangedEventArgs(GetStatus()));
        }
        catch
        {
            // UI observers must never break serial/database recording.
        }
    }

    private static void AddNullableText(SqliteCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value);

    private static void AddNullableInt(SqliteCommand command, string name, int? value) =>
        command.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);

    private static string UtcText(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        try
        {
            if (IsRecording)
                await StopSessionAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);
        }
        catch
        {
            // Application shutdown should continue; the database remains WAL-safe.
        }

        _disposed = true;
        _writeQueue.Writer.TryComplete();
        try { await _writerTask.ConfigureAwait(false); } catch { }
        _lifecycleGate.Dispose();
    }

    private abstract record DbWorkItem;
    private sealed record RawEntryWorkItem(
        Guid SessionId,
        long Sequence,
        DateTimeOffset TimestampUtc,
        string Direction,
        byte[] Data,
        string? Interpretation) : DbWorkItem;
    private sealed record LiveFrameWorkItem(
        Guid SessionId,
        long Sequence,
        DateTimeOffset TimestampUtc,
        byte[] Data) : DbWorkItem;
    private sealed record RcmCaptureWorkItem(
        Guid SessionId,
        RcmInputSnapshot Input,
        string ElectricalState,
        DateTimeOffset? StartedUtc,
        DateTimeOffset? FinishedUtc,
        bool NoOtmrData,
        string CandidateRawSignature,
        IReadOnlyList<RcmFrameSnapshot> Frames) : DbWorkItem;
    private sealed record RcmComparisonWorkItem(
        Guid SessionId,
        RcmInputSnapshot Input,
        DateTimeOffset? ComparedUtc,
        string CommonFeaturesJson,
        string UniqueVoltageRemovedJson,
        string UniqueVoltageAppliedJson,
        string RepeatableDifferencesJson,
        string CandidateTransitionJson,
        bool DecoderVerified,
        string Result) : DbWorkItem;
    private sealed record BarrierWorkItem(TaskCompletionSource<bool> Completion) : DbWorkItem;
    private sealed record RcmFrameSnapshot(int Sequence, DateTimeOffset TimestampUtc, byte[] Data);

    private sealed record RcmInputSnapshot(
        Guid InputGuid,
        string Connector,
        string Pin,
        string Function,
        string Role,
        string Mio,
        string PhysicalChannel,
        string ReturnOrPair,
        bool Testable,
        string SafetyClassification,
        string Notes,
        int? RecordA,
        int? RecordB,
        int? LogicalCard,
        int? LogicalChannel,
        string? RecordAText,
        string? RecordBText,
        int? RecordType,
        string? PairRelationship,
        string LatestResult)
    {
        public static RcmInputSnapshot From(RcmPinProfile pin) => new(
            pin.Id,
            pin.Connector,
            pin.Pin,
            pin.Function,
            pin.Role,
            pin.Mio,
            pin.PhysicalChannel,
            pin.ReturnOrPair,
            pin.Testable,
            pin.SafetyClassification,
            pin.Notes,
            pin.CcfReference?.RecordA,
            pin.CcfReference?.RecordB,
            pin.CcfReference?.LogicalCard,
            pin.CcfReference?.LogicalChannel,
            pin.CcfReference?.RecordAText,
            pin.CcfReference?.RecordBText,
            pin.CcfReference?.RecordType,
            pin.CcfReference?.PairRelationship,
            pin.RcmResult);
    }
}
