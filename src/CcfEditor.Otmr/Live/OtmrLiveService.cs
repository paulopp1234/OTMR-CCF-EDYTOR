using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Storage;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.Otmr.Live;

public sealed class OtmrLiveService : IDisposable
{
    private readonly IOtmrTransport _transport;
    private readonly object _captureSync = new();
    private readonly object _frameSync = new();
    private readonly List<OtmrCaptureEntry> _capture = new();
    private readonly OtmrLiveFrameAssembler _frameAssembler = new();
    private readonly OtmrByteSequenceDetector _replyDetector =
        new(OtmrLiveStartProtocol.ExpectedReplyPrefix.Span);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _stateSync = new();
    private readonly OtmrLiveStartTiming _startTiming;
    private OtmrSerialSettings? _settings;
    private IOtmrRecordingStore? _recordingStore;
    private TaskCompletionSource<bool>? _replyCompletion;
    private OtmrLiveState _state = OtmrLiveState.Disconnected;
    private bool _disposed;

    public OtmrLiveService(IOtmrTransport transport, OtmrLiveStartTiming? startTiming = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _startTiming = startTiming ?? OtmrLiveStartTiming.HardwareDefault;
        _transport.BytesReceived += Transport_BytesReceived;
        _transport.BytesTransmitted += Transport_BytesTransmitted;
        _transport.ErrorOccurred += Transport_ErrorOccurred;
    }

    public bool IsConnected => _transport.IsConnected;
    public bool IsLiveActive => State == OtmrLiveState.LiveActive;
    public OtmrLiveState State
    {
        get
        {
            lock (_stateSync)
                return _state;
        }
    }

    public event EventHandler<OtmrCaptureEntryEventArgs>? CaptureAdded;
    public event EventHandler<OtmrLiveFrameEventArgs>? FrameReceived;
    public event EventHandler<OtmrConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<OtmrLiveErrorEventArgs>? ErrorOccurred;
    public event EventHandler<OtmrLiveStateChangedEventArgs>? StateChanged;

    public void SetRecordingStore(IOtmrRecordingStore? recordingStore) =>
        _recordingStore = recordingStore;

    public Task ConnectAsync(OtmrSerialSettings settings, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return ConnectCoreAsync(settings, cancellationToken);
    }

    private async Task ConnectCoreAsync(OtmrSerialSettings settings, CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != OtmrLiveState.Disconnected)
                throw new InvalidOperationException("Disconnect the current OTMR session before connecting again.");
            ResetDetection();
            OtmrSerialSettings idleSettings = settings with { RtsEnable = false, DtrEnable = false };
            _settings = idleSettings;
            await _transport.ConnectAsync(idleSettings, cancellationToken).ConfigureAwait(false);
            SetState(OtmrLiveState.ConnectedIdle);
            ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(true, idleSettings.PortName));
        }
        catch
        {
            SetState(OtmrLiveState.Error);
            throw;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StartLiveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != OtmrLiveState.ConnectedIdle || !_transport.IsConnected || _settings is null)
                throw new InvalidOperationException("Connect the OTMR before starting live output.");

            ResetDetection();
            var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _replyCompletion = reply;
            SetState(OtmrLiveState.QuerySent);
            await _transport.SendAsync(OtmrLiveStartProtocol.ProvenQueryFrame, cancellationToken).ConfigureAwait(false);

            try
            {
                await reply.Task.WaitAsync(_startTiming.QueryReplyTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    "The OTMR did not return the expected proven 01 01 reply. The candidate live-start frame was not sent.", ex);
            }

            await Task.Delay(_startTiming.AfterReplyDelay, cancellationToken).ConfigureAwait(false);
            SetState(OtmrLiveState.StartingLive);
            await _transport.SendAsync(OtmrLiveStartProtocol.CandidateLiveStartFrame, cancellationToken).ConfigureAwait(false);
            await Task.Delay(_startTiming.AfterLiveCommandDelay, cancellationToken).ConfigureAwait(false);

            await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(false, null));
            await Task.Delay(_startTiming.ReopenDelay, cancellationToken).ConfigureAwait(false);

            ResetDetection();
            OtmrSerialSettings liveSettings = _settings with { RtsEnable = false, DtrEnable = true };
            // Arm live-frame recognition before opening so an immediate first
            // serial callback cannot arrive in the prior StartingLive state.
            SetState(OtmrLiveState.WaitingForLiveFrames);
            await _transport.ConnectAsync(liveSettings, cancellationToken).ConfigureAwait(false);
            ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(true, liveSettings.PortName));
        }
        catch (OperationCanceledException)
        {
            if (State != OtmrLiveState.Disconnected)
                SetState(OtmrLiveState.Error);
            throw;
        }
        catch
        {
            SetState(OtmrLiveState.Error);
            throw;
        }
        finally
        {
            _replyCompletion = null;
            _operationLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            _settings = null;
            ResetDetection();
            SetState(OtmrLiveState.Disconnected);
            ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(false, null));
        }
        finally
        {
            _operationLock.Release();
        }
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

        IReadOnlyList<OtmrLiveFrame> frames;
        bool expectedReply;
        lock (_frameSync)
        {
            expectedReply = _replyDetector.Append(e.Data);
            frames = _frameAssembler.Append(e.Data);
        }

        AddCapture(new OtmrCaptureEntry(
            timestamp,
            OtmrDirection.Rx,
            e.Data,
            expectedReply ? "Expected proven OTMR 01 01 reply detected" : null));

        if (expectedReply && State == OtmrLiveState.QuerySent)
        {
            SetState(OtmrLiveState.OtmrReplied);
            _replyCompletion?.TrySetResult(true);
        }

        foreach (OtmrLiveFrame frame in frames)
        {
            if (State == OtmrLiveState.WaitingForLiveFrames)
                SetState(OtmrLiveState.LiveActive);
            _recordingStore?.TryRecordLiveFrame(timestamp, frame);
            FrameReceived?.Invoke(this, new OtmrLiveFrameEventArgs(timestamp, frame));
        }
    }

    private void Transport_BytesTransmitted(object? sender, OtmrBytesTransmittedEventArgs e)
    {
        string? interpretation = e.Data.AsSpan().SequenceEqual(OtmrLiveStartProtocol.ProvenQueryFrame.Span)
            ? "Proven OTMR interrogation/query frame"
            : e.Data.AsSpan().SequenceEqual(OtmrLiveStartProtocol.CandidateLiveStartFrame.Span)
                ? "Evidence-backed CANDIDATE Arrowvale live-start frame"
                : null;
        AddCapture(new OtmrCaptureEntry(DateTimeOffset.Now, OtmrDirection.Tx, e.Data, interpretation));
    }

    private void AddCapture(OtmrCaptureEntry entry)
    {
        lock (_captureSync)
            _capture.Add(entry);
        _recordingStore?.TryRecordRaw(entry);
        CaptureAdded?.Invoke(this, new OtmrCaptureEntryEventArgs(entry));
    }

    private void Transport_ErrorOccurred(object? sender, OtmrTransportErrorEventArgs e)
    {
        SetState(OtmrLiveState.Error);
        _replyCompletion?.TrySetException(e.Exception);
        ErrorOccurred?.Invoke(this, new OtmrLiveErrorEventArgs(e.Exception));
    }

    private void ResetDetection()
    {
        lock (_frameSync)
        {
            _frameAssembler.Reset();
            _replyDetector.Reset();
        }
    }

    private void SetState(OtmrLiveState state)
    {
        lock (_stateSync)
        {
            if (_state == state)
                return;
            _state = state;
        }
        StateChanged?.Invoke(this, new OtmrLiveStateChangedEventArgs(state));
    }

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
