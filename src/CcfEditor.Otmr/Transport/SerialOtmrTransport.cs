using System.IO.Ports;

namespace CcfEditor.Otmr.Transport;

public sealed class SerialOtmrTransport : IOtmrTransport
{
    private readonly object _sync = new();
    private SerialPort? _serialPort;
    private bool _disposed;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
                return _serialPort?.IsOpen == true;
        }
    }

    public event EventHandler<OtmrBytesReceivedEventArgs>? BytesReceived;
    public event EventHandler<OtmrBytesTransmittedEventArgs>? BytesTransmitted;
    public event EventHandler<OtmrTransportErrorEventArgs>? ErrorOccurred;

    public static string[] GetAvailablePorts() =>
        SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public Task ConnectAsync(OtmrSerialSettings settings, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_serialPort?.IsOpen == true)
                throw new InvalidOperationException("OTMR serial port is already connected.");

            var port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
            {
                Handshake = Handshake.None,
                DtrEnable = settings.DtrEnable,
                RtsEnable = settings.RtsEnable,
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };

            port.DataReceived += SerialPort_DataReceived;
            port.ErrorReceived += SerialPort_ErrorReceived;

            try
            {
                port.Open();
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

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SerialPort? port;

        lock (_sync)
        {
            port = _serialPort;
            _serialPort = null;
        }

        if (port is not null)
        {
            port.DataReceived -= SerialPort_DataReceived;
            port.ErrorReceived -= SerialPort_ErrorReceived;
            if (port.IsOpen)
                port.Close();
            port.Dispose();
        }

        return Task.CompletedTask;
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (data.IsEmpty)
            return;

        SerialPort port;
        lock (_sync)
        {
            port = _serialPort is { IsOpen: true }
                ? _serialPort
                : throw new InvalidOperationException("OTMR serial port is not connected.");
        }

        byte[] exactTx = data.ToArray();
        await port.BaseStream.WriteAsync(exactTx, cancellationToken).ConfigureAwait(false);
        await port.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        BytesTransmitted?.Invoke(this, new OtmrBytesTransmittedEventArgs(exactTx));
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
