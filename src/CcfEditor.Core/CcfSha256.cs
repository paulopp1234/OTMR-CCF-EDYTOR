using System.Security.Cryptography;

namespace CcfEditor.Core;

public static class CcfSha256
{
    public static string ComputeHex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static string ComputeFileHex(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
