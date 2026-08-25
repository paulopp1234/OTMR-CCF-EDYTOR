using System.Globalization;
using System.Text;
using CcfEditor.Core;
using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.HardwareValidation;

internal static class Program
{
    private const string PortName = "COM2";
    private static readonly byte[] FinalLiveStart =
        { 0x01, 0x07, 0x00, 0x01, 0x01, 0x01, 0x02, 0x01, 0x02, 0x13, 0x03, 0x13, 0x04 };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: CcfEditor.HardwareValidation <selected.ccf> <new-log-path>");
            return 64;
        }

        string ccfPath = Path.GetFullPath(args[0]);
        string logPath = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        using var log = new EvidenceLog(logPath);
        log.Write("CLASS 171 OTMR CONTROLLED REAL-HARDWARE VALIDATION");
        log.Write("SAFETY: ONE START attempt only; no retry is implemented by this harness.");

        CcfDocument ccf;
        try
        {
            ccf = CcfParser.Load(ccfPath);
        }
        catch (Exception ex)
        {
            log.Write("CCF_LOAD_FAILED: " + ex);
            return 2;
        }

        byte[] ccfBytes = ccf.GetWorkingBytesSnapshot();
        CcfValidationIssue[] ccfErrors = CcfValidator.ValidateMilestone1(ccf)
            .Where(issue => issue.Severity == CcfValidationSeverity.Error)
            .ToArray();
        string vehicleType = ReadAscii(ccfBytes, CcfFieldDefinitions.Header.VehicleType, 8);
        bool class171 = string.Equals(vehicleType, "171", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(vehicleType, "Class 171", StringComparison.OrdinalIgnoreCase);

        log.Write($"SELECTED_CCF_PATH: {ccfPath}");
        log.Write($"SELECTED_CCF_NAME: {Path.GetFileName(ccfPath)}");
        log.Write($"CCF_VEHICLE_TYPE: {vehicleType}");
        log.Write($"CCF_VALIDATION_ERRORS: {ccfErrors.Length}");
        foreach (CcfValidationIssue error in ccfErrors)
            log.Write("CCF_ERROR: " + error.Message);
        log.Write($"CCF_CLASS171_RESULT: {(class171 && ccfErrors.Length == 0 ? "PASS" : "FAIL")}");
        log.Write($"CCF_0x0231: 0x{ccfBytes[0x0231]:X2}");
        log.Write($"CCF_0x0263: 0x{ccfBytes[0x0263]:X2}");

        Console.WriteLine($"Selected CCF: {ccfPath}");
        Console.WriteLine($"Vehicle/type validation: {(class171 && ccfErrors.Length == 0 ? "PASS" : "FAIL")} ({vehicleType})");
        Console.WriteLine($"CCF[0x0231] = 0x{ccfBytes[0x0231]:X2}");
        Console.WriteLine($"CCF[0x0263] = 0x{ccfBytes[0x0263]:X2}");
        if (!class171 || ccfErrors.Length != 0)
        {
            log.Write("STOP: selected CCF failed validation; COM was not opened and no TX occurred.");
            return 3;
        }

        string[] ports = SerialOtmrTransport.GetAvailablePorts();
        bool portExists = ports.Contains(PortName, StringComparer.OrdinalIgnoreCase);
        log.Write($"AVAILABLE_PORTS: {string.Join(", ", ports)}");
        log.Write($"COM2_EXISTS: {(portExists ? "YES" : "NO")}");
        if (!portExists)
        {
            log.Write("STOP: COM2 is not enumerated; no TX occurred.");
            return 4;
        }

        using var transport = new SerialOtmrTransport();
        using var service = new OtmrLiveService(transport);
        var firstTenFrames = new List<(DateTimeOffset Timestamp, byte[] Data)>();
        var rawRecords = new List<RawRecord>();
        var sync = new object();
        var tenFrames = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DateTimeOffset? finalLiveTxAt = null;
        DateTimeOffset? firstLiveFrameAt = null;
        DateTimeOffset? liveClosedAt = null;
        DateTimeOffset? restoreOpenedAt = null;
        int frameCount = 0;
        int connectionNumber = 0;
        int liveFrameCountAtClose = -1;
        int liveFrameCountAtRestoreOpen = -1;
        bool comOpened = false;
        bool startAttempted = false;
        bool stopLiveSucceeded = false;
        bool restorationSucceeded = false;
        Exception? failure = null;

        service.StateChanged += (_, e) => log.Write($"STATE: {e.State}");
        service.DiagnosticAdded += (_, e) => log.Write("DIAGNOSTIC: " + e.Entry.Message, e.Entry.Timestamp);
        service.ErrorOccurred += (_, e) => log.Write("SERVICE_ERROR: " + e.Exception);
        service.ConnectionChanged += (_, e) =>
        {
            if (e.IsConnected)
            {
                int number = Interlocked.Increment(ref connectionNumber);
                string settings = number == 2
                    ? "38400/8/N/1 Handshake.None RTS LOW DTR HIGH"
                    : "38400/8/N/1 Handshake.None RTS LOW DTR LOW";
                if (number == 3)
                {
                    restoreOpenedAt = DateTimeOffset.Now;
                    liveFrameCountAtRestoreOpen = Volatile.Read(ref frameCount);
                }
                log.Write($"COM_OPEN #{number}: {e.PortName} {settings}");
            }
            else
            {
                log.Write("COM_CLOSE");
            }
        };
        service.CaptureAdded += (_, e) =>
        {
            OtmrCaptureEntry entry = e.Entry;
            lock (sync)
                rawRecords.Add(new RawRecord(entry.Timestamp, entry.Direction, entry.GetDataSnapshot(), service.State));
            log.Write($"RAW {entry.Direction.ToString().ToUpperInvariant()}: {entry.Hex}" +
                      (string.IsNullOrWhiteSpace(entry.Interpretation) ? string.Empty : $" | {entry.Interpretation}"), entry.Timestamp);
            if (entry.Direction == OtmrDirection.Tx && entry.Data.Span.SequenceEqual(FinalLiveStart) && finalLiveTxAt is null)
            {
                finalLiveTxAt = entry.Timestamp;
                log.Write("FINAL_START_01_07_TX_TIMESTAMP_RECORDED", entry.Timestamp);
            }
        };
        service.FrameReceived += (_, e) =>
        {
            int number = Interlocked.Increment(ref frameCount);
            firstLiveFrameAt ??= e.Timestamp;
            lock (sync)
            {
                if (firstTenFrames.Count < 10)
                    firstTenFrames.Add((e.Timestamp, e.Frame.GetDataSnapshot()));
            }
            log.Write($"LIVE_FRAME {number}: {e.Frame.Hex}", e.Timestamp);
            if (number >= 10)
                tenFrames.TrySetResult();
        };

        try
        {
            log.Write("CONNECT_REQUEST: COM2 38400/8/N/1 Handshake.None RTS LOW DTR LOW");
            try
            {
                await service.ConnectAsync(OtmrSerialSettings.Class171Bench(PortName));
                comOpened = true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                failure = ex;
                log.Write("STOP_COM2_OPEN_FAILED_OR_BUSY: " + ex);
                Console.WriteLine("COM2 open failed or is busy. No process was terminated and no TX occurred.");
                return 5;
            }

            int txAfterConnect = service.GetCaptureSnapshot().Count(entry => entry.Direction == OtmrDirection.Tx);
            log.Write($"CONNECT_NO_TX_CHECK: {(txAfterConnect == 0 ? "PASS" : "FAIL")} (TX count {txAfterConnect})");
            if (txAfterConnect != 0)
                throw new InvalidOperationException("Connect emitted unexpected serial TX; refusing START.");

            Console.WriteLine("COM2 opened successfully with RTS LOW / DTR LOW; Connect TX count is 0.");
            Console.WriteLine("PRE-START HOLD: type START once to release the sole hardware START attempt.");
            string? confirmation = Console.ReadLine();
            if (!string.Equals(confirmation, "START", StringComparison.Ordinal))
            {
                log.Write("STOP: START confirmation was not supplied; no interrogation or configuration TX occurred.");
                return 6;
            }

            startAttempted = true;
            log.Write("START_ATTEMPT: 1 OF 1");
            await service.StartLiveAsync(ccf);
            log.Write($"START_METHOD_RETURNED: state={service.State}");

            Task completed = await Task.WhenAny(tenFrames.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != tenFrames.Task)
                throw new TimeoutException($"Timed out after 30 seconds with {Volatile.Read(ref frameCount)} complete FB FB ... FF frames.");
            await tenFrames.Task;
            log.Write($"LIVE_VALIDATION_THRESHOLD_REACHED: {Volatile.Read(ref frameCount)} complete frames");

            liveFrameCountAtClose = Volatile.Read(ref frameCount);
            await service.StopLiveAsync();
            liveClosedAt = DateTimeOffset.Now;
            stopLiveSucceeded = true;
            log.Write($"STOP_LIVE_RESULT: PASS; frames at close={liveFrameCountAtClose}; no stop TX");

            await service.StopAndRestoreAsync();
            restorationSucceeded = true;
            log.Write("RESTORATION_RESULT: PASS; captured cleanup exchange completed; COM remains closed");
        }
        catch (Exception ex)
        {
            failure = ex;
            log.Write("VALIDATION_FAILURE: " + ex);
            Console.Error.WriteLine(ex);
        }
        finally
        {
            if (service.IsConnected)
            {
                try
                {
                    await service.DisconnectAsync();
                    log.Write("FAILSAFE_COM_CLOSE: completed without transmitting protocol data");
                }
                catch (Exception closeEx)
                {
                    log.Write("FAILSAFE_COM_CLOSE_FAILED: " + closeEx);
                }
            }

            List<RawRecord> rawSnapshot;
            List<(DateTimeOffset Timestamp, byte[] Data)> frameSnapshot;
            lock (sync)
            {
                rawSnapshot = rawRecords.ToList();
                frameSnapshot = firstTenFrames.ToList();
            }

            IReadOnlyList<byte[]> protocolFrames = ExtractProtocolFrames(rawSnapshot);
            byte[]? recorderPage01 = protocolFrames.FirstOrDefault(frame => frame.Length == 0x10B && frame[1] == 0x02);
            string recorderIdentity = recorderPage01 is null ? "NOT OBTAINED" : DescribeRecorderIdentity(recorderPage01);
            IReadOnlyList<byte> d2Values = ExtractInterFrameD2Values(rawSnapshot, finalLiveTxAt, liveClosedAt);
            int startWrites = rawSnapshot.Count(record =>
                record.Direction == OtmrDirection.Tx && record.State is >= OtmrLiveState.PreflightingConfiguration and <= OtmrLiveState.WaitingFor01_12 &&
                record.Data.Length > 1 && record.Data[0] == 0x01 &&
                (record.Data.Length is 0x10B or 0xD2 || (record.Data.Length == 13 && record.Data[1] == 0x0C)));
            int restoreWrites = service.GetCaptureSnapshot().Count(entry =>
                entry.Direction == OtmrDirection.Tx &&
                entry.Interpretation?.StartsWith("RESTORE WRITE", StringComparison.Ordinal) == true);
            bool reply13Received = protocolFrames.Any(frame => frame.Length == 13 && frame[1] == 0x13 && frame.SequenceEqual(new byte[]
                { 0x01, 0x13, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x06, 0x03, 0x06, 0x04 }));
            int finalStartCount = rawSnapshot.Count(record => record.Direction == OtmrDirection.Tx && record.Data.SequenceEqual(FinalLiveStart));

            log.Write("FINAL HARDWARE CYCLE SUMMARY");
            log.Write($"COM2_OPENED: {(comOpened ? "YES" : "NO")}");
            log.Write($"SELECTED_CCF: {ccfPath}");
            log.Write($"RECORDER_IDENTITY: {recorderIdentity}");
            log.Write($"START_ATTEMPTED: {(startAttempted ? "YES (exactly once)" : "NO")}");
            log.Write($"PREFLIGHT_PASSED: {(service.GetDiagnosticSnapshot().Any(d => d.Message.StartsWith("START preflight passed", StringComparison.Ordinal)) ? "YES" : "NO")}");
            log.Write($"START_WRITES_COMPLETED: {startWrites}/7");
            log.Write($"01_13_RECEIVED_ANY_PHASE: {(reply13Received ? "YES" : "NO")}");
            log.Write($"FINAL_01_07_TX_COUNT_ALL_PHASES: {finalStartCount}");
            log.Write($"LIVE_COM_REOPEN_SUCCEEDED: {(connectionNumber >= 2 ? "YES" : "NO")}");
            log.Write($"VALID_FB_FRAMES_RECEIVED_BEFORE_STOP: {Math.Max(liveFrameCountAtClose, Volatile.Read(ref frameCount))}");
            log.Write($"LIVE_ACTIVE_ACHIEVED: {(firstLiveFrameAt.HasValue ? "YES" : "NO")}");
            log.Write($"FINAL_01_07_TO_FIRST_FRAME_MS: {(finalLiveTxAt.HasValue && firstLiveFrameAt.HasValue ? (firstLiveFrameAt.Value - finalLiveTxAt.Value).TotalMilliseconds.ToString("F4", CultureInfo.InvariantCulture) : "N/A")}");
            log.Write($"D2_VALUES_BETWEEN_FRAMES: {(d2Values.Count == 0 ? "NONE OBSERVED" : string.Join(" ", d2Values.Select(value => $"D2 {value:X2}")))}");
            for (int index = 0; index < frameSnapshot.Count; index++)
                log.Write($"FIRST_FRAME_{index + 1}: {Hex(frameSnapshot[index].Data)}", frameSnapshot[index].Timestamp);
            log.Write($"STOP_LIVE_SUCCEEDED: {(stopLiveSucceeded ? "YES" : "NO")}");
            log.Write($"FRAMES_AT_LIVE_CLOSE: {liveFrameCountAtClose}");
            log.Write($"FRAMES_AT_RESTORE_OPEN: {liveFrameCountAtRestoreOpen}");
            log.Write($"REALTIME_STOPPED_BY_CLOSE_CHECK: {(stopLiveSucceeded && liveFrameCountAtRestoreOpen == liveFrameCountAtClose ? "PASS" : "NOT PROVEN")}");
            log.Write($"RESTORATION_WRITES_COMPLETED: {restoreWrites}/7");
            log.Write($"ORIGINAL_CONFIGURATION_RESTORED: {(restorationSucceeded ? "YES - restoration exchange acknowledged; no independent post-close readback" : "NO")}");
            log.Write($"FINAL_COM_STATE: {(service.IsConnected ? "OPEN" : "CLOSED")}");
            log.Write($"FAILURE: {(failure is null ? "NONE" : failure.GetType().Name + ": " + failure.Message)}");
            log.Write("END OF HARDWARE EVIDENCE");
        }

        Console.WriteLine($"Evidence log: {logPath}");
        return failure is null ? 0 : 1;
    }

    private static string DescribeRecorderIdentity(byte[] page01)
    {
        ReadOnlySpan<byte> identity = page01.AsSpan(14, 41);
        return $"Firmware='{ReadAscii(identity, 0, 8)}'; Serial='{ReadAscii(identity, 8, 8)}'; " +
               $"Vehicle='{ReadAscii(identity, 16, 10)}'; Unit='{ReadAscii(identity, 26, 7)}'; Type='{ReadAscii(identity, 33, 8)}'; " +
               $"Raw={Hex(identity)}";
    }

    private static string ReadAscii(ReadOnlySpan<byte> bytes, int offset, int length) =>
        Encoding.ASCII.GetString(bytes.Slice(offset, length)).TrimEnd('\0', ' ');

    private static string Hex(ReadOnlySpan<byte> bytes) =>
        string.Join(" ", bytes.ToArray().Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));

    private static IReadOnlyList<byte[]> ExtractProtocolFrames(IEnumerable<RawRecord> records)
    {
        var buffer = new List<byte>();
        var frames = new List<byte[]>();
        foreach (RawRecord record in records.Where(record => record.Direction == OtmrDirection.Rx))
        {
            buffer.AddRange(record.Data);
            while (true)
            {
                int start = buffer.IndexOf(0x01);
                if (start < 0)
                {
                    buffer.Clear();
                    break;
                }
                if (start > 0)
                    buffer.RemoveRange(0, start);
                if (buffer.Count < 9)
                    break;
                int length = buffer[5] == 0x01 && buffer[6] == 0x01 && buffer[8] == 0x02 ? 12 + buffer[7] : 13;
                if (length is < 13 or > 4096)
                {
                    buffer.RemoveAt(0);
                    continue;
                }
                if (buffer.Count < length)
                    break;
                if (buffer[length - 3] != 0x03 || buffer[length - 1] != 0x04)
                {
                    buffer.RemoveAt(0);
                    continue;
                }
                frames.Add(buffer.GetRange(0, length).ToArray());
                buffer.RemoveRange(0, length);
            }
        }
        return frames;
    }

    private static IReadOnlyList<byte> ExtractInterFrameD2Values(
        IEnumerable<RawRecord> records,
        DateTimeOffset? finalTx,
        DateTimeOffset? liveClose)
    {
        if (!finalTx.HasValue)
            return Array.Empty<byte>();
        var values = new List<byte>();
        bool insideFrame = false;
        bool headerPrefix = false;
        bool d2Prefix = false;
        foreach (byte value in records
                     .Where(record => record.Direction == OtmrDirection.Rx && record.Timestamp >= finalTx.Value &&
                                      (!liveClose.HasValue || record.Timestamp <= liveClose.Value))
                     .SelectMany(record => record.Data))
        {
            if (insideFrame)
            {
                if (value == 0xFF)
                    insideFrame = false;
                continue;
            }
            if (d2Prefix)
            {
                values.Add(value);
                d2Prefix = false;
            }
            if (value == 0xD2)
                d2Prefix = true;
            if (value == 0xFB)
            {
                if (headerPrefix)
                {
                    insideFrame = true;
                    headerPrefix = false;
                }
                else
                {
                    headerPrefix = true;
                }
            }
            else
            {
                headerPrefix = false;
            }
        }
        return values;
    }

    private sealed record RawRecord(DateTimeOffset Timestamp, OtmrDirection Direction, byte[] Data, OtmrLiveState State);

    private sealed class EvidenceLog : IDisposable
    {
        private readonly object _sync = new();
        private readonly StreamWriter _writer;

        public EvidenceLog(string path)
        {
            _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
        }

        public void Write(string message, DateTimeOffset? timestamp = null)
        {
            string line = $"{timestamp ?? DateTimeOffset.Now:O}  {message}";
            lock (_sync)
                _writer.WriteLine(line);
            Console.WriteLine(line);
        }

        public void Dispose() => _writer.Dispose();
    }
}
