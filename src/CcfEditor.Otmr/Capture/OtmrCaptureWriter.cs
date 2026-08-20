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

        string fullPath = PrepareOutputPath(path);

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

    public static async Task WriteTextAsync(
        string path,
        IEnumerable<OtmrCaptureEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entries);

        string fullPath = PrepareOutputPath(path);

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(stream);

        await writer.WriteLineAsync("OTMR RAW CAPTURE");
        await writer.WriteLineAsync("Timestamp                          Dir  Raw bytes  Interpretation");
        await writer.WriteLineAsync("--------------------------------------------------------------------------------");

        foreach (OtmrCaptureEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string direction = entry.Direction.ToString().ToUpperInvariant();
            string interpretation = string.IsNullOrWhiteSpace(entry.Interpretation)
                ? string.Empty
                : $"  {entry.Interpretation}";
            string line = $"{entry.Timestamp:O}  {direction,-3}  {entry.Hex}{interpretation}";
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        }
    }

    private static string PrepareOutputPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        return fullPath;
    }

    private sealed record CaptureLine(
        DateTimeOffset Timestamp,
        string Direction,
        string Hex,
        string? Interpretation);
}
