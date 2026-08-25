namespace CcfEditor.Otmr.Live;

public enum OtmrLiveState
{
    Disconnected,
    ConnectedIdle,
    PreflightingConfiguration,
    WaitingFor01_01,
    WaitingFor01_02,
    WaitingFor01_03,
    WaitingFor01_04,
    WaitingFor01_05,
    WaitingFor01_06,
    WaitingFor01_07,
    WaitingFor01_08,
    WaitingFor01_09,
    WaitingFor01_0A,
    WaitingFor01_0B,
    WaitingFor01_0C,
    RecorderConfigurationWriteBlocked,
    WaitingFor01_0D,
    WaitingFor01_0E,
    WaitingFor01_0F,
    WaitingFor01_10,
    WaitingFor01_11,
    WaitingFor01_12,
    WaitingFor01_13,
    StartingLive,
    WaitingForLiveFrames,
    LiveActive,
    NotLive,
    RestoringOriginalConfiguration,
    Error
}

public static class OtmrLiveStartProtocol
{
    // Every TX frame below is copied byte-for-byte from an Analyser.exe Write
    // Request in HHD_Serial_Trace_20260824_143201.txt. No command is generated
    // by extrapolating the transaction number.
    public static ReadOnlyMemory<byte> Query01 { get; } = new byte[]
        { 0x01, 0x01, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x01, 0x03, 0x01, 0x04 };

    public static ReadOnlyMemory<byte> Acknowledge02 { get; } = new byte[]
        { 0x01, 0x02, 0x00, 0x01, 0x01, 0x00, 0x02, 0x01, 0x02, 0x02, 0x03, 0x02, 0x04 };
    public static ReadOnlyMemory<byte> Query03 { get; } = new byte[]
        { 0x01, 0x03, 0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x02, 0x03, 0x00, 0x04 };

    public static ReadOnlyMemory<byte> Acknowledge04 { get; } = new byte[]
        { 0x01, 0x04, 0x00, 0x01, 0x01, 0x00, 0x02, 0x01, 0x02, 0x04, 0x03, 0x04, 0x04 };
    public static ReadOnlyMemory<byte> Query05 { get; } = new byte[]
        { 0x01, 0x05, 0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x02, 0x03, 0x00, 0x04 };

    public static ReadOnlyMemory<byte> Acknowledge06 { get; } = new byte[]
        { 0x01, 0x06, 0x00, 0x01, 0x01, 0x00, 0x02, 0x01, 0x02, 0x06, 0x03, 0x06, 0x04 };
    public static ReadOnlyMemory<byte> Query07 { get; } = new byte[]
        { 0x01, 0x07, 0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x02, 0x03, 0x00, 0x04 };

    public static ReadOnlyMemory<byte> Acknowledge08 { get; } = new byte[]
        { 0x01, 0x08, 0x00, 0x01, 0x01, 0x00, 0x02, 0x01, 0x02, 0x08, 0x03, 0x08, 0x04 };
    public static ReadOnlyMemory<byte> Query09 { get; } = new byte[]
        { 0x01, 0x09, 0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x02, 0x03, 0x00, 0x04 };

    public static ReadOnlyMemory<byte> Acknowledge0A { get; } = new byte[]
        { 0x01, 0x0A, 0x00, 0x01, 0x01, 0x00, 0x02, 0x01, 0x02, 0x0A, 0x03, 0x0A, 0x04 };
    public static ReadOnlyMemory<byte> Query0B { get; } = new byte[]
        { 0x01, 0x0B, 0x00, 0x01, 0x01, 0x01, 0x00, 0x00, 0x02, 0x03, 0x00, 0x04 };

    public static ReadOnlyMemory<byte> FinalLiveStart07 { get; } = new byte[]
        { 0x01, 0x07, 0x00, 0x01, 0x01, 0x01, 0x02, 0x01, 0x02, 0x13, 0x03, 0x13, 0x04 };

    public static ReadOnlyMemory<byte> Reply01 { get; } = new byte[]
        { 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x01, 0x03, 0x01, 0x04 };
    public static ReadOnlyMemory<byte> Reply03 { get; } = new byte[]
        { 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x03, 0x03, 0x03, 0x04 };
    public static ReadOnlyMemory<byte> Reply05 { get; } = new byte[]
        { 0x01, 0x05, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x05, 0x03, 0x05, 0x04 };
    public static ReadOnlyMemory<byte> Reply07 { get; } = new byte[]
        { 0x01, 0x07, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x07, 0x03, 0x07, 0x04 };
    public static ReadOnlyMemory<byte> Reply09 { get; } = new byte[]
        { 0x01, 0x09, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x09, 0x03, 0x09, 0x04 };
    public static ReadOnlyMemory<byte> Reply0B { get; } = new byte[]
        { 0x01, 0x0B, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x0B, 0x03, 0x0B, 0x04 };
    public static ReadOnlyMemory<byte> Reply0D { get; } = new byte[]
        { 0x01, 0x0D, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x01, 0x03, 0x01, 0x04 };
    public static ReadOnlyMemory<byte> Reply0E { get; } = new byte[]
        { 0x01, 0x0E, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x02, 0x03, 0x02, 0x04 };
    public static ReadOnlyMemory<byte> Reply0F { get; } = new byte[]
        { 0x01, 0x0F, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x03, 0x03, 0x03, 0x04 };
    public static ReadOnlyMemory<byte> Reply10 { get; } = new byte[]
        { 0x01, 0x10, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x04, 0x03, 0x04, 0x04 };
    public static ReadOnlyMemory<byte> Reply11 { get; } = new byte[]
        { 0x01, 0x11, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x05, 0x03, 0x05, 0x04 };
    public static ReadOnlyMemory<byte> Reply12 { get; } = new byte[]
        { 0x01, 0x12, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x06, 0x03, 0x06, 0x04 };
    public static ReadOnlyMemory<byte> Reply13 { get; } = new byte[]
        { 0x01, 0x13, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x06, 0x03, 0x06, 0x04 };

    public static IReadOnlyList<ReadOnlyMemory<byte>> SafeInterrogationWrites { get; } = new[]
    {
        Query01,
        Acknowledge02, Query03,
        Acknowledge04, Query05,
        Acknowledge06, Query07,
        Acknowledge08, Query09,
        Acknowledge0A, Query0B
    };

    internal static bool IsExpectedReply(byte transaction, ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2 || frame[0] != 0x01 || frame[1] != transaction)
            return false;

        ReadOnlySpan<byte> exact = transaction switch
        {
            0x01 => Reply01.Span,
            0x03 => Reply03.Span,
            0x05 => Reply05.Span,
            0x07 => Reply07.Span,
            0x09 => Reply09.Span,
            0x0B => Reply0B.Span,
            0x0D => Reply0D.Span,
            0x0E => Reply0E.Span,
            0x0F => Reply0F.Span,
            0x10 => Reply10.Span,
            0x11 => Reply11.Span,
            0x12 => Reply12.Span,
            0x13 => Reply13.Span,
            _ => default
        };
        if (!exact.IsEmpty)
            return frame.SequenceEqual(exact);

        int expectedLength = transaction switch
        {
            0x02 or 0x04 or 0x06 or 0x08 or 0x0A => 0x10B,
            0x0C => 0xD6,
            _ => 0
        };
        byte page = transaction switch
        {
            0x02 => 0x01,
            0x04 => 0x02,
            0x06 => 0x03,
            0x08 => 0x04,
            0x0A => 0x05,
            0x0C => 0x06,
            _ => 0
        };
        byte payloadLength = transaction == 0x0C ? (byte)0xCA : (byte)0xFF;

        return expectedLength != 0 &&
               frame.Length == expectedLength &&
               frame[2] == 0x00 && frame[3] == page &&
               frame[4] == (transaction == 0x0C ? (byte)0x01 : (byte)0x00) &&
               frame[5] == 0x01 && frame[6] == 0x01 &&
               frame[7] == payloadLength && frame[8] == 0x02 &&
               frame[^3] == 0x03 && frame[^1] == 0x04;
    }

    internal static string? InterpretationForTx(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < SafeInterrogationWrites.Count; i++)
        {
            if (bytes.SequenceEqual(SafeInterrogationWrites[i].Span))
                return i == 0
                    ? "Captured OTMR 01 01 interrogation start"
                    : "Captured OTMR read/interrogation handshake";
        }

        return bytes.SequenceEqual(FinalLiveStart07.Span)
            ? "Captured Arrowvale final live-start 01 07"
            : null;
    }
}

public sealed record OtmrLiveStartTiming(
    TimeSpan StageReplyTimeout,
    TimeSpan FinalReplyToFinalCommandDelay,
    TimeSpan FinalCommandToCloseDelay)
{
    public static OtmrLiveStartTiming HardwareDefault { get; } = new(
        TimeSpan.FromSeconds(3),
        TimeSpan.FromMilliseconds(80.5),
        TimeSpan.FromMilliseconds(17.6));
}

public sealed record OtmrLiveRestoreTiming(
    TimeSpan LiveCloseToRestoreOpenDelay,
    TimeSpan FinalCommandToCloseDelay)
{
    public static OtmrLiveRestoreTiming HardwareDefault { get; } = new(
        TimeSpan.FromMilliseconds(200.3279),
        TimeSpan.FromMilliseconds(4.5618));
}

public sealed class OtmrConfigurationPreflightException : InvalidOperationException
{
    public OtmrConfigurationPreflightException(string message, Exception? innerException = null)
        : base("OTMR configuration preflight failed: " + message, innerException)
    {
    }
}

public sealed class OtmrLiveStateChangedEventArgs : EventArgs
{
    public OtmrLiveStateChangedEventArgs(OtmrLiveState state) => State = state;
    public OtmrLiveState State { get; }
}

internal sealed class OtmrProtocolFrameAssembler
{
    private const int MaximumFrameLength = 4096;
    private readonly List<byte> _buffer = new();

    public IReadOnlyList<byte[]> Append(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
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
            if (length < 13 || length > MaximumFrameLength)
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

            byte[] frame = _buffer.GetRange(0, length).ToArray();
            _buffer.RemoveRange(0, length);
            frames.Add(frame);
        }
        return frames;
    }

    public void Reset() => _buffer.Clear();
}
