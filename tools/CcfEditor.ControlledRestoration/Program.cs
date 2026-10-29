using System.Diagnostics;
using System.Threading.Channels;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.ControlledRestoration;

internal static class Program
{
    private static readonly byte[] PageTransactions = { 0x02, 0x04, 0x06, 0x08, 0x0A, 0x0C };
    private static readonly int[] ExpectedLengths = { 13, 0x10B, 0x10B, 0x10B, 0x10B, 0x10B, 0xD2 };
    private static readonly byte[] ExpectedCommands = { 0x0C, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
    private static readonly byte[] ExpectedPayloadLengths = { 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xC6 };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || args[2] is not ("preflight" or "execute"))
        {
            Console.Error.WriteLine("Usage: CcfEditor.ControlledRestoration <frozen-hardware-log> <evidence-log> <preflight|execute>");
            return 64;
        }

        string frozenLogPath = Path.GetFullPath(args[0]);
        string evidencePath = Path.GetFullPath(args[1]);
        bool execute = args[2] == "execute";
        IReadOnlyDictionary<byte, byte[]> frozenPages = LoadFrozenPreStartPages(frozenLogPath);
        OtmrGeneratedConfigurationExchange restoration =
            OtmrConfigurationExchangeGenerator.CreateStopRestoration(frozenPages);
        ValidateExchange(restoration);

        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        using var log = new EvidenceLog(evidencePath);
        log.Write("CLASS 171 CONTROLLED ORIGINAL-STATE RESTORATION");
        log.Write($"MODE: {(execute ? "EXECUTE ONE RESTORATION" : "PREFLIGHT ONLY; COM WILL NOT BE OPENED")}");
        log.Write("PROHIBITED TX: START writes derived from current recorder and invented messages. The exact captured post-01-13 01 07 is required for this STOP/restoration tail.");
        log.Write($"FROZEN_SOURCE: {frozenLogPath}");
        log.Write($"FROZEN_PAGE_01_02_FRAME_0x08F: {frozenPages[0x02][0x08F]:X2}");
        log.Write($"FROZEN_PAGE_01_02_FRAME_0x0C1: {frozenPages[0x02][0x0C1]:X2}");
        log.Write("RESTORATION_TARGET: original recorder state 0x08F=04, 0x0C1=00");
        for (int index = 0; index < restoration.Writes.Count; index++)
        {
            OtmrGeneratedConfigurationWrite write = restoration.Writes[index];
            log.Write($"GENERATED RESTORE WRITE {index + 1}/7: command/page {write.Bytes[1]:X2}; " +
                $"byte count {write.Bytes.Length}; check {write.Bytes[^2]:X2}; provenance {write.Provenance.Count}/{write.Bytes.Length}; TX {Hex(write.Bytes)}");
        }
        log.Write($"CAPTURED_STOP_TAIL: exact 01 13 -> TX {Hex(OtmrLiveStartProtocol.FinalLiveStart07.Span)} -> " +
            $"delay {OtmrLiveRestoreTiming.HardwareDefault.FinalCommandToCloseDelay.TotalMilliseconds:F4} ms -> close -> no reopen.");
        log.Write("PREFLIGHT: PASS; 7/7 writes, exact ordering, lengths, framing, checksums, complete known provenance, and captured final STOP tail validated in memory.");

        if (!execute)
        {
            log.Write("PREFLIGHT_ONLY_COMPLETE: COM2 was not opened and no TX occurred.");
            return 0;
        }

        string[] ports = SerialOtmrTransport.GetAvailablePorts();
        log.Write($"AVAILABLE_PORTS: {string.Join(", ", ports)}");
        if (!ports.Contains("COM2", StringComparer.OrdinalIgnoreCase))
        {
            log.Write("STOP: COM2 is not enumerated; no TX occurred.");
            return 2;
        }

        using var transport = new SerialOtmrTransport();
        var assembler = new ProtocolAssembler();
        var frames = Channel.CreateUnbounded<byte[]>();
        int txCount = 0;
        long finalTxStarted = 0;
        long finalWriteReturned = 0;
        long finalCloseInvoked = 0;
        long finalCloseReturned = 0;
        int captureFinalWriteTiming = 0;
        byte[]? capturedFinalTx = null;
        transport.BytesReceived += (_, e) =>
        {
            log.Write("RAW RX: " + Hex(e.Data));
            foreach (byte[] frame in assembler.Append(e.Data))
                frames.Writer.TryWrite(frame);
        };
        transport.BytesTransmitted += (_, e) =>
        {
            Interlocked.Increment(ref txCount);
            if (Volatile.Read(ref captureFinalWriteTiming) != 0)
                capturedFinalTx = e.Data.ToArray();
            else
                log.Write("RAW TX: " + Hex(e.Data));
        };
        transport.ErrorOccurred += (_, e) =>
        {
            log.Write("TRANSPORT ERROR: " + e.Exception);
            frames.Writer.TryComplete(e.Exception);
        };
        transport.DiagnosticOccurred += (_, e) =>
        {
            if (Volatile.Read(ref captureFinalWriteTiming) != 0 && e.Stage == "SERIAL_WRITE_INVOKED")
                Interlocked.Exchange(ref finalTxStarted, e.StopwatchTimestamp);
            else if (Volatile.Read(ref captureFinalWriteTiming) != 0 && e.Stage == "SERIAL_WRITE_RETURNED")
                Interlocked.Exchange(ref finalWriteReturned, e.StopwatchTimestamp);
            else if (e.Stage == "SERIALPORT_CLOSE_INVOKED")
                Interlocked.Exchange(ref finalCloseInvoked, e.StopwatchTimestamp);
            else if (e.Stage == "SERIALPORT_CLOSE_RETURNED")
                Interlocked.Exchange(ref finalCloseReturned, e.StopwatchTimestamp);
            if (Volatile.Read(ref captureFinalWriteTiming) == 0)
                log.Write($"TRANSPORT {e.Stage}: {e.Detail}; stopwatch={e.StopwatchTimestamp}/{e.StopwatchFrequency}");
        };

        int writesCompleted = 0;
        Exception? failure = null;
        try
        {
            log.Write("COM_OPEN_BEGIN: COM2 38400/8/N/1 Handshake.None RTS LOW DTR LOW");
            try
            {
                await transport.ConnectAsync(OtmrSerialSettings.Class171Bench("COM2"));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                log.Write("STOP_COM2_OPEN_FAILED_OR_BUSY: " + ex);
                Console.WriteLine("COM2 is busy or could not be opened. No process was terminated and no TX occurred.");
                return 3;
            }
            log.Write("COM_OPEN_RETURNED");
            if (txCount != 0)
                throw new InvalidOperationException("Connect emitted TX; restoration refused before interrogation.");

            await RunInterrogationAsync(transport, frames.Reader, log);
            log.Write("CLEANUP_INTERROGATION_COMPLETE: fresh replies received; frozen pages remain the sole restoration source.");

            for (int index = 0; index < restoration.Writes.Count; index++)
            {
                OtmrGeneratedConfigurationWrite write = restoration.Writes[index];
                byte? expectedReply = index == 0 ? null : (byte)(0x0C + index);
                long started = Stopwatch.GetTimestamp();
                log.Write($"RESTORE WRITE {index + 1}/7 BEGIN: command/page {write.Bytes[1]:X2}; byte count {write.Bytes.Length}; " +
                    $"check {write.Bytes[^2]:X2}; expected reply {(expectedReply.HasValue ? $"01 {expectedReply:X2}" : "none between captured paired writes 1 and 2")}; TX {Hex(write.Bytes)}");
                await transport.SendAsync(write.Bytes);
                if (expectedReply.HasValue)
                {
                    byte[] actual = await WaitExpectedAsync(frames.Reader, expectedReply.Value);
                    writesCompleted++;
                    log.Write($"RESTORE WRITE {index + 1}/7 COMPLETE: actual reply {Hex(actual)}; " +
                        $"elapsed {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F4} ms");
                }
                else
                {
                    writesCompleted++;
                    log.Write($"RESTORE WRITE 1/7 COMPLETE: actual reply NONE as captured; paired write follows; " +
                        $"elapsed {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F4} ms");
                }
            }

            byte[] finalReply = await WaitExpectedAsync(frames.Reader, 0x13);
            log.Write("EXACT_01_13_RECEIVED: " + Hex(finalReply));
            Volatile.Write(ref captureFinalWriteTiming, 1);
            await transport.SendAsync(OtmrLiveStartProtocol.FinalLiveStart07);
            Volatile.Write(ref captureFinalWriteTiming, 0);
            if (finalTxStarted == 0 || finalWriteReturned == 0)
                throw new InvalidOperationException("Transport did not report exact final write invocation/return timestamps.");
            if (capturedFinalTx is null || !capturedFinalTx.AsSpan().SequenceEqual(OtmrLiveStartProtocol.FinalLiveStart07.Span))
                throw new InvalidOperationException("Transport did not report the exact captured final 01 07 TX bytes.");
            TimeSpan closeDelay = OtmrLiveRestoreTiming.HardwareDefault.FinalCommandToCloseDelay;
            WaitUntilElapsed(finalTxStarted, closeDelay);
            await transport.DisconnectAsync();
            if (finalCloseInvoked == 0 || finalCloseReturned == 0)
                throw new InvalidOperationException("Transport did not report exact close invocation/return timestamps.");
            log.Write("CAPTURED_FINAL_01_07_TX: " + Hex(OtmrLiveStartProtocol.FinalLiveStart07.Span));
            log.Write($"FINAL_TIMING: TX start={finalTxStarted}; write return={finalWriteReturned}; " +
                $"close invocation={finalCloseInvoked}; close return={finalCloseReturned}; frequency={Stopwatch.Frequency}; " +
                $"TX-start-to-write-return={Stopwatch.GetElapsedTime(finalTxStarted, finalWriteReturned).TotalMilliseconds:F4} ms; " +
                $"TX-start-to-close-invocation={Stopwatch.GetElapsedTime(finalTxStarted, finalCloseInvoked).TotalMilliseconds:F4} ms; " +
                $"close duration={Stopwatch.GetElapsedTime(finalCloseInvoked, finalCloseReturned).TotalMilliseconds:F4} ms");
            log.Write("CAPTURED_FINAL_CLOSE_COMPLETE: COM closed; no reopen");
        }
        catch (Exception ex)
        {
            failure = ex;
            log.Write("RESTORATION_FAILURE_STOP_IMMEDIATELY: " + ex);
        }
        finally
        {
            if (transport.IsConnected)
            {
                log.Write("COM_CLOSE_BEGIN");
                await transport.DisconnectAsync();
                log.Write("COM_CLOSE_RETURNED");
            }
        }

        log.Write($"RESTORE_WRITES_COMPLETED: {writesCompleted}/7");
        log.Write("FINAL_COM_STATE: CLOSED");
        log.Write($"FAILURE: {(failure is null ? "NONE" : failure.GetType().Name + ": " + failure.Message)}");
        return failure is null && writesCompleted == 7 ? 0 : 1;
    }

    private static async Task RunInterrogationAsync(
        SerialOtmrTransport transport,
        ChannelReader<byte[]> frames,
        EvidenceLog log)
    {
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query01);
        await WaitExpectedAsync(frames, 0x01);
        await WaitExpectedAsync(frames, 0x02);
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Acknowledge02);
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query03);
        await WaitExpectedAsync(frames, 0x03);
        await WaitExpectedAsync(frames, 0x04);
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Acknowledge04);
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query05);
        await WaitExpectedAsync(frames, 0x05);
        await WaitExpectedAsync(frames, 0x06);
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Acknowledge06);
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query07);
        await WaitExpectedAsync(frames, 0x07);
        await WaitExpectedAsync(frames, 0x08);
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Acknowledge08);
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query09);
        await WaitExpectedAsync(frames, 0x09);
        await WaitExpectedAsync(frames, 0x0A);
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Acknowledge0A);
        await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query0B);
        await WaitExpectedAsync(frames, 0x0B);
        await WaitExpectedAsync(frames, 0x0C);
        log.Write("INTERROGATION_TX_COUNT: 11; no generated write was sent before complete 01 0C.");
    }

    private static async Task SendAllowedAsync(SerialOtmrTransport transport, ReadOnlyMemory<byte> bytes)
    {
        if (!OtmrLiveStartProtocol.SafeInterrogationWrites.Any(allowed => allowed.Span.SequenceEqual(bytes.Span)))
            throw new InvalidOperationException("Interrogation TX rejected because it is outside SafeInterrogationWrites.");
        await transport.SendAsync(bytes);
    }

    private static async Task<byte[]> WaitExpectedAsync(ChannelReader<byte[]> frames, byte transaction)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        byte[] frame;
        try
        {
            frame = await frames.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException($"Timed out waiting for exact complete 01 {transaction:X2}.", ex);
        }
        if (!OtmrLiveStartProtocol.IsExpectedReply(transaction, frame))
            throw new InvalidDataException($"Expected exact 01 {transaction:X2}; received {Hex(frame)}.");
        return frame;
    }

    private static void ValidateExchange(OtmrGeneratedConfigurationExchange exchange)
    {
        if (exchange.Writes.Count != 7)
            throw new InvalidDataException($"Restoration generation produced {exchange.Writes.Count}/7 writes.");
        for (int index = 0; index < exchange.Writes.Count; index++)
        {
            OtmrGeneratedConfigurationWrite write = exchange.Writes[index];
            byte[] bytes = write.Bytes;
            if (bytes.Length != ExpectedLengths[index] || bytes[0] != 0x01 ||
                bytes[1] != ExpectedCommands[index] || bytes[7] != ExpectedPayloadLengths[index] ||
                bytes[8] != 0x02 || bytes[^3] != 0x03 || bytes[^1] != 0x04)
                throw new InvalidDataException($"Restoration write {index + 1}/7 failed length/framing/order validation.");
            if (!OtmrProtocolDerivation.HasValidPayloadCheckByte(bytes))
                throw new InvalidDataException($"Restoration write {index + 1}/7 failed check-byte validation.");
            if (write.Provenance.Count != bytes.Length || write.Provenance.Any(item =>
                    item.Value != bytes[item.FrameOffset] || string.IsNullOrWhiteSpace(item.SourceReference) ||
                    !Enum.IsDefined(item.Source)))
                throw new InvalidDataException($"Restoration write {index + 1}/7 has missing or unknown provenance.");
        }
    }

    private static IReadOnlyDictionary<byte, byte[]> LoadFrozenPreStartPages(string path)
    {
        var pages = new Dictionary<byte, byte[]>();
        foreach (string line in File.ReadLines(path))
        {
            if (line.Contains("START WRITE 1/7", StringComparison.Ordinal))
                break;
            const string marker = "RAW RX: ";
            int markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                continue;
            int end = line.IndexOf(" |", markerIndex, StringComparison.Ordinal);
            string hex = end < 0 ? line[(markerIndex + marker.Length)..] :
                line.Substring(markerIndex + marker.Length, end - markerIndex - marker.Length);
            byte[] bytes;
            try
            {
                bytes = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => Convert.ToByte(value, 16)).ToArray();
            }
            catch (FormatException)
            {
                continue;
            }
            if (bytes.Length >= 2 && PageTransactions.Contains(bytes[1]) && !pages.ContainsKey(bytes[1]))
                pages.Add(bytes[1], bytes);
        }
        if (pages.Count != 6)
            throw new InvalidDataException($"Frozen log yielded {pages.Count}/6 required pre-START pages before START WRITE 1/7.");
        if (pages[0x02].Length <= 0x0C1 || pages[0x02][0x08F] != 0x04 || pages[0x02][0x0C1] != 0x00)
            throw new InvalidDataException("Frozen page 01 02 does not contain the authorised original 04/00 baseline.");
        foreach (byte transaction in PageTransactions)
        {
            byte[] frame = pages[transaction];
            if (!OtmrLiveStartProtocol.IsExpectedReply(transaction, frame) ||
                !OtmrProtocolDerivation.HasValidPayloadCheckByte(frame))
                throw new InvalidDataException($"Frozen page 01 {transaction:X2} is incomplete or has invalid framing/check byte.");
        }
        return pages.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static string Hex(ReadOnlySpan<byte> bytes) =>
        string.Join(" ", bytes.ToArray().Select(value => value.ToString("X2")));

    private static void WaitUntilElapsed(long started, TimeSpan targetElapsed)
    {
        while (Stopwatch.GetElapsedTime(started) < targetElapsed)
            Thread.SpinWait(64);
    }

    private sealed class ProtocolAssembler
    {
        private readonly List<byte> _buffer = new();

        public IReadOnlyList<byte[]> Append(ReadOnlySpan<byte> data)
        {
            foreach (byte value in data)
                _buffer.Add(value);
            var result = new List<byte[]>();
            while (true)
            {
                int start = _buffer.IndexOf(0x01);
                if (start < 0) { _buffer.Clear(); break; }
                if (start > 0) _buffer.RemoveRange(0, start);
                if (_buffer.Count < 9) break;
                int length = _buffer[5] == 0x01 && _buffer[6] == 0x01 && _buffer[8] == 0x02
                    ? 12 + _buffer[7] : 13;
                if (length is < 13 or > 4096) { _buffer.RemoveAt(0); continue; }
                if (_buffer.Count < length) break;
                if (_buffer[length - 3] != 0x03 || _buffer[length - 1] != 0x04)
                { _buffer.RemoveAt(0); continue; }
                result.Add(_buffer.GetRange(0, length).ToArray());
                _buffer.RemoveRange(0, length);
            }
            return result;
        }
    }

    private sealed class EvidenceLog : IDisposable
    {
        private readonly object _sync = new();
        private readonly StreamWriter _writer;

        public EvidenceLog(string path)
        {
            _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                { AutoFlush = true };
        }

        public void Write(string message)
        {
            string line = $"{DateTimeOffset.Now:O}  {message}";
            lock (_sync) _writer.WriteLine(line);
            Console.WriteLine(line);
        }

        public void Dispose() => _writer.Dispose();
    }
}
