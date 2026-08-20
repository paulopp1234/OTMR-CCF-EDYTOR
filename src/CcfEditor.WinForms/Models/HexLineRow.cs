namespace CcfEditor.WinForms.Models;

internal sealed class HexLineRow
{
    public int OffsetValue { get; init; }
    public string Offset { get; init; } = string.Empty;
    public string Hex { get; init; } = string.Empty;
    public string Ascii { get; init; } = string.Empty;
}
