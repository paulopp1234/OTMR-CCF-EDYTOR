using System.Threading.Channels;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.ReadOnlyInterrogation;

internal static class Program
{
    private static readonly byte[] PageTransactions = { 0x02, 0x04, 0x06, 0x08, 0x0A, 0x0C };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: CcfEditor.ReadOnlyInterrogation <prior-hardware-log> <new-read-only-log>");
            return 64;
        }

        string priorLogPath = Path.GetFullPath(args[0]);
        string outputPath = Path.GetFullPath(args[1]);
        IReadOnlyDictionary<byte, byte[]> originalPages = LoadOriginalPages(priorLogPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var log = new EvidenceLog(outputPath);
        log.Write("CLASS 171 READ-ONLY RECORDER STATE CHECK");
        log.Write("TX SAFETY ALLOWLIST: OtmrLiveStartProtocol.SafeInterrogationWrites only; no 01 0C configuration acknowledgement, generated write, final 01 07, or restoration write.");
        log.Write($"FROZEN_SOURCE: {priorLogPath}");
        foreach ((byte transaction, byte[] frame) in originalPages.OrderBy(pair => pair.Key))
            log.Write($"FROZEN 01 {transaction:X2}: {Hex(frame)}");

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
        transport.BytesReceived += (_, e) =>
        {
            log.Write("RAW RX: " + Hex(e.Data));
            foreach (byte[] frame in assembler.Append(e.Data))
                frames.Writer.TryWrite(frame);
        };
        transport.BytesTransmitted += (_, e) =>
        {
            Interlocked.Increment(ref txCount);
            log.Write("RAW TX: " + Hex(e.Data));
        };
        transport.ErrorOccurred += (_, e) =>
        {
            log.Write("TRANSPORT ERROR: " + e.Exception);
            frames.Writer.TryComplete(e.Exception);
        };

        var currentPages = new Dictionary<byte, byte[]>();
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
            log.Write($"CONNECT_NO_TX_CHECK: {(txCount == 0 ? "PASS" : "FAIL")}");
            if (txCount != 0)
                throw new InvalidOperationException("Connect emitted TX; read-only interrogation refused.");

            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query01);
            await WaitExpectedAsync(frames.Reader, 0x01);
            currentPages[0x02] = await WaitExpectedAsync(frames.Reader, 0x02);

            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Acknowledge02);
            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query03);
            await WaitExpectedAsync(frames.Reader, 0x03);
            currentPages[0x04] = await WaitExpectedAsync(frames.Reader, 0x04);

            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Acknowledge04);
            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query05);
            await WaitExpectedAsync(frames.Reader, 0x05);
            currentPages[0x06] = await WaitExpectedAsync(frames.Reader, 0x06);

            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Acknowledge06);
            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query07);
            await WaitExpectedAsync(frames.Reader, 0x07);
            currentPages[0x08] = await WaitExpectedAsync(frames.Reader, 0x08);

            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Acknowledge08);
            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query09);
            await WaitExpectedAsync(frames.Reader, 0x09);
            currentPages[0x0A] = await WaitExpectedAsync(frames.Reader, 0x0A);

            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Acknowledge0A);
            await SendAllowedAsync(transport, OtmrLiveStartProtocol.Query0B);
            await WaitExpectedAsync(frames.Reader, 0x0B);
            currentPages[0x0C] = await WaitExpectedAsync(frames.Reader, 0x0C);

            log.Write("READ_ONLY_INTERROGATION_COMPLETE: all six pages received; no 01 0C acknowledgement sent");
        }
        catch (Exception ex)
        {
            failure = ex;
            log.Write("READ_ONLY_INTERROGATION_FAILURE: " + ex);
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

        var differences = new List<string>();
        foreach (byte transaction in PageTransactions)
        {
            if (!currentPages.TryGetValue(transaction, out byte[]? current))
            {
                differences.Add($"01 {transaction:X2}: current page missing");
                continue;
            }
            byte[] original = originalPages[transaction];
            int max = Math.Max(original.Length, current.Length);
            for (int offset = 0; offset < max; offset++)
            {
                string oldValue = offset < original.Length ? $"{original[offset]:X2}" : "--";
                string newValue = offset < current.Length ? $"{current[offset]:X2}" : "--";
                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                    differences.Add($"01 {transaction:X2} frame offset 0x{offset:X3}: frozen {oldValue}, current {newValue}");
            }
        }

        log.Write("FINAL READ-ONLY STATE SUMMARY");
        log.Write($"INTERROGATION_SUCCEEDED: {(failure is null && currentPages.Count == 6 ? "YES" : "NO")}");
        log.Write($"SIX_PAGES_IDENTICAL: {(differences.Count == 0 ? "YES" : "NO")}");
        log.Write($"CHANGED_OFFSET_COUNT: {differences.Count}");
        foreach (string difference in differences)
            log.Write("CHANGED: " + difference);
        if (currentPages.TryGetValue(0x02, out byte[]? page01))
        {
            log.Write($"CURRENT_SOURCE_FOR_WRITE_0x08B__RX_FRAME_0x08F: 0x{page01[0x08F]:X2}");
            log.Write($"CURRENT_SOURCE_FOR_WRITE_0x0BD__RX_FRAME_0x0C1: 0x{page01[0x0C1]:X2}");
        }
        log.Write($"ORIGINAL_PRE_START_STATE_PROVEN: {(failure is null && differences.Count == 0 ? "YES" : "NO")}");
        log.Write($"TOTAL_TX_COUNT: {txCount} (expected read-only allowlist sequence count 11)");
        log.Write("FINAL_COM_STATE: CLOSED");
        log.Write($"FAILURE: {(failure is null ? "NONE" : failure.GetType().Name + ": " + failure.Message)}");
        Console.WriteLine($"Read-only evidence: {outputPath}");
        return failure is null && differences.Count == 0 ? 0 : 1;
    }

    private static async Task SendAllowedAsync(SerialOtmrTransport transport, ReadOnlyMemory<byte> bytes)
    {
        if (!OtmrLiveStartProtocol.SafeInterrogationWrites.Any(allowed => allowed.Span.SequenceEqual(bytes.Span)))
            throw new InvalidOperationException("TX rejected because it is outside SafeInterrogationWrites.");
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
            throw new TimeoutException($"Timed out waiting for complete 01 {transaction:X2}.", ex);
        }
        if (!IsExpected(transaction, frame))
            throw new InvalidDataException($"Expected exact 01 {transaction:X2}; received {Hex(frame)}.");
        return frame;
    }

    private static bool IsExpected(byte transaction, ReadOnlySpan<byte> frame)
    {
        ReadOnlySpan<byte> exact = transaction switch
        {
            0x01 => OtmrLiveStartProtocol.Reply01.Span,
            0x03 => OtmrLiveStartProtocol.Reply03.Span,
            0x05 => OtmrLiveStartProtocol.Reply05.Span,
            0x07 => OtmrLiveStartProtocol.Reply07.Span,
            0x09 => OtmrLiveStartProtocol.Reply09.Span,
            0x0B => OtmrLiveStartProtocol.Reply0B.Span,
            _ => default
        };
        if (!exact.IsEmpty)
            return frame.SequenceEqual(exact);

        int expectedLength = transaction == 0x0C ? 0xD6 : 0x10B;
        byte expectedPage = transaction switch
        {
            0x02 => 0x01, 0x04 => 0x02, 0x06 => 0x03,
            0x08 => 0x04, 0x0A => 0x05, 0x0C => 0x06,
            _ => 0
        };
        byte expectedPayloadLength = transaction == 0x0C ? (byte)0xCA : (byte)0xFF;
        return expectedPage != 0 && frame.Length == expectedLength && frame[0] == 0x01 &&
               frame[1] == transaction && frame[2] == 0x00 && frame[3] == expectedPage &&
               frame[4] == (transaction == 0x0C ? (byte)0x01 : (byte)0x00) &&
               frame[5] == 0x01 && frame[6] == 0x01 && frame[7] == expectedPayloadLength &&
               frame[8] == 0x02 && frame[^3] == 0x03 && frame[^1] == 0x04 && HasValidCheck(frame);
    }

    private static bool HasValidCheck(ReadOnlySpan<byte> frame)
    {
        int sum = 0;
        foreach (byte value in frame.Slice(9, frame.Length - 12))
            sum = (sum + value) & 0xFF;
        return frame[^2] == (byte)sum;
    }

    private static IReadOnlyDictionary<byte, byte[]> LoadOriginalPages(string path)
    {
        var pages = new Dictionary<byte, byte[]>();
        foreach (string line in File.ReadLines(path))
        {
            const string marker = "RAW RX: ";
            int markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                continue;
            int end = line.IndexOf(" |", markerIndex, StringComparison.Ordinal);
            string hex = end < 0
                ? line[(markerIndex + marker.Length)..]
                : line.Substring(markerIndex + marker.Length, end - markerIndex - marker.Length);
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
        if (pages.Count != PageTransactions.Length)
            throw new InvalidDataException($"Frozen hardware log yielded {pages.Count}/6 required pre-START pages.");
        return pages;
    }

    private static string Hex(ReadOnlySpan<byte> bytes) =>
        string.Join(" ", bytes.ToArray().Select(value => value.ToString("X2")));

    private sealed class ProtocolAssembler
    {
        private readonly List<byte> _buffer = new();

        public IReadOnlyList<byte[]> Append(ReadOnlySpan<byte> data)
        {
            foreach (byte value in data)
                _buffer.Add(value);
            var frames = new List<byte[]>();
            while (true)
            {
                int start = _buffer.IndexOf(0x01);
                if (start < 0)
                {
                    _buffer.Clear();
                    break;
                }
                if (start > 0)
                    _buffer.RemoveRange(0, start);
                if (_buffer.Count < 9)
                    break;
                int length = _buffer[5] == 0x01 && _buffer[6] == 0x01 && _buffer[8] == 0x02
                    ? 12 + _buffer[7]
                    : 13;
                if (length is < 13 or > 4096)
                {
                    _buffer.RemoveAt(0);
                    continue;
                }
                if (_buffer.Count < length)
                    break;
                if (_buffer[length - 3] != 0x03 || _buffer[length - 1] != 0x04)
                {
                    _buffer.RemoveAt(0);
                    continue;
                }
                frames.Add(_buffer.GetRange(0, length).ToArray());
                _buffer.RemoveRange(0, length);
            }
            return frames;
        }
    }

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

        public void Write(string message)
        {
            string line = $"{DateTimeOffset.Now:O}  {message}";
            lock (_sync)
                _writer.WriteLine(line);
            Console.WriteLine(line);
        }

        public void Dispose() => _writer.Dispose();
    }
}
