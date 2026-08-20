namespace CcfEditor.Core;

public static class CcfFileService
{
    public static SaveVerification SaveAs(CcfDocument document, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var fullOutputPath = Path.GetFullPath(outputPath);
        if (document.SourcePath is not null && PathsEqual(document.SourcePath, fullOutputPath))
            throw new InvalidOperationException("Milestone 1 uses Save As only. Refusing to overwrite the source CCF.");

        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using (var stream = new FileStream(fullOutputPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(document.WorkingBytesSpan);
            stream.Flush(flushToDisk: true);
        }

        var outputSha = CcfSha256.ComputeFileHex(fullOutputPath);
        var outputLength = new FileInfo(fullOutputPath).Length;
        var byteIdentical = outputLength == document.Length &&
                            File.ReadAllBytes(fullOutputPath).AsSpan().SequenceEqual(document.WorkingBytesSpan);

        return new SaveVerification(
            fullOutputPath,
            outputLength,
            document.OriginalSha256,
            document.WorkingSha256,
            outputSha,
            byteIdentical);
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}

public sealed record SaveVerification(
    string OutputPath,
    long OutputLength,
    string OriginalSha256,
    string WorkingSha256,
    string OutputSha256,
    bool OutputMatchesWorkingBytes)
{
    public bool NoEditShaMatchesOriginal => OriginalSha256 == WorkingSha256 && WorkingSha256 == OutputSha256;
}
