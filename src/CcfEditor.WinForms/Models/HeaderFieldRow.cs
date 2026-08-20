namespace CcfEditor.WinForms.Models;

internal enum HeaderEditKind
{
    None,
    Ascii,
    Byte,
    UInt16
}

internal sealed class HeaderFieldRow
{
    public string Offset { get; init; } = string.Empty;
    public int OffsetValue { get; init; }
    public int Length { get; init; }
    public string Meaning { get; init; } = string.Empty;
    public string RawHex { get; set; } = string.Empty;
    public string Decoded { get; set; } = string.Empty;
    public HeaderEditKind EditKind { get; init; }
    public bool Editable => EditKind != HeaderEditKind.None;
}
