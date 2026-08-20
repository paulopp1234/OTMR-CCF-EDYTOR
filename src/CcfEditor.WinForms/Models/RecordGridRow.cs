namespace CcfEditor.WinForms.Models;

internal sealed class RecordGridRow
{
    public int Record { get; init; }
    public ushort EventIndex { get; init; }
    public byte Type { get; set; }
    public byte Flag { get; init; }
    public string Name { get; set; } = string.Empty;
    public string ColourHex { get; set; } = string.Empty;
    public byte Card { get; set; }
    public byte Channel { get; set; }
    public byte LoggerMode { get; set; }
    public byte HardwareFunction { get; set; }
    public string Pair { get; set; } = string.Empty;
    public string OffText { get; set; } = string.Empty;
    public string OnText { get; set; } = string.Empty;
    public string RawOffset { get; init; } = string.Empty;
}
