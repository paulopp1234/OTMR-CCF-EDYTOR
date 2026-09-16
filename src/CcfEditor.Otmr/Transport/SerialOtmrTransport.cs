using System.IO.Ports;
using System.Diagnostics;

namespace CcfEditor.Otmr.Transport;

public sealed class SerialOtmrTransport : IOtmrTransport
{
    private readonly object _sync = new();
    private SerialPort? _serialPort;
    private WindowsNativeLiveSerialPort? _nativeLivePort;
    private int _firstRxReported;
    private bool _disposed;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
                return _serialPort?.IsOpen == true || _nativeLivePort?.IsOpen == true;
        }
    }

    public event EventHandler<OtmrBytesReceivedEventArgs>? BytesReceived;
    public event EventHandler<OtmrBytesTransmittedEventArgs>? BytesTransmitted;
    public event EventHandler<OtmrTransportErrorEventArgs>? ErrorOccurred;
    public event EventHandler<OtmrTransportDiagnosticEventArgs>? DiagnosticOccurred;

    public static string[] GetAvailablePorts() =>
        SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    internal bool TryGetNativeLiveReceiveStatus(
        out NativeLiveReceiveStatistics statistics,
        out string queueStatus)
    {
        WindowsNativeLiveSerialPort? native;
        lock (_sync)
            native = _nativeLivePort is { IsOpen: true } ? _nativeLivePort : null;
        if (native is null)
        {
            statistics = default;
            queueStatus = "native live port is not open";
            return false;
        }

        statistics = native.GetReceiveStatistics();
        queueStatus = native.QueryReceiveQueue();
        return true;
    }

    public async Task ConnectAsync(OtmrSerialSettings settings, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_serialPort?.IsOpen == true || _nativeLivePort?.IsOpen == true)
                throw new InvalidOperationException("OTMR serial port is already connected.");
        }

        if (settings.DtrEnable && !settings.RtsEnable)
        {
            Interlocked.Exchange(ref _firstRxReported, 0);
            ReportDiagnostic("PORT_OPEN_BEGIN", $"{settings.PortName} captured Windows-native live reopen");
            WindowsNativeLiveSerialPort native = await WindowsNativeLiveSerialPort.OpenAsync(
                settings,
                ReportDiagnostic,
                bytes => BytesReceived?.Invoke(this, new OtmrBytesReceivedEventArgs(bytes)),
                ex => ErrorOccurred?.Invoke(this, new OtmrTransportErrorEventArgs(ex)),
                cancellationToken).ConfigureAwait(false);
            lock (_sync)
                _nativeLivePort = native;
            ReportDiagnostic("PORT_OPEN_RETURNED", settings.PortName + "; native ReadFile is already pending");
            return;
        }

        lock (_sync)
        {
            var port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
            {
                Handshake = Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };

            port.DataReceived += SerialPort_DataReceived;
            port.ErrorReceived += SerialPort_ErrorReceived;

            try
            {
                Interlocked.Exchange(ref _firstRxReported, 0);
                ReportDiagnostic("PORT_OPEN_BEGIN", $"{settings.PortName} {settings.BaudRate}/{settings.DataBits}/{settings.Parity}/{settings.StopBits} Handshake.None");
                port.Open();
                ReportDiagnostic("PORT_OPEN_RETURNED", settings.PortName);

                // Arrowvale applies the live line states after Create/Open.
                // Keep the port defaults LOW during Open, then apply the
                // requested values in the captured RTS-then-DTR order.
                port.RtsEnable = settings.RtsEnable;
                ReportDiagnostic("RTS_APPLIED", settings.RtsEnable ? "HIGH" : "LOW");
                port.DtrEnable = settings.DtrEnable;
                ReportDiagnostic("DTR_APPLIED", settings.DtrEnable ? "HIGH" : "LOW");
                _serialPort = port;
            }
            catch
            {
                port.DataReceived -= SerialPort_DataReceived;
                port.ErrorReceived -= SerialPort_ErrorReceived;
                port.Dispose();
                throw;
            }
        }

    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SerialPort? port;
        WindowsNativeLiveSerialPort? native;

        lock (_sync)
        {
            port = _serialPort;
            native = _nativeLivePort;
            _serialPort = null;
            _nativeLivePort = null;
        }

        if (native is not null)
            await native.DisposeAsync().ConfigureAwait(false);

        if (port is not null)
        {
            port.DataReceived -= SerialPort_DataReceived;
            port.ErrorReceived -= SerialPort_ErrorReceived;
            if (port.IsOpen)
            {
                long closeStarted = Stopwatch.GetTimestamp();
                port.Close();
                long closeReturned = Stopwatch.GetTimestamp();
                // Publish both markers after Close returns so diagnostic I/O
                // cannot delay the close invocation being measured.
                ReportDiagnosticAt(
                    closeStarted,
                    "SERIALPORT_CLOSE_INVOKED",
                    "captured immediately before SerialPort.Close(); diagnostic delivered after close returned");
                ReportDiagnosticAt(
                    closeReturned,
                    "SERIALPORT_CLOSE_RETURNED",
                    $"SerialPort.Close() elapsed {Stopwatch.GetElapsedTime(closeStarted).TotalMilliseconds:F4} ms");
            }
            long disposeStarted = Stopwatch.GetTimestamp();
            ReportDiagnostic("SERIALPORT_DISPOSE_BEGIN", "immediately before SerialPort.Dispose()");
            port.Dispose();
            ReportDiagnostic(
                "SERIALPORT_DISPOSE_RETURNED",
                $"SerialPort.Dispose() elapsed {Stopwatch.GetElapsedTime(disposeStarted).TotalMilliseconds:F4} ms");
        }

    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (data.IsEmpty)
            return;

        SerialPort? port;
        WindowsNativeLiveSerialPort? native;
        lock (_sync)
        {
            port = _serialPort is { IsOpen: true } ? _serialPort : null;
            native = _nativeLivePort is { IsOpen: true } ? _nativeLivePort : null;
        }

        byte[] exactTx = data.ToArray();
        long writeStarted = Stopwatch.GetTimestamp();
        if (native is not null)
            await native.SendAsync(exactTx, cancellationToken).ConfigureAwait(false);
        else if (port is not null)
        {
            await port.BaseStream.WriteAsync(exactTx, cancellationToken).ConfigureAwait(false);
            await port.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else
            throw new InvalidOperationException("OTMR serial port is not connected.");
        long writeReturned = Stopwatch.GetTimestamp();
        BytesTransmitted?.Invoke(this, new OtmrBytesTransmittedEventArgs(exactTx));
        // Publish the timestamps only after the write has returned. This keeps
        // diagnostic file I/O outside the measured native write operation.
        ReportDiagnosticAt(
            writeStarted,
            "SERIAL_WRITE_INVOKED",
            $"{exactTx.Length} byte(s); captured immediately before the transport write call");
        ReportDiagnosticAt(
            writeReturned,
            "SERIAL_WRITE_RETURNED",
            $"{exactTx.Length} byte(s); write/flush elapsed {Stopwatch.GetElapsedTime(writeStarted, writeReturned).TotalMilliseconds:F4} ms");
    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (sender is not SerialPort port || !port.IsOpen)
                return;

            int available = port.BytesToRead;
            if (available <= 0)
                return;

            byte[] buffer = new byte[available];
            int read = port.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                return;

            if (read != buffer.Length)
                Array.Resize(ref buffer, read);

            if (Interlocked.Exchange(ref _firstRxReported, 1) == 0)
                ReportDiagnostic("FIRST_RX_BYTE", $"0x{buffer[0]:X2}; first read returned {buffer.Length} byte(s)");

            BytesReceived?.Invoke(this, new OtmrBytesReceivedEventArgs(buffer));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new OtmrTransportErrorEventArgs(ex));
        }
    }

    private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e) =>
        ErrorOccurred?.Invoke(this, new OtmrTransportErrorEventArgs(
            new IOException($"Serial port reported {e.EventType}.")));

    private void ReportDiagnostic(string stage, string detail) =>
        DiagnosticOccurred?.Invoke(this, new OtmrTransportDiagnosticEventArgs(
            DateTimeOffset.Now,
            Stopwatch.GetTimestamp(),
            Stopwatch.Frequency,
            stage,
            detail));

    private void ReportDiagnosticAt(long stopwatchTimestamp, string stage, string detail)
    {
        long now = Stopwatch.GetTimestamp();
        DiagnosticOccurred?.Invoke(this, new OtmrTransportDiagnosticEventArgs(
            DateTimeOffset.Now - Stopwatch.GetElapsedTime(stopwatchTimestamp, now),
            stopwatchTimestamp,
            Stopwatch.Frequency,
            stage,
            detail));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Dispose must not throw during application shutdown.
        }
    }
}
