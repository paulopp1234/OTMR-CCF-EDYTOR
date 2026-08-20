using System.Text;

namespace CcfEditor.Core;

internal static class CcfText
{
    public static string ReadFixedAscii(byte[] buffer, int offset, int length)
    {
        var span = buffer.AsSpan(offset, length);
        var nulIndex = span.IndexOf((byte)0);
        if (nulIndex >= 0)
            span = span[..nulIndex];

        return Encoding.ASCII.GetString(span);
    }

    public static void WriteFixedAscii(byte[] buffer, int offset, int length, string value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(value);

        if (length < 1)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (value.Any(ch => ch > 0x7F || ch == '\0'))
            throw new ArgumentException("Only ASCII text without NUL characters is supported.", nameof(value));

        byte[] encoded = Encoding.ASCII.GetBytes(value);
        if (encoded.Length > length - 1)
            throw new ArgumentException($"Text is too long. Maximum is {length - 1} ASCII characters.", nameof(value));

        Span<byte> field = buffer.AsSpan(offset, length);
        field.Clear();
        encoded.CopyTo(field);
    }
}
