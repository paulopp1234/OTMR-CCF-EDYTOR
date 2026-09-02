using System.Globalization;
using System.Text.Json;
using CcfEditor.Otmr.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CcfEditor.Otmr.Server;

public sealed record ServerUploadReceipt(
    Guid SessionId,
    int ApiVersion,
    string ManifestSha256,
    DateTimeOffset ReceivedUtc,
    long RawEntryCount,
    long LiveFrameCount,
    long RcmInputCount,
    long RcmCaptureCount,
    long RcmCaptureFrameCount,
    long RcmComparisonCount);

public enum ServerUploadDisposition { Inserted, AlreadyPresent, Conflict }

public sealed record ServerUploadResult(ServerUploadDisposition Disposition, ServerUploadReceipt Receipt);

public enum OtmrLiveUpdateDisposition
{
    Accepted,
    SourceConnectionMismatch,
    OlderSessionStart
}

public sealed record OtmrApplicationHeartbeatReceipt(
    Guid AppInstanceId,
    DateTimeOffset LastHeartbeatUtc);

public interface IOtmrUploadPersistenceHook
{
    Task BeforeReceiptAsync(OtmrApiV1UploadRequest request, CancellationToken cancellationToken);
}

public sealed class NoOpOtmrUploadPersistenceHook : IOtmrUploadPersistenceHook
{
    public Task BeforeReceiptAsync(OtmrApiV1UploadRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
}

public interface IOtmrServerDatabase
{
    string DatabasePath { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<ServerUploadResult> StoreUploadAsync(OtmrApiV1UploadRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OtmrVehicleSummary>> GetVehiclesAsync(int maximum, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OtmrServerSessionSummary>> GetSessionsAsync(string vehicleIdentifier, int offset, int maximum, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OtmrHistoricalRecord>> GetRecordsAsync(string vehicleIdentifier, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximum, CancellationToken cancellationToken = default);
    Task<OtmrVehicleConfiguration?> GetConfigurationAsync(string vehicleIdentifier, CancellationToken cancellationToken = default);
    Task<OtmrLiveUpdateDisposition> StoreLiveUpdateAsync(OtmrRealtimeUpdateRequest request, CancellationToken cancellationToken = default);
    Task<OtmrApplicationHeartbeatReceipt> StoreApplicationHeartbeatAsync(OtmrApplicationHeartbeatRequest request, CancellationToken cancellationToken = default);
    Task<OtmrLiveAvailability> GetLiveAsync(
        string vehicleIdentifier,
        DateTimeOffset nowUtc,
        TimeSpan staleAfter,
        TimeSpan windowsAppOfflineAfter,
        CancellationToken cancellationToken = default);
}

public sealed class OtmrServerDatabase(
    IOptions<OtmrServerOptions> options,
    IOtmrUploadPersistenceHook persistenceHook) : IOtmrServerDatabase
{
    public const int CurrentSchemaVersion = 3;
    private readonly OtmrServerOptions _options = options.Value;
    private readonly IOtmrUploadPersistenceHook _persistenceHook = persistenceHook;
    public string DatabasePath => _options.DatabasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(DatabasePath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServerUploadResult> StoreUploadAsync(OtmrApiV1UploadRequest request, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();

        ServerUploadReceipt? existing = await ReadReceiptAsync(connection, transaction, request.Session.SessionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            bool matches = ReceiptMatches(existing, request.Manifest, request.ApiVersion);
            transaction.Rollback();
            return new(matches ? ServerUploadDisposition.AlreadyPresent : ServerUploadDisposition.Conflict, existing);
        }

        if (await SessionExistsAsync(connection, transaction, request.Session.SessionId, cancellationToken).ConfigureAwait(false))
        {
            transaction.Rollback();
            return new(ServerUploadDisposition.Conflict, ReceiptFrom(request, DateTimeOffset.MinValue));
        }

        await InsertSessionAsync(connection, transaction, request.Session, cancellationToken).ConfigureAwait(false);
        foreach (OtmrApiV1RawEntry row in request.RawEntries)
            await InsertRawAsync(connection, transaction, row, cancellationToken).ConfigureAwait(false);
        foreach (OtmrApiV1LiveFrame row in request.LiveFrames)
            await InsertLiveAsync(connection, transaction, row, cancellationToken).ConfigureAwait(false);
        foreach (OtmrApiV1RcmInput row in request.Rcm.Inputs)
            await InsertInputAsync(connection, transaction, row, cancellationToken).ConfigureAwait(false);
        foreach (OtmrApiV1RcmCapture row in request.Rcm.Captures)
            await InsertCaptureAsync(connection, transaction, row, cancellationToken).ConfigureAwait(false);
        foreach (OtmrApiV1RcmCaptureFrame row in request.Rcm.CaptureFrames)
            await InsertCaptureFrameAsync(connection, transaction, row, cancellationToken).ConfigureAwait(false);
        foreach (OtmrApiV1RcmComparison row in request.Rcm.Comparisons)
            await InsertComparisonAsync(connection, transaction, row, cancellationToken).ConfigureAwait(false);

        await _persistenceHook.BeforeReceiptAsync(request, cancellationToken).ConfigureAwait(false);
        ServerUploadReceipt receipt = ReceiptFrom(request, DateTimeOffset.UtcNow);
        await InsertReceiptAsync(connection, transaction, receipt, cancellationToken).ConfigureAwait(false);
        transaction.Commit();
        return new(ServerUploadDisposition.Inserted, receipt);
    }

    public async Task<IReadOnlyList<OtmrVehicleSummary>> GetVehiclesAsync(int maximum, CancellationToken cancellationToken = default)
    {
        var result = new List<OtmrVehicleSummary>();
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT vehicle_identifier, MAX(vehicle_type), MAX(started_utc), COUNT(*)
            FROM recording_sessions
            WHERE vehicle_identifier IS NOT NULL AND vehicle_identifier <> ''
            GROUP BY vehicle_identifier
            ORDER BY MAX(started_utc) DESC
            LIMIT $maximum;
            """;
        command.Parameters.AddWithValue("$maximum", maximum);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetString(0), NullableText(reader, 1), ParseUtc(reader.GetString(2)), reader.GetInt32(3)));
        return result;
    }

    public async Task<IReadOnlyList<OtmrServerSessionSummary>> GetSessionsAsync(string vehicleIdentifier, int offset, int maximum, CancellationToken cancellationToken = default)
    {
        var result = new List<OtmrServerSessionSummary>();
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.session_id, s.started_utc, s.finished_utc, s.created_utc,
                   s.vehicle_identifier, s.vehicle_type, s.software_version, s.notes,
                   (SELECT COUNT(*) FROM raw_serial_entries r WHERE r.session_id = s.session_id),
                   (SELECT COUNT(*) FROM live_frames f WHERE f.session_id = s.session_id)
            FROM recording_sessions s
            WHERE s.vehicle_identifier = $vehicleIdentifier
            ORDER BY s.started_utc DESC
            LIMIT $maximum OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$vehicleIdentifier", vehicleIdentifier);
        command.Parameters.AddWithValue("$maximum", maximum);
        command.Parameters.AddWithValue("$offset", offset);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(Guid.Parse(reader.GetString(0)), ParseUtc(reader.GetString(1)), NullableUtc(reader, 2), ParseUtc(reader.GetString(3)), NullableText(reader, 4), NullableText(reader, 5), reader.GetString(6), NullableText(reader, 7), reader.GetInt64(8), reader.GetInt64(9)));
        return result;
    }

    public async Task<IReadOnlyList<OtmrHistoricalRecord>> GetRecordsAsync(string vehicleIdentifier, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximum, CancellationToken cancellationToken = default)
    {
        var result = new List<OtmrHistoricalRecord>();
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT f.session_id, f.sequence, f.timestamp_utc, f.data, f.decode_status, f.decoder_version
            FROM live_frames f
            INNER JOIN recording_sessions s ON s.session_id = f.session_id
            WHERE s.vehicle_identifier = $vehicleIdentifier
              AND f.timestamp_utc >= $fromUtc AND f.timestamp_utc < $toUtc
            ORDER BY f.timestamp_utc, f.session_id, f.sequence
            LIMIT $maximum;
            """;
        command.Parameters.AddWithValue("$vehicleIdentifier", vehicleIdentifier);
        command.Parameters.AddWithValue("$fromUtc", UtcText(fromUtc));
        command.Parameters.AddWithValue("$toUtc", UtcText(toUtc));
        command.Parameters.AddWithValue("$maximum", maximum);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetInt64(1), ParseUtc(reader.GetString(2)), (byte[])reader[3], reader.GetString(4), NullableText(reader, 5)));
        return result;
    }

    public async Task<OtmrVehicleConfiguration?> GetConfigurationAsync(string vehicleIdentifier, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, started_utc, vehicle_type, ccf_filename, ccf_sha256,
                   rcm_profile_filename, rcm_profile_sha256, rcm_profile_json
            FROM recording_sessions
            WHERE vehicle_identifier = $vehicleIdentifier
            ORDER BY started_utc DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$vehicleIdentifier", vehicleIdentifier);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new(vehicleIdentifier, Guid.Parse(reader.GetString(0)), ParseUtc(reader.GetString(1)), NullableText(reader, 2), NullableText(reader, 3), NullableText(reader, 4), NullableText(reader, 5), NullableText(reader, 6), NullableText(reader, 7));
    }

    public async Task<OtmrLiveUpdateDisposition> StoreLiveUpdateAsync(
        OtmrRealtimeUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using SqliteTransaction transaction = connection.BeginTransaction();

        string? currentSourceConnectionId = null;
        DateTimeOffset? currentAsOfUtc = null;
        await using (SqliteCommand current = connection.CreateCommand())
        {
            current.Transaction = transaction;
            current.CommandText = "SELECT source_connection_id, as_of_utc FROM live_vehicle_state WHERE vehicle_identifier=$vehicle;";
            Add(current, "$vehicle", request.VehicleIdentifier);
            await using SqliteDataReader reader = await current.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                currentSourceConnectionId = NullableText(reader, 0);
                currentAsOfUtc = ParseUtc(reader.GetString(1));
            }
        }

        bool sourceChanged = !string.Equals(
            currentSourceConnectionId, request.SourceConnectionId, StringComparison.Ordinal);
        if (!request.IsSessionStart && (currentAsOfUtc is null || sourceChanged))
        {
            transaction.Rollback();
            return OtmrLiveUpdateDisposition.SourceConnectionMismatch;
        }
        if (request.IsSessionStart && sourceChanged && currentAsOfUtc.HasValue &&
            request.TimestampUtc.ToUniversalTime() < currentAsOfUtc.Value)
        {
            transaction.Rollback();
            return OtmrLiveUpdateDisposition.OlderSessionStart;
        }

        DateTimeOffset effectiveAsOfUtc = !sourceChanged && currentAsOfUtc.HasValue &&
                                            currentAsOfUtc.Value > request.TimestampUtc.ToUniversalTime()
            ? currentAsOfUtc.Value
            : request.TimestampUtc.ToUniversalTime();

        await using (SqliteCommand vehicle = connection.CreateCommand())
        {
            vehicle.Transaction = transaction;
            vehicle.CommandText = """
                INSERT INTO live_vehicle_state(
                    vehicle_identifier, as_of_utc, received_utc, source_connection_id,
                    rcm_profile_filename, rcm_profile_sha256)
                VALUES($vehicle,$asOf,$received,$source,$profileFile,$profileHash)
                ON CONFLICT(vehicle_identifier) DO UPDATE SET
                    as_of_utc=excluded.as_of_utc,
                    received_utc=excluded.received_utc,
                    source_connection_id=excluded.source_connection_id,
                    rcm_profile_filename=excluded.rcm_profile_filename,
                    rcm_profile_sha256=excluded.rcm_profile_sha256;
                """;
            Add(vehicle, "$vehicle", request.VehicleIdentifier);
            Add(vehicle, "$asOf", UtcText(effectiveAsOfUtc));
            Add(vehicle, "$received", UtcText(DateTimeOffset.UtcNow));
            Add(vehicle, "$source", request.SourceConnectionId);
            Add(vehicle, "$profileFile", request.RcmProfileFilename);
            Add(vehicle, "$profileHash", request.RcmProfileSha256);
            await vehicle.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (request.IsSessionStart && sourceChanged)
        {
            await using SqliteCommand invalidate = connection.CreateCommand();
            invalidate.Transaction = transaction;
            invalidate.CommandText = "DELETE FROM live_signal_state WHERE vehicle_identifier=$vehicle;";
            Add(invalidate, "$vehicle", request.VehicleIdentifier);
            await invalidate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (OtmrRealtimeSignalUpdate signal in request.Signals)
        {
            await using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                INSERT INTO live_signal_state(
                    vehicle_identifier, signal_id, connector, pin, function,
                    logical_card, logical_channel, state, raw_value, observed_bit_value,
                    verification, updated_utc)
                VALUES($vehicle,$signal,$connector,$pin,$function,$card,$channel,$state,$raw,$bit,$verification,$updated)
                ON CONFLICT(vehicle_identifier,signal_id) DO UPDATE SET
                    connector=excluded.connector,
                    pin=excluded.pin,
                    function=excluded.function,
                    logical_card=excluded.logical_card,
                    logical_channel=excluded.logical_channel,
                    state=excluded.state,
                    raw_value=excluded.raw_value,
                    observed_bit_value=excluded.observed_bit_value,
                    verification=excluded.verification,
                    updated_utc=excluded.updated_utc;
                """;
            Add(update, "$vehicle", request.VehicleIdentifier);
            Add(update, "$signal", signal.SignalId.ToString("D"));
            Add(update, "$connector", signal.Connector);
            Add(update, "$pin", signal.Pin);
            Add(update, "$function", signal.Function);
            Add(update, "$card", signal.LogicalCard);
            Add(update, "$channel", signal.LogicalChannel);
            Add(update, "$state", signal.State);
            Add(update, "$raw", signal.RawValue);
            Add(update, "$bit", signal.ObservedBitValue);
            Add(update, "$verification", signal.Verification);
            Add(update, "$updated", UtcText(request.TimestampUtc));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        transaction.Commit();
        return OtmrLiveUpdateDisposition.Accepted;
    }

    public async Task<OtmrApplicationHeartbeatReceipt> StoreApplicationHeartbeatAsync(
        OtmrApplicationHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset receivedUtc = DateTimeOffset.UtcNow;
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO windows_app_presence(
                singleton_id, app_instance_id, last_heartbeat_utc, reported_utc,
                application_version, otmr_live_connected, vehicle_identifier, source_connection_id)
            VALUES(1,$instance,$lastSeen,$reported,$version,$live,$vehicle,$source)
            ON CONFLICT(singleton_id) DO UPDATE SET
                app_instance_id=excluded.app_instance_id,
                last_heartbeat_utc=excluded.last_heartbeat_utc,
                reported_utc=excluded.reported_utc,
                application_version=excluded.application_version,
                otmr_live_connected=excluded.otmr_live_connected,
                vehicle_identifier=excluded.vehicle_identifier,
                source_connection_id=excluded.source_connection_id;
            """;
        Add(command, "$instance", request.AppInstanceId.ToString("D"));
        Add(command, "$lastSeen", UtcText(receivedUtc));
        Add(command, "$reported", UtcText(request.TimestampUtc));
        Add(command, "$version", request.ApplicationVersion);
        Add(command, "$live", request.OtmrLiveConnected ? 1 : 0);
        Add(command, "$vehicle", request.VehicleIdentifier);
        Add(command, "$source", request.SourceConnectionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new(request.AppInstanceId, receivedUtc);
    }

    public async Task<OtmrLiveAvailability> GetLiveAsync(
        string vehicleIdentifier,
        DateTimeOffset nowUtc,
        TimeSpan staleAfter,
        TimeSpan windowsAppOfflineAfter,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? windowsLastSeenUtc = null;
        Guid? windowsInstanceId = null;
        string? windowsVersion = null;
        bool windowsReportedOtmrLive = false;
        string? windowsVehicleIdentifier = null;
        string? windowsSourceConnectionId = null;
        await using (SqliteCommand presence = connection.CreateCommand())
        {
            presence.CommandText = """
                SELECT app_instance_id, last_heartbeat_utc, application_version,
                       otmr_live_connected, vehicle_identifier, source_connection_id
                FROM windows_app_presence WHERE singleton_id=1;
                """;
            await using SqliteDataReader reader = await presence.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                windowsInstanceId = Guid.Parse(reader.GetString(0));
                windowsLastSeenUtc = ParseUtc(reader.GetString(1));
                windowsVersion = reader.GetString(2);
                windowsReportedOtmrLive = reader.GetInt32(3) != 0;
                windowsVehicleIdentifier = NullableText(reader, 4);
                windowsSourceConnectionId = NullableText(reader, 5);
            }
        }
        bool windowsOnline = windowsLastSeenUtc.HasValue &&
            nowUtc.ToUniversalTime() - windowsLastSeenUtc.Value <= windowsAppOfflineAfter;

        DateTimeOffset? asOfUtc;
        string? sourceConnectionId;
        string? profileFilename;
        string? profileSha256;
        await using (SqliteCommand vehicle = connection.CreateCommand())
        {
            vehicle.CommandText = """
                SELECT as_of_utc, source_connection_id, rcm_profile_filename, rcm_profile_sha256
                FROM live_vehicle_state WHERE vehicle_identifier=$vehicle;
                """;
            vehicle.Parameters.AddWithValue("$vehicle", vehicleIdentifier);
            await using SqliteDataReader reader = await vehicle.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new(vehicleIdentifier, false, null, false, false,
                    checked((int)staleAfter.TotalSeconds), null, null, null,
                    Array.Empty<OtmrLiveSignalState>(), windowsOnline,
                    windowsLastSeenUtc, windowsInstanceId, windowsVersion,
                    checked((int)windowsAppOfflineAfter.TotalSeconds));
            }
            asOfUtc = ParseUtc(reader.GetString(0));
            sourceConnectionId = NullableText(reader, 1);
            profileFilename = NullableText(reader, 2);
            profileSha256 = NullableText(reader, 3);
        }

        bool otmrOnline = windowsOnline && windowsReportedOtmrLive &&
            string.Equals(windowsVehicleIdentifier, vehicleIdentifier, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(sourceConnectionId) &&
            string.Equals(windowsSourceConnectionId, sourceConnectionId, StringComparison.Ordinal);
        var signals = new List<OtmrLiveSignalState>();
        if (otmrOnline)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                    SELECT signal_id, connector, pin, function, logical_card, logical_channel,
                           state, raw_value, observed_bit_value, verification, updated_utc
                    FROM live_signal_state
                    WHERE vehicle_identifier=$vehicle
                    ORDER BY connector, pin, signal_id;
                    """;
            command.Parameters.AddWithValue("$vehicle", vehicleIdentifier);
            await using SqliteDataReader signalReader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await signalReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                signals.Add(new(
                    Guid.Parse(signalReader.GetString(0)),
                    signalReader.GetString(1),
                    signalReader.GetString(2),
                    signalReader.GetString(3),
                    signalReader.IsDBNull(4) ? null : signalReader.GetInt32(4),
                    signalReader.IsDBNull(5) ? null : signalReader.GetInt32(5),
                    signalReader.GetString(6),
                    signalReader.GetInt32(7),
                    signalReader.IsDBNull(8) ? null : signalReader.GetInt32(8),
                    signalReader.GetString(9),
                    ParseUtc(signalReader.GetString(10))));
            }
        }

        bool stale = !otmrOnline;
        return new(vehicleIdentifier, otmrOnline, asOfUtc, stale, otmrOnline,
            checked((int)staleAfter.TotalSeconds), sourceConnectionId,
            profileFilename, profileSha256, signals, windowsOnline,
            windowsLastSeenUtc, windowsInstanceId, windowsVersion,
            checked((int)windowsAppOfflineAfter.TotalSeconds));
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, Pooling = true };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<bool> SessionExistsAsync(SqliteConnection c, SqliteTransaction t, Guid id, CancellationToken ct)
    {
        await using SqliteCommand command = c.CreateCommand(); command.Transaction = t;
        command.CommandText = "SELECT 1 FROM recording_sessions WHERE session_id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static async Task<ServerUploadReceipt?> ReadReceiptAsync(SqliteConnection c, SqliteTransaction t, Guid id, CancellationToken ct)
    {
        await using SqliteCommand command = c.CreateCommand(); command.Transaction = t;
        command.CommandText = "SELECT api_version, manifest_sha256, received_utc, raw_entry_count, live_frame_count, rcm_input_count, rcm_capture_count, rcm_capture_frame_count, rcm_comparison_count FROM server_upload_receipts WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new(id, reader.GetInt32(0), reader.GetString(1), ParseUtc(reader.GetString(2)), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8))
            : null;
    }

    private static bool ReceiptMatches(ServerUploadReceipt r, OtmrApiV1Manifest m, int apiVersion) =>
        r.ApiVersion == apiVersion && string.Equals(r.ManifestSha256, m.ContentSha256, StringComparison.OrdinalIgnoreCase) &&
        r.RawEntryCount == m.RawEntryCount && r.LiveFrameCount == m.LiveFrameCount && r.RcmInputCount == m.RcmInputCount &&
        r.RcmCaptureCount == m.RcmCaptureCount && r.RcmCaptureFrameCount == m.RcmCaptureFrameCount && r.RcmComparisonCount == m.RcmComparisonCount;

    private static ServerUploadReceipt ReceiptFrom(OtmrApiV1UploadRequest r, DateTimeOffset received) => new(
        r.Session.SessionId, r.ApiVersion, r.Manifest.ContentSha256, received, r.Manifest.RawEntryCount, r.Manifest.LiveFrameCount,
        r.Manifest.RcmInputCount, r.Manifest.RcmCaptureCount, r.Manifest.RcmCaptureFrameCount, r.Manifest.RcmComparisonCount);

    private static async Task InsertSessionAsync(SqliteConnection c, SqliteTransaction t, OtmrApiV1Session x, CancellationToken ct)
    {
        await using SqliteCommand q = c.CreateCommand(); q.Transaction = t;
        q.CommandText = """
            INSERT INTO recording_sessions (session_id, started_utc, finished_utc, software_version, com_port, serial_settings, vehicle_identifier, vehicle_type, ccf_filename, ccf_sha256, rcm_profile_filename, rcm_profile_sha256, rcm_profile_json, sync_state, remote_session_id, created_utc, notes)
            VALUES ($id,$started,$finished,$software,$port,$serial,$vehicle,$type,$ccfFile,$ccfHash,$rcmFile,$rcmHash,$rcmJson,'UPLOADED',$remote,$created,$notes);
            """;
        Add(q,"$id",x.SessionId.ToString("D")); Add(q,"$started",UtcText(x.StartedUtc)); Add(q,"$finished",x.FinishedUtc is null ? null : UtcText(x.FinishedUtc.Value)); Add(q,"$software",x.SoftwareVersion); Add(q,"$port",x.ComPort); Add(q,"$serial",x.SerialSettings); Add(q,"$vehicle",x.VehicleIdentifier); Add(q,"$type",x.VehicleType); Add(q,"$ccfFile",x.CcfFilename); Add(q,"$ccfHash",x.CcfSha256); Add(q,"$rcmFile",x.RcmProfileFilename); Add(q,"$rcmHash",x.RcmProfileSha256); Add(q,"$rcmJson",x.RcmProfileJsonSnapshot); Add(q,"$remote",x.SessionId.ToString("D")); Add(q,"$created",UtcText(x.CreatedUtc)); Add(q,"$notes",x.Notes);
        await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertRawAsync(SqliteConnection c, SqliteTransaction t, OtmrApiV1RawEntry x, CancellationToken ct)
    {
        await using SqliteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="INSERT INTO raw_serial_entries(session_id,sequence,timestamp_utc,direction,data,interpretation) VALUES($sid,$seq,$at,$dir,$data,$text);";
        Add(q,"$sid",x.SessionId.ToString("D")); Add(q,"$seq",x.Sequence); Add(q,"$at",UtcText(x.TimestampUtc)); Add(q,"$dir",x.Direction); AddBlob(q,"$data",x.Data); Add(q,"$text",x.Interpretation); await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertLiveAsync(SqliteConnection c, SqliteTransaction t, OtmrApiV1LiveFrame x, CancellationToken ct)
    {
        await using SqliteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="INSERT INTO live_frames(session_id,sequence,timestamp_utc,data,decode_status,decoder_version) VALUES($sid,$seq,$at,$data,$status,$version);";
        Add(q,"$sid",x.SessionId.ToString("D")); Add(q,"$seq",x.Sequence); Add(q,"$at",UtcText(x.TimestampUtc)); AddBlob(q,"$data",x.Data); Add(q,"$status",x.DecodeStatus); Add(q,"$version",x.DecoderVersion); await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertInputAsync(SqliteConnection c, SqliteTransaction t, OtmrApiV1RcmInput x, CancellationToken ct)
    {
        await using SqliteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="""
            INSERT INTO rcm_input_tests(session_id,input_guid,connector,pin,function,role,mio,physical_channel,return_or_pair,testable,safety_classification,notes,record_a,record_b,logical_card,logical_channel,record_a_text,record_b_text,record_type,pair_relationship,latest_result)
            VALUES($sid,$iid,$connector,$pin,$function,$role,$mio,$channel,$pair,$testable,$safety,$notes,$a,$b,$card,$logical,$aText,$bText,$type,$relationship,$result);
            """;
        Add(q,"$sid",x.SessionId.ToString("D")); Add(q,"$iid",x.InputGuid.ToString("D")); Add(q,"$connector",x.Connector); Add(q,"$pin",x.Pin); Add(q,"$function",x.Function); Add(q,"$role",x.Role); Add(q,"$mio",x.Mio); Add(q,"$channel",x.PhysicalChannel); Add(q,"$pair",x.ReturnOrPair); Add(q,"$testable",x.Testable?1:0); Add(q,"$safety",x.SafetyClassification); Add(q,"$notes",x.Notes); Add(q,"$a",x.RecordA); Add(q,"$b",x.RecordB); Add(q,"$card",x.LogicalCard); Add(q,"$logical",x.LogicalChannel); Add(q,"$aText",x.RecordAText); Add(q,"$bText",x.RecordBText); Add(q,"$type",x.RecordType); Add(q,"$relationship",x.PairRelationship); Add(q,"$result",x.LatestResult); await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertCaptureAsync(SqliteConnection c, SqliteTransaction t, OtmrApiV1RcmCapture x, CancellationToken ct)
    {
        await using SqliteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="INSERT INTO rcm_capture_windows(capture_id,session_id,input_guid,electrical_state,started_utc,finished_utc,frame_count,no_otmr_data,candidate_raw_signature,created_utc) VALUES($cid,$sid,$iid,$state,$started,$finished,$count,$none,$signature,$created);";
        Add(q,"$cid",x.CaptureId.ToString("D")); Add(q,"$sid",x.SessionId.ToString("D")); Add(q,"$iid",x.InputGuid.ToString("D")); Add(q,"$state",x.ElectricalState); Add(q,"$started",x.StartedUtc is null?null:UtcText(x.StartedUtc.Value)); Add(q,"$finished",x.FinishedUtc is null?null:UtcText(x.FinishedUtc.Value)); Add(q,"$count",x.FrameCount); Add(q,"$none",x.NoOtmrData?1:0); Add(q,"$signature",x.CandidateRawSignature); Add(q,"$created",UtcText(x.CreatedUtc)); await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertCaptureFrameAsync(SqliteConnection c, SqliteTransaction t, OtmrApiV1RcmCaptureFrame x, CancellationToken ct)
    {
        await using SqliteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="INSERT INTO rcm_capture_frames(capture_id,sequence,timestamp_utc,data) VALUES($cid,$seq,$at,$data);";
        Add(q,"$cid",x.CaptureId.ToString("D")); Add(q,"$seq",x.Sequence); Add(q,"$at",UtcText(x.TimestampUtc)); AddBlob(q,"$data",x.Data); await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertComparisonAsync(SqliteConnection c, SqliteTransaction t, OtmrApiV1RcmComparison x, CancellationToken ct)
    {
        await using SqliteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="INSERT INTO rcm_comparisons(session_id,input_guid,compared_utc,common_features_json,unique_voltage_removed_json,unique_voltage_applied_json,repeatable_differences_json,candidate_transition_json,decoder_verified,result) VALUES($sid,$iid,$at,$common,$removed,$applied,$repeatable,$transitions,$verified,$result);";
        Add(q,"$sid",x.SessionId.ToString("D")); Add(q,"$iid",x.InputGuid.ToString("D")); Add(q,"$at",x.ComparedUtc is null?null:UtcText(x.ComparedUtc.Value)); Add(q,"$common",JsonSerializer.Serialize(x.CommonFeatures,OtmrApiV1Json.Options)); Add(q,"$removed",JsonSerializer.Serialize(x.UniqueVoltageRemoved,OtmrApiV1Json.Options)); Add(q,"$applied",JsonSerializer.Serialize(x.UniqueVoltageApplied,OtmrApiV1Json.Options)); Add(q,"$repeatable",JsonSerializer.Serialize(x.RepeatableDifferences,OtmrApiV1Json.Options)); Add(q,"$transitions",JsonSerializer.Serialize(x.CandidateTransitions,OtmrApiV1Json.Options)); Add(q,"$verified",x.DecoderVerified?1:0); Add(q,"$result",x.Result); await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertReceiptAsync(SqliteConnection c, SqliteTransaction t, ServerUploadReceipt x, CancellationToken ct)
    {
        await using SqliteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="INSERT INTO server_upload_receipts(session_id,api_version,manifest_sha256,received_utc,raw_entry_count,live_frame_count,rcm_input_count,rcm_capture_count,rcm_capture_frame_count,rcm_comparison_count) VALUES($sid,$version,$hash,$at,$raw,$live,$input,$capture,$frames,$comparison);";
        Add(q,"$sid",x.SessionId.ToString("D")); Add(q,"$version",x.ApiVersion); Add(q,"$hash",x.ManifestSha256); Add(q,"$at",UtcText(x.ReceivedUtc)); Add(q,"$raw",x.RawEntryCount); Add(q,"$live",x.LiveFrameCount); Add(q,"$input",x.RcmInputCount); Add(q,"$capture",x.RcmCaptureCount); Add(q,"$frames",x.RcmCaptureFrameCount); Add(q,"$comparison",x.RcmComparisonCount); await q.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void Add(SqliteCommand q,string name,object? value)=>q.Parameters.AddWithValue(name,value??DBNull.Value);
    private static void AddBlob(SqliteCommand q,string name,byte[] value)=>q.Parameters.Add(name,SqliteType.Blob).Value=value;
    private static string? NullableText(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static DateTimeOffset? NullableUtc(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:ParseUtc(r.GetString(i));
    private static DateTimeOffset ParseUtc(string value)=>DateTimeOffset.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static string UtcText(DateTimeOffset value)=>value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);

    private const string SchemaSql = """
        PRAGMA journal_mode=WAL;
        PRAGMA foreign_keys=ON;
        PRAGMA busy_timeout=5000;
        CREATE TABLE IF NOT EXISTS schema_info(schema_version INTEGER NOT NULL, applied_utc TEXT NOT NULL);
        INSERT INTO schema_info(schema_version,applied_utc) SELECT 1,strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE NOT EXISTS(SELECT 1 FROM schema_info);
        CREATE TABLE IF NOT EXISTS recording_sessions(
          session_id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, finished_utc TEXT NULL, software_version TEXT NOT NULL,
          com_port TEXT NOT NULL, serial_settings TEXT NOT NULL, vehicle_identifier TEXT NULL, vehicle_type TEXT NULL,
          ccf_filename TEXT NULL, ccf_sha256 TEXT NULL, rcm_profile_filename TEXT NULL, rcm_profile_sha256 TEXT NULL,
          rcm_profile_json TEXT NULL, sync_state TEXT NOT NULL, remote_session_id TEXT NULL, created_utc TEXT NOT NULL, notes TEXT NULL);
        CREATE TABLE IF NOT EXISTS raw_serial_entries(
          session_id TEXT NOT NULL, sequence INTEGER NOT NULL, timestamp_utc TEXT NOT NULL, direction TEXT NOT NULL,
          data BLOB NOT NULL, interpretation TEXT NULL, PRIMARY KEY(session_id,sequence), FOREIGN KEY(session_id) REFERENCES recording_sessions(session_id) ON DELETE RESTRICT);
        CREATE TABLE IF NOT EXISTS live_frames(
          session_id TEXT NOT NULL, sequence INTEGER NOT NULL, timestamp_utc TEXT NOT NULL, data BLOB NOT NULL,
          decode_status TEXT NOT NULL, decoder_version TEXT NULL, PRIMARY KEY(session_id,sequence), FOREIGN KEY(session_id) REFERENCES recording_sessions(session_id) ON DELETE RESTRICT);
        CREATE TABLE IF NOT EXISTS rcm_input_tests(
          session_id TEXT NOT NULL, input_guid TEXT NOT NULL, connector TEXT NOT NULL, pin TEXT NOT NULL, function TEXT NOT NULL,
          role TEXT NOT NULL, mio TEXT NOT NULL, physical_channel TEXT NOT NULL, return_or_pair TEXT NOT NULL, testable INTEGER NOT NULL,
          safety_classification TEXT NOT NULL, notes TEXT NOT NULL, record_a INTEGER NULL, record_b INTEGER NULL, logical_card INTEGER NULL,
          logical_channel INTEGER NULL, record_a_text TEXT NULL, record_b_text TEXT NULL, record_type INTEGER NULL,
          pair_relationship TEXT NULL, latest_result TEXT NOT NULL, PRIMARY KEY(session_id,input_guid), FOREIGN KEY(session_id) REFERENCES recording_sessions(session_id) ON DELETE RESTRICT);
        CREATE TABLE IF NOT EXISTS rcm_capture_windows(
          capture_id TEXT PRIMARY KEY, session_id TEXT NOT NULL, input_guid TEXT NOT NULL, electrical_state TEXT NOT NULL,
          started_utc TEXT NULL, finished_utc TEXT NULL, frame_count INTEGER NOT NULL, no_otmr_data INTEGER NOT NULL,
          candidate_raw_signature TEXT NULL, created_utc TEXT NOT NULL,
          FOREIGN KEY(session_id,input_guid) REFERENCES rcm_input_tests(session_id,input_guid) ON DELETE RESTRICT);
        CREATE TABLE IF NOT EXISTS rcm_capture_frames(
          capture_id TEXT NOT NULL, sequence INTEGER NOT NULL, timestamp_utc TEXT NOT NULL, data BLOB NOT NULL,
          PRIMARY KEY(capture_id,sequence), FOREIGN KEY(capture_id) REFERENCES rcm_capture_windows(capture_id) ON DELETE RESTRICT);
        CREATE TABLE IF NOT EXISTS rcm_comparisons(
          session_id TEXT NOT NULL, input_guid TEXT NOT NULL, compared_utc TEXT NULL, common_features_json TEXT NOT NULL,
          unique_voltage_removed_json TEXT NOT NULL, unique_voltage_applied_json TEXT NOT NULL, repeatable_differences_json TEXT NOT NULL,
          candidate_transition_json TEXT NOT NULL, decoder_verified INTEGER NOT NULL, result TEXT NOT NULL,
          PRIMARY KEY(session_id,input_guid), FOREIGN KEY(session_id,input_guid) REFERENCES rcm_input_tests(session_id,input_guid) ON DELETE RESTRICT);
        CREATE TABLE IF NOT EXISTS server_upload_receipts(
          session_id TEXT PRIMARY KEY, api_version INTEGER NOT NULL, manifest_sha256 TEXT NOT NULL, received_utc TEXT NOT NULL,
          raw_entry_count INTEGER NOT NULL, live_frame_count INTEGER NOT NULL, rcm_input_count INTEGER NOT NULL,
          rcm_capture_count INTEGER NOT NULL, rcm_capture_frame_count INTEGER NOT NULL, rcm_comparison_count INTEGER NOT NULL,
          FOREIGN KEY(session_id) REFERENCES recording_sessions(session_id) ON DELETE RESTRICT);
        CREATE TABLE IF NOT EXISTS live_vehicle_state(
          vehicle_identifier TEXT PRIMARY KEY, as_of_utc TEXT NOT NULL, received_utc TEXT NOT NULL,
          source_connection_id TEXT NULL, rcm_profile_filename TEXT NULL, rcm_profile_sha256 TEXT NULL);
        CREATE TABLE IF NOT EXISTS live_signal_state(
          vehicle_identifier TEXT NOT NULL, signal_id TEXT NOT NULL, connector TEXT NOT NULL, pin TEXT NOT NULL,
          function TEXT NOT NULL, logical_card INTEGER NULL, logical_channel INTEGER NULL, state TEXT NOT NULL,
          raw_value INTEGER NOT NULL, observed_bit_value INTEGER NULL, verification TEXT NOT NULL, updated_utc TEXT NOT NULL,
          PRIMARY KEY(vehicle_identifier,signal_id),
          FOREIGN KEY(vehicle_identifier) REFERENCES live_vehicle_state(vehicle_identifier) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS windows_app_presence(
          singleton_id INTEGER PRIMARY KEY CHECK(singleton_id=1),
          app_instance_id TEXT NOT NULL,
          last_heartbeat_utc TEXT NOT NULL,
          reported_utc TEXT NOT NULL,
          application_version TEXT NOT NULL,
          otmr_live_connected INTEGER NOT NULL,
          vehicle_identifier TEXT NULL,
          source_connection_id TEXT NULL);
        CREATE INDEX IF NOT EXISTS ix_recording_sessions_vehicle_started ON recording_sessions(vehicle_identifier,started_utc DESC);
        CREATE INDEX IF NOT EXISTS ix_live_frames_timestamp ON live_frames(timestamp_utc);
        CREATE INDEX IF NOT EXISTS ix_live_signal_vehicle ON live_signal_state(vehicle_identifier);
        UPDATE schema_info SET schema_version=3, applied_utc=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE schema_version<3;
        PRAGMA user_version=3;
        """;
}
