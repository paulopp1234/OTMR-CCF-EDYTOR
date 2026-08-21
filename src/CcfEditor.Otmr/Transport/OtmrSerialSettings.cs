using System.IO.Ports;

namespace CcfEditor.Otmr.Transport;

public sealed record OtmrSerialSettings(
    string PortName,
    int BaudRate,
    int DataBits,
    Parity Parity,
    StopBits StopBits,
    bool RtsEnable = false,
    bool DtrEnable = false)
{
    public static OtmrSerialSettings Class171Bench(string portName, bool dtrHigh = false) =>
        new(portName, 38400, 8, Parity.None, StopBits.One, RtsEnable: false, DtrEnable: dtrHigh);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName))
            throw new ArgumentException("A serial port must be selected.", nameof(PortName));
        if (BaudRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(BaudRate));
        if (DataBits is < 5 or > 8)
            throw new ArgumentOutOfRangeException(nameof(DataBits));
    }
}
