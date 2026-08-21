using CcfEditor.Otmr.Live;

namespace CcfEditor.Otmr.Bench;

/// <summary>
/// One controlled, raw-only observation period for an explicitly armed pin.
/// The frames and their differences carry no inferred protocol semantics.
/// </summary>
public sealed class OtmrBenchObservationSession
{
    public const string RawOnlySemanticStatus =
        "RAW FRAME OBSERVATION ONLY - NOT CCF RECORDS, ON/OFF, CARD/CHANNEL, PASS, OR LIVE MATCH";

    private readonly List<OtmrBenchObservationFrame> _frames = new();

    public OtmrBenchObservationSession(
        string connector,
        string pin,
        string expectedFunction,
        int? expectedRecordA,
        int? expectedRecordB,
        DateTimeOffset armTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFunction);

        SessionId = Guid.NewGuid();
        Connector = connector;
        Pin = pin;
        ExpectedFunction = expectedFunction;
        ExpectedRecordA = expectedRecordA;
        ExpectedRecordB = expectedRecordB;
        ArmTimestamp = armTimestamp;
    }

    public Guid SessionId { get; }
    public string Connector { get; }
    public string Pin { get; }
    public string ExpectedFunction { get; }
    public int? ExpectedRecordA { get; }
    public int? ExpectedRecordB { get; }
    public DateTimeOffset ArmTimestamp { get; }
    public DateTimeOffset? StopTimestamp { get; private set; }
    public bool IsStopped => StopTimestamp.HasValue;
    public IReadOnlyList<OtmrBenchObservationFrame> Frames => _frames.AsReadOnly();
    public int FrameCount => _frames.Count;
    public DateTimeOffset? LastTimestamp => _frames.Count == 0 ? null : _frames[^1].Timestamp;
    public string LastRawHex => _frames.Count == 0 ? string.Empty : _frames[^1].RawFrameHex;
    public string LastCandidateRawDelta => _frames.Count == 0 ? string.Empty : _frames[^1].CandidateRawDelta;

    public OtmrBenchObservationFrame AddFrame(DateTimeOffset timestamp, OtmrLiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (IsStopped)
            throw new InvalidOperationException("Cannot add a frame after the observation session has stopped.");

        byte[] current = frame.GetDataSnapshot();
        byte[]? previous = _frames.Count == 0 ? null : _frames[^1].GetRawFrameBytesSnapshot();
        var observationFrame = new OtmrBenchObservationFrame(
            _frames.Count + 1,
            timestamp,
            current,
            BuildCandidateDelta(previous, current));
        _frames.Add(observationFrame);
        return observationFrame;
    }

    public void Stop(DateTimeOffset timestamp)
    {
        if (IsStopped)
            return;
        if (timestamp < ArmTimestamp)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Stop timestamp cannot precede the arm timestamp.");
        StopTimestamp = timestamp;
    }

    private static string BuildCandidateDelta(byte[]? previous, byte[] current)
    {
        const string prefix = "CANDIDATE RAW DELTA: ";
        if (previous is null)
            return prefix + "BASELINE (no previous complete frame)";

        var changes = new List<string>();
        int sharedLength = Math.Min(previous.Length, current.Length);
        for (int index = 0; index < sharedLength; index++)
        {
            if (previous[index] != current[index])
                changes.Add($"@{index:D2}:{previous[index]:X2}→{current[index]:X2}");
        }

        if (previous.Length != current.Length)
            changes.Add($"length {previous.Length}→{current.Length}");

        return prefix + (changes.Count == 0 ? "no byte changes" : string.Join(", ", changes));
    }
}

public sealed class OtmrBenchObservationFrame
{
    private readonly byte[] _rawFrameBytes;

    internal OtmrBenchObservationFrame(
        int sequenceNumber,
        DateTimeOffset timestamp,
        byte[] rawFrameBytes,
        string candidateRawDelta)
    {
        SequenceNumber = sequenceNumber;
        Timestamp = timestamp;
        _rawFrameBytes = rawFrameBytes.ToArray();
        RawFrameHex = string.Join(" ", _rawFrameBytes.Select(value => value.ToString("X2")));
        CandidateRawDelta = candidateRawDelta;
    }

    public int SequenceNumber { get; }
    public DateTimeOffset Timestamp { get; }
    public string RawFrameHex { get; }
    public string CandidateRawDelta { get; }
    public byte[] GetRawFrameBytesSnapshot() => _rawFrameBytes.ToArray();
}
