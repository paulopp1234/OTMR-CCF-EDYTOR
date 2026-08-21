namespace CcfEditor.Otmr.Live;

public enum OtmrLiveState
{
    Disconnected,
    ConnectedIdle,
    QuerySent,
    OtmrReplied,
    StartingLive,
    WaitingForLiveFrames,
    LiveActive,
    Error
}

public static class OtmrLiveStartProtocol
{
    public static ReadOnlyMemory<byte> ProvenQueryFrame { get; } = new byte[]
        { 0x01, 0x01, 0x00, 0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x01, 0x03, 0x01, 0x04 };

    public static ReadOnlyMemory<byte> ExpectedReplyPrefix { get; } = new byte[]
        { 0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x01, 0x03, 0x01, 0x04 };

    public static ReadOnlyMemory<byte> CandidateLiveStartFrame { get; } = new byte[]
        { 0x01, 0x07, 0x00, 0x01, 0x01, 0x01, 0x02, 0x01, 0x02, 0x13, 0x03, 0x13, 0x04 };
}

public sealed record OtmrLiveStartTiming(
    TimeSpan QueryReplyTimeout,
    TimeSpan AfterReplyDelay,
    TimeSpan AfterLiveCommandDelay,
    TimeSpan ReopenDelay)
{
    public static OtmrLiveStartTiming HardwareDefault { get; } = new(
        TimeSpan.FromSeconds(3),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(150));
}

public sealed class OtmrLiveStateChangedEventArgs : EventArgs
{
    public OtmrLiveStateChangedEventArgs(OtmrLiveState state) => State = state;
    public OtmrLiveState State { get; }
}

internal sealed class OtmrByteSequenceDetector
{
    private readonly byte[] _sequence;
    private int _matched;

    public OtmrByteSequenceDetector(ReadOnlySpan<byte> sequence)
    {
        if (sequence.IsEmpty)
            throw new ArgumentException("Detection sequence cannot be empty.", nameof(sequence));
        _sequence = sequence.ToArray();
    }

    public bool Append(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            if (value == _sequence[_matched])
            {
                _matched++;
                if (_matched == _sequence.Length)
                {
                    _matched = 0;
                    return true;
                }
            }
            else
            {
                _matched = value == _sequence[0] ? 1 : 0;
            }
        }

        return false;
    }

    public void Reset() => _matched = 0;
}
