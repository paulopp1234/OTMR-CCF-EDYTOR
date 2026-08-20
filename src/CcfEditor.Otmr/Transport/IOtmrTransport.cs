namespace CcfEditor.Otmr.Transport;

public interface IOtmrTransport : IDisposable
{
    bool IsConnected { get; }
    event EventHandler<OtmrBytesReceivedEventArgs>? BytesReceived;
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

public sealed class OtmrTransportErrorEventArgs : EventArgs
{
    public OtmrTransportErrorEventArgs(Exception exception) => Exception = exception;
    public Exception Exception { get; }
}
