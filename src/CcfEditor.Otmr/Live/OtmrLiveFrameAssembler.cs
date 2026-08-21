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

    public IReadOnlyList<OtmrLiveFrame> Append(ReadOnlySpan<byte> chunk)
    {
        var completed = new List<OtmrLiveFrame>();

        foreach (byte value in chunk)
        {
            if (!_insideFrame)
            {
                HuntForHeader(value);
                continue;
            }

            // A fresh header before a terminator means the prior candidate was
            // malformed/incomplete. Re-synchronise at the newest FB FB pair.
            if (_lastPayloadByteWasFb && value == 0xFB)
            {
                StartFrame();
                continue;
            }

            _buffer.Add(value);
            _lastPayloadByteWasFb = value == 0xFB;

            if (value == 0xFF)
            {
                completed.Add(new OtmrLiveFrame(_buffer));
                Reset();
            }
            else if (_buffer.Count > _maximumFrameLength)
            {
                bool trailingHeaderPrefix = value == 0xFB;
                Reset();
                _sawFirstHeaderByte = trailingHeaderPrefix;
            }
        }

        return completed;
    }

    public void Reset()
    {
        _buffer.Clear();
        _sawFirstHeaderByte = false;
        _insideFrame = false;
        _lastPayloadByteWasFb = false;
    }

    private void HuntForHeader(byte value)
    {
        if (value != 0xFB)
        {
            _sawFirstHeaderByte = false;
            return;
        }

        if (_sawFirstHeaderByte)
        {
            StartFrame();
            return;
        }

        _sawFirstHeaderByte = true;
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

public sealed class OtmrLiveFrame
{
    private readonly byte[] _data;

    internal OtmrLiveFrame(IEnumerable<byte> data) => _data = data.ToArray();

    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;
    public string Hex => string.Join(" ", _data.Select(value => value.ToString("X2")));
    public byte[] GetDataSnapshot() => _data.ToArray();
}
