namespace CcfEditor.WinForms.Models;

internal sealed class RecordGridRow
{
    public int Record { get; init; }
    public ushort EventIndex { get; init; }
    public byte Type { get; init; }
    public byte Flag { get; init; }
    public string Name { get; init; } = string.Empty;
    public byte Card { get; init; }
    public byte Channel { get; init; }
    public byte LoggerMode { get; init; }
    public byte HardwareFunction { get; init; }
    public string Pair { get; init; } = string.Empty;
    public string OffText { get; init; } = string.Empty;
    public string OnText { get; init; } = string.Empty;
    public string RawOffset { get; init; } = string.Empty;
}
