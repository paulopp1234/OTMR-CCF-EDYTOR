using System.Text.Json;

namespace CcfEditor.Otmr.Bench;

public static class OtmrBenchObservationSessionWriter
{
    public const string FormatName = "OTMR_BENCH_RAW_OBSERVATION_V1";

    public static async Task WriteJsonLinesAsync(
        string path,
        OtmrBenchObservationSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsStopped)
            throw new InvalidOperationException("Stop the observation session before saving it.");

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            useAsync: true);
        await using var writer = new StreamWriter(stream);

        string sessionLine = JsonSerializer.Serialize(new
        {
            EntryType = "Session",
            Format = FormatName,
            session.SessionId,
            session.Connector,
            session.Pin,
            session.ExpectedFunction,
            session.ExpectedRecordA,
            session.ExpectedRecordB,
            session.ArmTimestamp,
            session.StopTimestamp,
            session.FrameCount,
            SemanticStatus = OtmrBenchObservationSession.RawOnlySemanticStatus
        });
        await writer.WriteLineAsync(sessionLine.AsMemory(), cancellationToken);

        foreach (OtmrBenchObservationFrame frame in session.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string frameLine = JsonSerializer.Serialize(new
            {
                EntryType = "Frame",
                session.SessionId,
                frame.SequenceNumber,
                frame.Timestamp,
                frame.RawFrameHex,
                RawFrameBytes = frame.GetRawFrameBytesSnapshot().Select(value => (int)value).ToArray(),
                frame.CandidateRawDelta,
                SemanticStatus = OtmrBenchObservationSession.RawOnlySemanticStatus
            });
            await writer.WriteLineAsync(frameLine.AsMemory(), cancellationToken);
        }
    }
}
