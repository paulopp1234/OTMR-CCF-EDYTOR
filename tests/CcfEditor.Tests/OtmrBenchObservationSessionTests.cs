using System.Text.Json;
using CcfEditor.Otmr.Bench;
using CcfEditor.Otmr.Live;

namespace CcfEditor.Tests;

public sealed class OtmrBenchObservationSessionTests
{
    [Fact]
    public void SessionRetainsPinMetadataExactFramesSequenceTimestampsAndCandidateDeltas()
    {
        var armTimestamp = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
        var session = new OtmrBenchObservationSession("J1", "A", "Throttle 1", 0, 12, armTimestamp);
        OtmrLiveFrame first = Assemble(0xFB, 0xFB, 0x38, 0x4A, 0xFF);
        OtmrLiveFrame second = Assemble(0xFB, 0xFB, 0x38, 0x4B, 0xFF);

        session.AddFrame(armTimestamp.AddSeconds(1), first);
        session.AddFrame(armTimestamp.AddSeconds(2), second);
        session.Stop(armTimestamp.AddSeconds(3));

        Assert.Equal("J1", session.Connector);
        Assert.Equal("A", session.Pin);
        Assert.Equal("Throttle 1", session.ExpectedFunction);
        Assert.Equal(0, session.ExpectedRecordA);
        Assert.Equal(12, session.ExpectedRecordB);
        Assert.Equal(armTimestamp, session.ArmTimestamp);
        Assert.Equal(armTimestamp.AddSeconds(3), session.StopTimestamp);
        Assert.Equal(2, session.FrameCount);

        Assert.Equal(1, session.Frames[0].SequenceNumber);
        Assert.Equal(armTimestamp.AddSeconds(1), session.Frames[0].Timestamp);
        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }, session.Frames[0].GetRawFrameBytesSnapshot());
        Assert.Equal(
            "CANDIDATE RAW DELTA: BASELINE (no previous complete frame)",
            session.Frames[0].CandidateRawDelta);

        Assert.Equal(2, session.Frames[1].SequenceNumber);
        Assert.Equal(armTimestamp.AddSeconds(2), session.Frames[1].Timestamp);
        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4B, 0xFF }, session.Frames[1].GetRawFrameBytesSnapshot());
        Assert.Equal("CANDIDATE RAW DELTA: @03:4A→4B", session.Frames[1].CandidateRawDelta);
    }

    [Fact]
    public async Task JsonLinesWriterEmitsSessionHeaderAndRawOnlyFrameLines()
    {
        string output = Path.Combine(Path.GetTempPath(), $"otmr_observation_{Guid.NewGuid():N}.jsonl");
        var armTimestamp = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
        var session = new OtmrBenchObservationSession("J1", "A", "Throttle 1", 0, 12, armTimestamp);
        session.AddFrame(armTimestamp.AddMilliseconds(100), Assemble(0xFB, 0xFB, 0x38, 0x4A, 0xFF));
        session.AddFrame(armTimestamp.AddMilliseconds(200), Assemble(0xFB, 0xFB, 0x38, 0x4B, 0xFF));
        session.Stop(armTimestamp.AddSeconds(1));

        try
        {
            await OtmrBenchObservationSessionWriter.WriteJsonLinesAsync(output, session);
            string[] lines = await File.ReadAllLinesAsync(output);
            Assert.Equal(3, lines.Length);

            using JsonDocument header = JsonDocument.Parse(lines[0]);
            JsonElement headerRoot = header.RootElement;
            Assert.Equal("Session", headerRoot.GetProperty("EntryType").GetString());
            Assert.Equal(OtmrBenchObservationSessionWriter.FormatName, headerRoot.GetProperty("Format").GetString());
            Assert.Equal("J1", headerRoot.GetProperty("Connector").GetString());
            Assert.Equal("A", headerRoot.GetProperty("Pin").GetString());
            Assert.Equal("Throttle 1", headerRoot.GetProperty("ExpectedFunction").GetString());
            Assert.Equal(0, headerRoot.GetProperty("ExpectedRecordA").GetInt32());
            Assert.Equal(12, headerRoot.GetProperty("ExpectedRecordB").GetInt32());
            Assert.Equal(2, headerRoot.GetProperty("FrameCount").GetInt32());

            using JsonDocument frameLine = JsonDocument.Parse(lines[2]);
            JsonElement frameRoot = frameLine.RootElement;
            Assert.Equal("Frame", frameRoot.GetProperty("EntryType").GetString());
            Assert.Equal(2, frameRoot.GetProperty("SequenceNumber").GetInt32());
            Assert.Equal("FB FB 38 4B FF", frameRoot.GetProperty("RawFrameHex").GetString());
            Assert.Equal(
                new[] { 251, 251, 56, 75, 255 },
                frameRoot.GetProperty("RawFrameBytes").EnumerateArray().Select(value => value.GetInt32()));
            Assert.Equal("CANDIDATE RAW DELTA: @03:4A→4B", frameRoot.GetProperty("CandidateRawDelta").GetString());
            Assert.Equal(OtmrBenchObservationSession.RawOnlySemanticStatus, frameRoot.GetProperty("SemanticStatus").GetString());
            Assert.False(frameRoot.TryGetProperty("RecordIndex", out _));
            Assert.False(frameRoot.TryGetProperty("State", out _));
            Assert.False(frameRoot.TryGetProperty("Card", out _));
            Assert.False(frameRoot.TryGetProperty("Channel", out _));
            Assert.False(frameRoot.TryGetProperty("Result", out _));
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public async Task JsonLinesWriterRejectsAnActiveSession()
    {
        string output = Path.Combine(Path.GetTempPath(), $"otmr_active_{Guid.NewGuid():N}.jsonl");
        var session = new OtmrBenchObservationSession(
            "J1",
            "A",
            "Throttle 1",
            0,
            12,
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OtmrBenchObservationSessionWriter.WriteJsonLinesAsync(output, session));
        Assert.False(File.Exists(output));
    }

    private static OtmrLiveFrame Assemble(params byte[] bytes) =>
        Assert.Single(new OtmrLiveFrameAssembler().Append(bytes));
}
