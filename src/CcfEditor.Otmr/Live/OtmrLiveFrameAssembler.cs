namespace CcfEditor.Otmr.Live;

/// <summary>
/// Reassembles the observed OTMR live stream framing (FB FB ... FF) across
/// arbitrary transport receive chunks. It deliberately does not interpret
/// payload bytes as events or CCF record numbers.
/// </summary>
public sealed class OtmrLiveFrameAssembler
{
    public const int DefaultMaximumFrameLength = 4096;

    private readonly int _maximumFrameLength;
    private readonly List<byte> _buffer = new();
    private bool _sawFirstHeaderByte;
    private bool _insideFrame;
    private bool _lastPayloadByteWasFb;

    public OtmrLiveFrameAssembler(int maximumFrameLength = DefaultMaximumFrameLength)
    {
        if (maximumFrameLength < 3)
            throw new ArgumentOutOfRangeException(nameof(maximumFrameLength));

        _maximumFrameLength = maximumFrameLength;
    }

    public int BufferedByteCount => _buffer.Count + (_sawFirstHeaderByte ? 1 : 0);

    public IReadOnlyList<OtmrLiveFrame> Append(ReadOnlySpan<byte> chunk) =>
        AppendWithAnalysis(chunk).CompletedFrames.Select(completion => completion.Frame).ToArray();

    /// <summary>
    /// Runs the normal assembler once and additionally reports which bytes in
    /// this exact transport chunk completed frames, remain partial, or were
    /// outside live framing. Frame assembly behavior is unchanged.
    /// </summary>
    public OtmrLiveFrameAppendAnalysis AppendWithAnalysis(ReadOnlySpan<byte> chunk)
    {
        var completed = new List<OtmrLiveFrameCompletion>();
        var outside = new bool[chunk.Length];
        int malformedCandidates = 0;

        for (int chunkOffset = 0; chunkOffset < chunk.Length; chunkOffset++)
        {
            byte value = chunk[chunkOffset];
            if (!_insideFrame)
            {
                if (value != 0xFB)
                {
                    if (_sawFirstHeaderByte)
                        malformedCandidates++;
                    _sawFirstHeaderByte = false;
                    outside[chunkOffset] = true;
                }
                else if (_sawFirstHeaderByte)
                {
                    StartFrame();
                }
                else
                {
                    _sawFirstHeaderByte = true;
                }
                continue;
            }

            // A fresh header before a terminator means the prior candidate was
            // malformed/incomplete. Re-synchronise at the newest FB FB pair.
            if (_lastPayloadByteWasFb && value == 0xFB)
            {
                malformedCandidates++;
                StartFrame();
                continue;
            }

            _buffer.Add(value);
            _lastPayloadByteWasFb = value == 0xFB;

            if (value == 0xFF)
            {
                completed.Add(new OtmrLiveFrameCompletion(
                    new OtmrLiveFrame(_buffer),
                    chunkOffset));
                Reset();
            }
            else if (_buffer.Count > _maximumFrameLength)
            {
                malformedCandidates++;
                bool trailingHeaderPrefix = value == 0xFB;
                Reset();
                _sawFirstHeaderByte = trailingHeaderPrefix;
            }
        }

        return new OtmrLiveFrameAppendAnalysis(
            completed,
            BuildOutsideSegments(chunk, outside),
            _insideFrame || _sawFirstHeaderByte,
            malformedCandidates);
    }

    public void Reset()
    {
        _buffer.Clear();
        _sawFirstHeaderByte = false;
        _insideFrame = false;
        _lastPayloadByteWasFb = false;
    }

    private static IReadOnlyList<OtmrRxOutsideSegment> BuildOutsideSegments(
        ReadOnlySpan<byte> chunk,
        IReadOnlyList<bool> outside)
    {
        var segments = new List<OtmrRxOutsideSegment>();
        int start = -1;
        for (int index = 0; index <= outside.Count; index++)
        {
            bool isOutside = index < outside.Count && outside[index];
            if (isOutside && start < 0)
                start = index;
            if ((!isOutside || index == outside.Count) && start >= 0)
            {
                int length = index - start;
                segments.Add(new OtmrRxOutsideSegment(start, chunk.Slice(start, length).ToArray()));
                start = -1;
            }
        }
        return segments;
    }

    private void StartFrame()
    {
        _buffer.Clear();
        _buffer.Add(0xFB);
        _buffer.Add(0xFB);
        _sawFirstHeaderByte = false;
        _insideFrame = true;
        _lastPayloadByteWasFb = false;
    }
}

public sealed record OtmrLiveFrameCompletion(OtmrLiveFrame Frame, int TerminatorChunkOffset);

public sealed class OtmrRxOutsideSegment
{
    private readonly byte[] _data;

    internal OtmrRxOutsideSegment(int startOffset, byte[] data)
    {
        StartOffset = startOffset;
        _data = data.ToArray();
    }

    public int StartOffset { get; }
    public ReadOnlyMemory<byte> Data => _data;
    public string Hex => string.Join(" ", _data.Select(value => value.ToString("X2")));
}

public sealed record OtmrLiveFrameAppendAnalysis(
    IReadOnlyList<OtmrLiveFrameCompletion> CompletedFrames,
    IReadOnlyList<OtmrRxOutsideSegment> OutsideSegments,
    bool HasPartialLiveFrame,
    int MalformedCandidateCount);

public sealed class OtmrLiveFrame
{
    private readonly byte[] _data;

    internal OtmrLiveFrame(IEnumerable<byte> data) => _data = data.ToArray();

    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;
    public string Hex => string.Join(" ", _data.Select(value => value.ToString("X2")));
    public byte[] GetDataSnapshot() => _data.ToArray();
}
