namespace CcfEditor.Core;

public static class CcfParser
{
    public static CcfDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        return Parse(bytes, fullPath);
    }

    public static CcfDocument Parse(byte[] bytes, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length != CcfConstants.FileSize)
        {
            throw new InvalidDataException(
                $"Unsupported CCF size {bytes.Length:N0} bytes. Milestone 1 requires exactly {CcfConstants.FileSize:N0} bytes.");
        }

        return new CcfDocument(bytes, sourcePath is null ? null : Path.GetFullPath(sourcePath));
    }
}
