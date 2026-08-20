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
}
