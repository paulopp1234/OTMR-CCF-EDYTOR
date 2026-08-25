namespace CcfEditor.Otmr.Transport;

public interface IOtmrTransport : IDisposable
{
    bool IsConnected { get; }
    event EventHandler<OtmrBytesReceivedEventArgs>? BytesReceived;
    event EventHandler<OtmrBytesTransmittedEventArgs>? BytesTransmitted;
    event EventHandler<OtmrTransportErrorEventArgs>? ErrorOccurred;

    Task ConnectAsync(OtmrSerialSettings settings, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

public sealed class OtmrBytesReceivedEventArgs : EventArgs
{
    public OtmrBytesReceivedEventArgs(byte[] data) => Data = data.ToArray();
    public byte[] Data { get; }
}

public sealed class OtmrBytesTransmittedEventArgs : EventArgs
{
    public OtmrBytesTransmittedEventArgs(byte[] data) => Data = data.ToArray();
    public byte[] Data { get; }
}

public sealed class OtmrTransportErrorEventArgs : EventArgs
{
    public OtmrTransportErrorEventArgs(Exception exception) => Exception = exception;
    public Exception Exception { get; }
}

public sealed class OtmrTransportDiagnosticEventArgs : EventArgs
{
    public OtmrTransportDiagnosticEventArgs(
        DateTimeOffset timestamp,
        long stopwatchTimestamp,
        long stopwatchFrequency,
        string stage,
        string detail)
    {
        Timestamp = timestamp;
        StopwatchTimestamp = stopwatchTimestamp;
        StopwatchFrequency = stopwatchFrequency;
        Stage = stage;
        Detail = detail;
    }

    public DateTimeOffset Timestamp { get; }
    public long StopwatchTimestamp { get; }
    public long StopwatchFrequency { get; }
    public string Stage { get; }
    public string Detail { get; }
}
