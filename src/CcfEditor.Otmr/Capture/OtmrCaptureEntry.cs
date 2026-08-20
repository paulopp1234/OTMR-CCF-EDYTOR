namespace CcfEditor.Otmr.Capture;

public sealed class OtmrCaptureEntry
{
    private readonly byte[] _data;

    public OtmrCaptureEntry(DateTimeOffset timestamp, OtmrDirection direction, byte[] data, string? interpretation = null)
    {
        Timestamp = timestamp;
        Direction = direction;
        _data = data?.ToArray() ?? throw new ArgumentNullException(nameof(data));
        Interpretation = interpretation;
    }

    public DateTimeOffset Timestamp { get; }
    public OtmrDirection Direction { get; }
    public ReadOnlyMemory<byte> Data => _data;
    public string? Interpretation { get; }
    public byte[] GetDataSnapshot() => _data.ToArray();
    public string Hex => string.Join(" ", _data.Select(b => b.ToString("X2")));
}
