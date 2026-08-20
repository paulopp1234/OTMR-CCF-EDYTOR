using System.Text.Json;

namespace CcfEditor.Otmr.Capture;

public static class OtmrCaptureWriter
{
    public static async Task WriteJsonLinesAsync(
        string path,
        IEnumerable<OtmrCaptureEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entries);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(stream);

        foreach (OtmrCaptureEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string json = JsonSerializer.Serialize(new CaptureLine(
                entry.Timestamp,
                entry.Direction.ToString().ToUpperInvariant(),
                entry.Hex,
                entry.Interpretation));
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        }
    }

    private sealed record CaptureLine(
        DateTimeOffset Timestamp,
        string Direction,
        string Hex,
        string? Interpretation);
}
