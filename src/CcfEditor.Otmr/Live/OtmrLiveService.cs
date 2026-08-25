using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using CcfEditor.Core;
using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Storage;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.Otmr.Live;

public sealed class OtmrLiveService : IDisposable
{
    private readonly IOtmrTransport _transport;
    private readonly SerialOtmrTransport? _serialTransport;
    private readonly object _captureSync = new();
    private readonly object _diagnosticSync = new();
    private readonly object _txInterpretationSync = new();
    private readonly object _frameSync = new();
    private readonly List<OtmrCaptureEntry> _capture = new();
    private readonly List<OtmrProtocolDiagnosticEntry> _diagnostics = new();
    private readonly OtmrLiveFrameAssembler _frameAssembler = new();
    private readonly OtmrProtocolFrameAssembler _protocolFrameAssembler = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _stateSync = new();
    private readonly OtmrLiveStartTiming _startTiming;
    private readonly OtmrLiveRestoreTiming _restoreTiming;
    private Channel<byte[]> _protocolFrames = CreateProtocolChannel();
    private OtmrSerialSettings? _settings;
    private IOtmrRecordingStore? _recordingStore;
    private IReadOnlyDictionary<byte, byte[]>? _cachedOriginalRecorderPages;
    private OtmrGeneratedConfigurationExchange? _cachedRestorationExchange;
    private DateTimeOffset? _liveStoppedAt;
    private string? _pendingTxInterpretation;
    private OtmrLiveState _state = OtmrLiveState.Disconnected;
    private bool _disposed;

    public OtmrLiveService(IOtmrTransport transport, OtmrLiveStartTiming? startTiming = null)
        : this(transport, startTiming, restoreTiming: null)
    {
    }

    internal OtmrLiveService(
        IOtmrTransport transport,
        OtmrLiveStartTiming? startTiming,
        OtmrLiveRestoreTiming? restoreTiming)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serialTransport = transport as SerialOtmrTransport;
        _startTiming = startTiming ?? OtmrLiveStartTiming.HardwareDefault;
        _restoreTiming = restoreTiming ?? OtmrLiveRestoreTiming.HardwareDefault;
        _transport.BytesReceived += Transport_BytesReceived;
        _transport.BytesTransmitted += Transport_BytesTransmitted;
        _transport.ErrorOccurred += Transport_ErrorOccurred;
        if (_serialTransport is not null)
            _serialTransport.DiagnosticOccurred += SerialTransport_DiagnosticOccurred;
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
    public event EventHandler<OtmrProtocolDiagnosticEventArgs>? DiagnosticAdded;

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

    public Task StartLiveAsync(CancellationToken cancellationToken = default) =>
        StartLiveCoreAsync(selectedCcf: null, cancellationToken);

    public Task StartLiveAsync(CcfDocument selectedCcf, CancellationToken cancellationToken = default) =>
        StartLiveCoreAsync(selectedCcf, cancellationToken);

    private async Task StartLiveCoreAsync(CcfDocument? selectedCcf, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != OtmrLiveState.ConnectedIdle || !_transport.IsConnected || _settings is null)
                throw new InvalidOperationException("Connect the OTMR before starting live output.");

            byte[] selectedCcfSnapshot;
            try
            {
                selectedCcfSnapshot = ValidateAndSnapshotSelectedCcf(selectedCcf);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetState(OtmrLiveState.RecorderConfigurationWriteBlocked);
                throw new OtmrConfigurationPreflightException(ex.Message, ex);
            }

            ResetDetection();
            IReadOnlyDictionary<byte, byte[]> recorderPages =
                await RunInterrogationAsync(cancellationToken).ConfigureAwait(false);

            OtmrGeneratedConfigurationExchange startExchange;
            OtmrGeneratedConfigurationExchange restorationExchange;
            IReadOnlyDictionary<byte, byte[]> frozenPages;
            try
            {
                SetState(OtmrLiveState.PreflightingConfiguration);
                ValidateRecorderCcfCompatibility(recorderPages, selectedCcfSnapshot);
                frozenPages = FreezeRecorderPages(recorderPages);
                startExchange = OtmrConfigurationExchangeGenerator.CreateStart(frozenPages, selectedCcfSnapshot);
                restorationExchange = OtmrConfigurationExchangeGenerator.CreateStopRestoration(frozenPages);
                ValidateGeneratedExchange(startExchange, "START");
                ValidateGeneratedExchange(restorationExchange, "STOP restoration");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetState(OtmrLiveState.RecorderConfigurationWriteBlocked);
                throw new OtmrConfigurationPreflightException(ex.Message, ex);
            }

            // Commit the frozen snapshot and restoration bytes before write 1/7.
            // Later interrogation traffic never replaces these objects.
            _cachedOriginalRecorderPages = frozenPages;
            _cachedRestorationExchange = restorationExchange;
            ReportDiagnostic("START preflight passed: selected CCF, six frozen recorder pages, seven START writes, seven restoration writes, framing, lengths, checksums, compatibility, and provenance validated.");

            await ExecuteGeneratedExchangeAsync(startExchange, "START", cancellationToken).ConfigureAwait(false);
            await WaitForStageAsync(0x13, OtmrLiveState.WaitingFor01_13, cancellationToken).ConfigureAwait(false);

            await PerformFinalLiveTransitionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OtmrConfigurationPreflightException)
        {
            // Preserve the explicit safety state for the operator and caller.
            throw;
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
            _operationLock.Release();
        }
    }

    private async Task<IReadOnlyDictionary<byte, byte[]>> RunInterrogationAsync(CancellationToken cancellationToken)
    {
        var pages = new Dictionary<byte, byte[]>();
        await SendAndWaitAsync(OtmrLiveStartProtocol.Query01, 0x01, OtmrLiveState.WaitingFor01_01, cancellationToken).ConfigureAwait(false);

        pages[0x02] = (await WaitForStageAsync(0x02, OtmrLiveState.WaitingFor01_02, cancellationToken).ConfigureAwait(false)).ToArray();
        await SendPairAsync(OtmrLiveStartProtocol.Acknowledge02, OtmrLiveStartProtocol.Query03, cancellationToken).ConfigureAwait(false);
        await WaitForStageAsync(0x03, OtmrLiveState.WaitingFor01_03, cancellationToken).ConfigureAwait(false);

        pages[0x04] = (await WaitForStageAsync(0x04, OtmrLiveState.WaitingFor01_04, cancellationToken).ConfigureAwait(false)).ToArray();
        await SendPairAsync(OtmrLiveStartProtocol.Acknowledge04, OtmrLiveStartProtocol.Query05, cancellationToken).ConfigureAwait(false);
        await WaitForStageAsync(0x05, OtmrLiveState.WaitingFor01_05, cancellationToken).ConfigureAwait(false);

        pages[0x06] = (await WaitForStageAsync(0x06, OtmrLiveState.WaitingFor01_06, cancellationToken).ConfigureAwait(false)).ToArray();
        await SendPairAsync(OtmrLiveStartProtocol.Acknowledge06, OtmrLiveStartProtocol.Query07, cancellationToken).ConfigureAwait(false);
        await WaitForStageAsync(0x07, OtmrLiveState.WaitingFor01_07, cancellationToken).ConfigureAwait(false);

        pages[0x08] = (await WaitForStageAsync(0x08, OtmrLiveState.WaitingFor01_08, cancellationToken).ConfigureAwait(false)).ToArray();
        await SendPairAsync(OtmrLiveStartProtocol.Acknowledge08, OtmrLiveStartProtocol.Query09, cancellationToken).ConfigureAwait(false);
        await WaitForStageAsync(0x09, OtmrLiveState.WaitingFor01_09, cancellationToken).ConfigureAwait(false);

        pages[0x0A] = (await WaitForStageAsync(0x0A, OtmrLiveState.WaitingFor01_0A, cancellationToken).ConfigureAwait(false)).ToArray();
        await SendPairAsync(OtmrLiveStartProtocol.Acknowledge0A, OtmrLiveStartProtocol.Query0B, cancellationToken).ConfigureAwait(false);
        await WaitForStageAsync(0x0B, OtmrLiveState.WaitingFor01_0B, cancellationToken).ConfigureAwait(false);
        pages[0x0C] = (await WaitForStageAsync(0x0C, OtmrLiveState.WaitingFor01_0C, cancellationToken).ConfigureAwait(false)).ToArray();
        return pages;
    }

    private async Task SendAndWaitAsync(
        ReadOnlyMemory<byte> write,
        byte expectedTransaction,
        OtmrLiveState waitingState,
        CancellationToken cancellationToken)
    {
        SetState(waitingState);
        await _transport.SendAsync(write, cancellationToken).ConfigureAwait(false);
        await WaitForExpectedFrameAsync(expectedTransaction, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendPairAsync(
        ReadOnlyMemory<byte> first,
        ReadOnlyMemory<byte> second,
        CancellationToken cancellationToken)
    {
        await _transport.SendAsync(first, cancellationToken).ConfigureAwait(false);
        await _transport.SendAsync(second, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> WaitForStageAsync(
        byte transaction,
        OtmrLiveState waitingState,
        CancellationToken cancellationToken)
    {
        SetState(waitingState);
        return await WaitForExpectedFrameAsync(transaction, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> WaitForExpectedFrameAsync(byte transaction, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_startTiming.StageReplyTimeout);

        byte[] frame;
        try
        {
            frame = await _protocolFrames.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The OTMR live-start interrogation timed out waiting for a complete 01 {transaction:X2} frame. " +
                "No later command or final live-start 01 07 was sent.", ex);
        }
        catch (ChannelClosedException ex)
        {
            throw new IOException(
                $"The serial transport failed while waiting for OTMR stage 01 {transaction:X2}.",
                ex.InnerException ?? ex);
        }

        if (!OtmrLiveStartProtocol.IsExpectedReply(transaction, frame))
        {
            string actual = frame.Length >= 2 ? $"01 {frame[1]:X2}" : "an invalid frame";
            throw new InvalidDataException(
                $"Expected OTMR stage 01 {transaction:X2}, but received {actual}. " +
                "The state machine stopped without sending any later command.");
        }
        return frame;
    }

    private async Task ExecuteGeneratedExchangeAsync(
        OtmrGeneratedConfigurationExchange exchange,
        string phase,
        CancellationToken cancellationToken)
    {
        OtmrGeneratedConfigurationWrite first = exchange.Writes[0];
        long firstStarted = Stopwatch.GetTimestamp();
        await SendGeneratedWriteAsync(first, phase, 1, cancellationToken).ConfigureAwait(false);
        ReportDiagnostic(FormatWriteDiagnostic(
            phase, 1, first, "no intervening reply in capture; paired with write 2/7", Stopwatch.GetElapsedTime(firstStarted)));

        byte[] expectedReplies = { 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12 };
        OtmrLiveState[] waitingStates =
        {
            OtmrLiveState.WaitingFor01_0D, OtmrLiveState.WaitingFor01_0E,
            OtmrLiveState.WaitingFor01_0F, OtmrLiveState.WaitingFor01_10,
            OtmrLiveState.WaitingFor01_11, OtmrLiveState.WaitingFor01_12
        };
        for (int index = 1; index < exchange.Writes.Count; index++)
        {
            OtmrGeneratedConfigurationWrite write = exchange.Writes[index];
            long started = Stopwatch.GetTimestamp();
            await SendGeneratedWriteAsync(write, phase, index + 1, cancellationToken).ConfigureAwait(false);
            await WaitForStageAsync(expectedReplies[index - 1], waitingStates[index - 1], cancellationToken).ConfigureAwait(false);
            ReportDiagnostic(FormatWriteDiagnostic(
                phase, index + 1, write, $"reply 01 {expectedReplies[index - 1]:X2} received", Stopwatch.GetElapsedTime(started)));
        }
    }

    private async Task SendGeneratedWriteAsync(
        OtmrGeneratedConfigurationWrite write,
        string phase,
        int number,
        CancellationToken cancellationToken)
    {
        string interpretation = $"{phase} WRITE {number}/7 | command/page {write.Bytes[1]:X2} | {write.Bytes.Length} bytes | check {write.Bytes[^2]:X2}";
        ReportDiagnostic(interpretation + " | transmitting");
        await SendWithInterpretationAsync(write.Bytes, interpretation, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendWithInterpretationAsync(
        ReadOnlyMemory<byte> bytes,
        string interpretation,
        CancellationToken cancellationToken)
    {
        lock (_txInterpretationSync)
            _pendingTxInterpretation = interpretation;
        try
        {
            await _transport.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_txInterpretationSync)
                _pendingTxInterpretation = null;
        }
    }

    private static string FormatWriteDiagnostic(
        string phase,
        int number,
        OtmrGeneratedConfigurationWrite write,
        string reply,
        TimeSpan elapsed) =>
        $"{phase} WRITE {number}/7 | command/page {write.Bytes[1]:X2} | {write.Bytes.Length} bytes | " +
        $"check {write.Bytes[^2]:X2} | {reply} | elapsed {elapsed.TotalMilliseconds:F4} ms";

    private async Task PerformFinalLiveTransitionAsync(CancellationToken cancellationToken)
    {
        SetState(OtmrLiveState.StartingLive);
        ReportHighResolutionDiagnostic("LIVE TRANSITION 1/11: final 01 07 write begins");
        await _transport.SendAsync(OtmrLiveStartProtocol.FinalLiveStart07, cancellationToken).ConfigureAwait(false);
        ReportHighResolutionDiagnostic("LIVE TRANSITION 2/11: final 01 07 write returned");

        if (_startTiming.FinalCommandToCloseDelay > TimeSpan.Zero)
        {
            ReportHighResolutionDiagnostic(
                $"LIVE TRANSITION 3/11: {_startTiming.FinalCommandToCloseDelay.TotalMilliseconds:F4} ms delay begins");
            await Task.Delay(_startTiming.FinalCommandToCloseDelay, cancellationToken).ConfigureAwait(false);
            ReportHighResolutionDiagnostic(
                $"LIVE TRANSITION 4/11: {_startTiming.FinalCommandToCloseDelay.TotalMilliseconds:F4} ms delay ends");
        }

        ReportHighResolutionDiagnostic("LIVE TRANSITION: transport disconnect begins");
        await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        ReportHighResolutionDiagnostic("LIVE TRANSITION: transport disconnect returned");
        ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(false, null));

        ResetDetection();
        OtmrSerialSettings liveSettings = _settings! with { RtsEnable = false, DtrEnable = true };
        // Arm recognition before opening; the first complete frame may arrive
        // immediately during the DTR-HIGH reopen callback.
        SetState(OtmrLiveState.WaitingForLiveFrames);
        ReportHighResolutionDiagnostic("LIVE TRANSITION 7/11: reopen begins");
        await _transport.ConnectAsync(liveSettings, cancellationToken).ConfigureAwait(false);
        ReportHighResolutionDiagnostic("LIVE TRANSITION: transport reopen returned");
        ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(true, liveSettings.PortName));
    }

    public async Task StopLiveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is not (OtmrLiveState.LiveActive or OtmrLiveState.WaitingForLiveFrames))
                throw new InvalidOperationException("Stop Live is available only while live output is active or awaiting its first frame.");
            await StopLiveCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StopAndRestoreAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is OtmrLiveState.LiveActive or OtmrLiveState.WaitingForLiveFrames)
                await StopLiveCoreAsync(cancellationToken).ConfigureAwait(false);
            if (State != OtmrLiveState.NotLive || _settings is null)
                throw new InvalidOperationException("Stop + Restore requires a stopped live session with retained serial settings.");
            if (_cachedOriginalRecorderPages is null || _cachedRestorationExchange is null)
                throw new OtmrConfigurationPreflightException("No frozen pre-START recorder snapshot is available for restoration.");

            if (_liveStoppedAt.HasValue)
            {
                TimeSpan elapsed = DateTimeOffset.UtcNow - _liveStoppedAt.Value;
                TimeSpan remaining = _restoreTiming.LiveCloseToRestoreOpenDelay - elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
            }

            ResetDetection();
            OtmrSerialSettings restoreSettings = _settings with { RtsEnable = false, DtrEnable = false };
            await _transport.ConnectAsync(restoreSettings, cancellationToken).ConfigureAwait(false);
            ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(true, restoreSettings.PortName));
            SetState(OtmrLiveState.RestoringOriginalConfiguration);
            ReportDiagnostic("STOP + RESTORE opened 38400/8N1, RTS LOW, DTR LOW; beginning captured cleanup interrogation.");

            // Fresh replies gate the cleanup protocol but never replace the
            // frozen pre-START source used by the restoration generator.
            await RunInterrogationAsync(cancellationToken).ConfigureAwait(false);
            ValidateGeneratedExchange(_cachedRestorationExchange, "STOP restoration");
            await ExecuteGeneratedExchangeAsync(_cachedRestorationExchange, "RESTORE", cancellationToken).ConfigureAwait(false);
            await WaitForStageAsync(0x13, OtmrLiveState.WaitingFor01_13, cancellationToken).ConfigureAwait(false);

            SetState(OtmrLiveState.RestoringOriginalConfiguration);
            const string finalRestoreInterpretation =
                "RESTORE final 01 07 after exact complete 01 13; captured cleanup close follows";
            ReportDiagnostic(finalRestoreInterpretation);
            long finalRestoreStarted = Stopwatch.GetTimestamp();
            await SendWithInterpretationAsync(
                OtmrLiveStartProtocol.FinalLiveStart07,
                finalRestoreInterpretation,
                cancellationToken).ConfigureAwait(false);
            if (_restoreTiming.FinalCommandToCloseDelay > TimeSpan.Zero)
                WaitUntilElapsed(finalRestoreStarted, _restoreTiming.FinalCommandToCloseDelay, cancellationToken);
            await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(false, null));
            ResetDetection();
            SetState(OtmrLiveState.NotLive);
            ReportDiagnostic("STOP + RESTORE completed captured cleanup exchange and final close; COM remains closed and realtime is not active.");
        }
        catch (OperationCanceledException)
        {
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
            _operationLock.Release();
        }
    }

    private async Task StopLiveCoreAsync(CancellationToken cancellationToken)
    {
        int txBeforeClose;
        lock (_captureSync)
            txBeforeClose = _capture.Count(entry => entry.Direction == OtmrDirection.Tx);
        await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        ConnectionChanged?.Invoke(this, new OtmrConnectionChangedEventArgs(false, null));
        ResetDetection();
        _liveStoppedAt = DateTimeOffset.UtcNow;
        SetState(OtmrLiveState.NotLive);
        int txAfterClose;
        lock (_captureSync)
            txAfterClose = _capture.Count(entry => entry.Direction == OtmrDirection.Tx);
        if (txAfterClose != txBeforeClose)
            throw new InvalidOperationException("Stop Live unexpectedly transmitted serial data.");
        ReportDiagnostic("STOP LIVE: closed the RTS LOW / DTR HIGH live COM port; no stop command was transmitted. Original configuration remains cached for the separate Stop + Restore operation.");
    }

    private static byte[] ValidateAndSnapshotSelectedCcf(CcfDocument? selectedCcf)
    {
        if (selectedCcf is null)
            throw new InvalidOperationException("An explicitly selected Class 171 CCF is required before Start OTMR Live.");

        CcfValidationIssue[] errors = CcfValidator.ValidateMilestone1(selectedCcf)
            .Where(issue => issue.Severity == CcfValidationSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException("Selected CCF validation failed: " + string.Join("; ", errors.Select(issue => issue.Message)));

        byte[] snapshot = selectedCcf.GetWorkingBytesSnapshot();
        string vehicleType = Encoding.ASCII
            .GetString(snapshot, CcfFieldDefinitions.Header.VehicleType, 8)
            .TrimEnd('\0', ' ');
        if (!string.Equals(vehicleType, "171", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(vehicleType, "Class 171", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Selected CCF vehicle type '{vehicleType}' is not compatible with the Class 171 live workflow.");
        }
        return snapshot;
    }

    private static void ValidateRecorderCcfCompatibility(
        IReadOnlyDictionary<byte, byte[]> recorderPages,
        ReadOnlySpan<byte> selectedCcf)
    {
        if (!recorderPages.TryGetValue(0x02, out byte[]? page01))
            throw new InvalidOperationException("Recorder/CCF compatibility cannot be checked because RX 01 02 is missing.");
        ReadOnlySpan<byte> payload = page01.AsSpan(9, page01.Length - 12);
        const int ccfIdentityOffset = 0x01B0;
        const int identityLength = 41;
        ReadOnlySpan<byte> recorderIdentity = payload.Slice(5, identityLength);
        ReadOnlySpan<byte> ccfIdentity = selectedCcf.Slice(ccfIdentityOffset, identityLength);
        if (!recorderIdentity.SequenceEqual(ccfIdentity))
        {
            int mismatch = 0;
            while (mismatch < identityLength && recorderIdentity[mismatch] == ccfIdentity[mismatch])
                mismatch++;
            throw new InvalidOperationException(
                $"Recorder/CCF identity compatibility failed at CCF 0x{ccfIdentityOffset + mismatch:X4}: " +
                $"recorder {recorderIdentity[mismatch]:X2}, selected CCF {ccfIdentity[mismatch]:X2}.");
        }
    }

    private static IReadOnlyDictionary<byte, byte[]> FreezeRecorderPages(
        IReadOnlyDictionary<byte, byte[]> pages) =>
        pages.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

    private static void WaitUntilElapsed(long started, TimeSpan targetElapsed, CancellationToken cancellationToken)
    {
        // The captured STOP interval is only 4.5618 ms. Windows Task.Delay
        // commonly rounds this to a 15.6 ms scheduler tick, so measure from
        // the write start and use a bounded spin for this one short transition.
        while (Stopwatch.GetElapsedTime(started) < targetElapsed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.SpinWait(64);
        }
    }

    private static void ValidateGeneratedExchange(
        OtmrGeneratedConfigurationExchange exchange,
        string phase)
    {
        int[] expectedLengths = { 13, 0x10B, 0x10B, 0x10B, 0x10B, 0x10B, 0xD2 };
        byte[] expectedPayloadLengths = { 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xC6 };
        byte[] expectedCommands = { 0x0C, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
        if (exchange.Writes.Count != 7)
            throw new InvalidOperationException($"{phase} generation produced {exchange.Writes.Count} writes; exactly seven are required.");

        for (int index = 0; index < exchange.Writes.Count; index++)
        {
            OtmrGeneratedConfigurationWrite write = exchange.Writes[index];
            byte[] bytes = write.Bytes;
            if (bytes.Length != expectedLengths[index])
                throw new InvalidOperationException($"{phase} write {index + 1}/7 length is {bytes.Length}; expected {expectedLengths[index]}.");
            if (bytes[0] != 0x01 || bytes[1] != expectedCommands[index] || bytes[8] != 0x02 ||
                bytes[7] != expectedPayloadLengths[index] || bytes[^3] != 0x03 || bytes[^1] != 0x04)
                throw new InvalidOperationException($"{phase} write {index + 1}/7 framing or ordering validation failed.");
            if (!OtmrProtocolDerivation.HasValidPayloadCheckByte(bytes))
                throw new InvalidOperationException($"{phase} write {index + 1}/7 has an invalid calculated payload check byte.");
            if (write.Provenance.Count != bytes.Length ||
                write.Provenance.Any(item => string.IsNullOrWhiteSpace(item.SourceReference) ||
                    !Enum.IsDefined(item.Source)))
            {
                throw new InvalidOperationException($"{phase} write {index + 1}/7 has missing or unknown byte provenance.");
            }
            for (int offset = 0; offset < bytes.Length; offset++)
            {
                OtmrConfigurationByteProvenance provenance = write.Provenance[offset];
                if (provenance.FrameOffset != offset || provenance.Value != bytes[offset])
                    throw new InvalidOperationException($"{phase} write {index + 1}/7 provenance mismatch at frame offset 0x{offset:X3}.");
            }
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
            _cachedOriginalRecorderPages = null;
            _cachedRestorationExchange = null;
            _liveStoppedAt = null;
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

    public IReadOnlyList<OtmrProtocolDiagnosticEntry> GetDiagnosticSnapshot()
    {
        lock (_diagnosticSync)
            return _diagnostics.ToArray();
    }

    public void ClearCapture()
    {
        lock (_captureSync)
            _capture.Clear();
    }

    private void Transport_BytesReceived(object? sender, OtmrBytesReceivedEventArgs e)
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;

        IReadOnlyList<OtmrLiveFrame> liveFrames;
        IReadOnlyList<byte[]> protocolFrames;
        lock (_frameSync)
        {
            protocolFrames = _protocolFrameAssembler.Append(e.Data);
            liveFrames = _frameAssembler.Append(e.Data);
            foreach (byte[] frame in protocolFrames)
                _protocolFrames.Writer.TryWrite(frame);
        }

        string? interpretation = protocolFrames.Count == 0
            ? null
            : protocolFrames.Count == 1
                ? $"Complete OTMR protocol frame 01 {protocolFrames[0][1]:X2} assembled"
                : $"{protocolFrames.Count} complete OTMR protocol frames assembled";
        AddCapture(new OtmrCaptureEntry(timestamp, OtmrDirection.Rx, e.Data, interpretation));

        foreach (OtmrLiveFrame frame in liveFrames)
        {
            if (State == OtmrLiveState.WaitingForLiveFrames)
                SetState(OtmrLiveState.LiveActive);
            _recordingStore?.TryRecordLiveFrame(timestamp, frame);
            FrameReceived?.Invoke(this, new OtmrLiveFrameEventArgs(timestamp, frame));
        }
    }

    private void Transport_BytesTransmitted(object? sender, OtmrBytesTransmittedEventArgs e)
    {
        string? interpretation;
        lock (_txInterpretationSync)
            interpretation = _pendingTxInterpretation;
        interpretation ??= OtmrLiveStartProtocol.InterpretationForTx(e.Data);
        AddCapture(new OtmrCaptureEntry(DateTimeOffset.Now, OtmrDirection.Tx, e.Data, interpretation));
    }

    private void ReportDiagnostic(string message)
    {
        var entry = new OtmrProtocolDiagnosticEntry(DateTimeOffset.Now, message);
        lock (_diagnosticSync)
            _diagnostics.Add(entry);
        DiagnosticAdded?.Invoke(this, new OtmrProtocolDiagnosticEventArgs(entry));
    }

    private void ReportHighResolutionDiagnostic(string message)
    {
        long timestamp = Stopwatch.GetTimestamp();
        ReportDiagnostic($"{message} | stopwatch={timestamp} | frequency={Stopwatch.Frequency}");
    }

    private void SerialTransport_DiagnosticOccurred(object? sender, OtmrTransportDiagnosticEventArgs e) =>
        ReportDiagnostic(
            $"TRANSPORT {e.Stage}: {e.Detail} | wall={e.Timestamp:O} | " +
            $"stopwatch={e.StopwatchTimestamp} | frequency={e.StopwatchFrequency}");

    private void AddCapture(OtmrCaptureEntry entry)
    {
        lock (_captureSync)
            _capture.Add(entry);
        _recordingStore?.TryRecordRaw(entry);
        CaptureAdded?.Invoke(this, new OtmrCaptureEntryEventArgs(entry));
    }

    private void Transport_ErrorOccurred(object? sender, OtmrTransportErrorEventArgs e)
    {
        lock (_frameSync)
            _protocolFrames.Writer.TryComplete(e.Exception);
        SetState(OtmrLiveState.Error);
        ErrorOccurred?.Invoke(this, new OtmrLiveErrorEventArgs(e.Exception));
    }

    private void ResetDetection()
    {
        lock (_frameSync)
        {
            _frameAssembler.Reset();
            _protocolFrameAssembler.Reset();
            _protocolFrames.Writer.TryComplete();
            _protocolFrames = CreateProtocolChannel();
        }
    }

    private static Channel<byte[]> CreateProtocolChannel() =>
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

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
        if (_serialTransport is not null)
            _serialTransport.DiagnosticOccurred -= SerialTransport_DiagnosticOccurred;
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

public sealed record OtmrProtocolDiagnosticEntry(DateTimeOffset Timestamp, string Message);

public sealed class OtmrProtocolDiagnosticEventArgs : EventArgs
{
    public OtmrProtocolDiagnosticEventArgs(OtmrProtocolDiagnosticEntry entry) => Entry = entry;
    public OtmrProtocolDiagnosticEntry Entry { get; }
}

public sealed class OtmrLiveErrorEventArgs : EventArgs
{
    public OtmrLiveErrorEventArgs(Exception exception) => Exception = exception;
    public Exception Exception { get; }
}
