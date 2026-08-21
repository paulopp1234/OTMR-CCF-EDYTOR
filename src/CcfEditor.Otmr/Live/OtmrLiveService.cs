using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.Otmr.Live;

public sealed class OtmrLiveService : IDisposable
{
    private readonly IOtmrTransport _transport;
    private readonly object _captureSync = new();
    private readonly object _frameSync = new();
    private readonly List<OtmrCaptureEntry> _capture = new();
    private readonly OtmrLiveFrameAssembler _frameAssembler = new();
    private bool _disposed;

    public OtmrLiveService(IOtmrTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _transport.BytesReceived += Transport_BytesReceived;
        _transport.BytesTransmitted += Transport_BytesTransmitted;
        _transport.ErrorOccurred += Transport_ErrorOccurred;
    }

    public bool IsConnected => _transport.IsConnected;

    public event EventHandler<OtmrCaptureEntryEventArgs>? CaptureAdded;
    public event EventHandler<OtmrLiveFrameEventArgs>? FrameReceived;
    public event EventHandler<OtmrConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<OtmrLiveErrorEventArgs>? ErrorOccurred;

    public Task ConnectAsync(OtmrSerialSettings settings, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return ConnectCoreAsync(settings, cancellationToken);
    }

    private async Task ConnectCoreAsync(OtmrSerialSettings settings, CancellationToken cancellationToken)
    {
        lock (_frameSync)
            _frameAssembler.Reset();
        await _transport.ConnectAsync(settings, cancellationToken).ConfigureAwait(false);
        ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(true, settings.PortName));
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        lock (_frameSync)
            _frameAssembler.Reset();
        ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(false, null));
    }

    public IReadOnlyList<OtmrCaptureEntry> GetCaptureSnapshot()
    {
        lock (_captureSync)
            return _capture.ToArray();
    }

    public void ClearCapture()
    {
        lock (_captureSync)
            _capture.Clear();
    }

    private void Transport_BytesReceived(object? sender, OtmrBytesReceivedEventArgs e)
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;
        AddCapture(new OtmrCaptureEntry(timestamp, OtmrDirection.Rx, e.Data));

        IReadOnlyList<OtmrLiveFrame> frames;
        lock (_frameSync)
            frames = _frameAssembler.Append(e.Data);

        foreach (OtmrLiveFrame frame in frames)
            FrameReceived?.Invoke(this, new OtmrLiveFrameEventArgs(timestamp, frame));
    }

    private void Transport_BytesTransmitted(object? sender, OtmrBytesTransmittedEventArgs e) =>
        AddCapture(new OtmrCaptureEntry(DateTimeOffset.Now, OtmrDirection.Tx, e.Data));

    private void AddCapture(OtmrCaptureEntry entry)
    {
        lock (_captureSync)
            _capture.Add(entry);
        CaptureAdded?.Invoke(this, new OtmrCaptureEntryEventArgs(entry));
    }

    private void Transport_ErrorOccurred(object? sender, OtmrTransportErrorEventArgs e) =>
        ErrorOccurred?.Invoke(this, new OtmrLiveErrorEventArgs(e.Exception));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_transport.IsConnected)
                _transport.DisconnectAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Shutdown must not throw.
        }

        _transport.BytesReceived -= Transport_BytesReceived;
        _transport.BytesTransmitted -= Transport_BytesTransmitted;
        _transport.ErrorOccurred -= Transport_ErrorOccurred;
        _transport.Dispose();
        _disposed = true;
    }
}

public sealed class OtmrCaptureEntryEventArgs : EventArgs
{
    public OtmrCaptureEntryEventArgs(OtmrCaptureEntry entry) => Entry = entry;
    public OtmrCaptureEntry Entry { get; }
}

public sealed class OtmrConnectionChangedEventArgs : EventArgs
{
    public OtmrConnectionChangedEventArgs(bool isConnected, string? portName)
    {
        IsConnected = isConnected;
        PortName = portName;
    }

    public bool IsConnected { get; }
    public string? PortName { get; }
}

public sealed class OtmrLiveFrameEventArgs : EventArgs
{
    public OtmrLiveFrameEventArgs(DateTimeOffset timestamp, OtmrLiveFrame frame)
    {
        Timestamp = timestamp;
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public DateTimeOffset Timestamp { get; }
    public OtmrLiveFrame Frame { get; }
}

public sealed class OtmrLiveErrorEventArgs : EventArgs
{
    public OtmrLiveErrorEventArgs(Exception exception) => Exception = exception;
    public Exception Exception { get; }
}
