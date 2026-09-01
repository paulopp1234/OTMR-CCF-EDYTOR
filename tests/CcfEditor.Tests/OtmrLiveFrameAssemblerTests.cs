using System.Text.Json;
using CcfEditor.Otmr.Live;

namespace CcfEditor.Tests;

public sealed class OtmrLiveFrameAssemblerTests
{
    private static readonly byte[] FrameA = { 0xFB, 0xFB, 0x38, 0x4B, 0x38, 0x4A, 0xFF };

    [Fact]
    public void CompleteFrameInOneCallback_ProducesOneExactFrame()
    {
        var assembler = new OtmrLiveFrameAssembler();

        OtmrLiveFrame frame = Assert.Single(assembler.Append(FrameA));

        Assert.Equal(FrameA, frame.GetDataSnapshot());
        Assert.Equal(0, assembler.BufferedByteCount);
    }

    [Fact]
    public void FragmentedFrameAcrossCallbacks_ProducesTheSameExactFrame()
    {
        var assembler = new OtmrLiveFrameAssembler();

        Assert.Empty(assembler.Append(new byte[] { 0xFB, 0xFB, 0x38, 0x4B }));
        Assert.Empty(assembler.Append(new byte[] { 0x38, 0x4A }));
        OtmrLiveFrame frame = Assert.Single(assembler.Append(new byte[] { 0xFF }));

        Assert.Equal(FrameA, frame.GetDataSnapshot());
    }

    [Fact]
    public void GarbageBeforeHeader_IsDiscardedAndValidFrameIsProduced()
    {
        var assembler = new OtmrLiveFrameAssembler();

        OtmrLiveFrame frame = Assert.Single(assembler.Append(
            new byte[] { 0x01, 0x02, 0xAA, 0xFB, 0xFB, 0x38, 0x4A, 0xFF }));

        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }, frame.GetDataSnapshot());
    }

    [Fact]
    public void TwoFramesInOneCallback_ProducesTwoFramesInOrder()
    {
        var assembler = new OtmrLiveFrameAssembler();

        IReadOnlyList<OtmrLiveFrame> frames = assembler.Append(
            new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF, 0xFB, 0xFB, 0x38, 0x4B, 0xFF });

        Assert.Equal(2, frames.Count);
        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }, frames[0].GetDataSnapshot());
        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4B, 0xFF }, frames[1].GetDataSnapshot());
    }

    [Fact]
    public void HeaderSplitAcrossCallbacks_IsRecognised()
    {
        var assembler = new OtmrLiveFrameAssembler();

        Assert.Empty(assembler.Append(new byte[] { 0xFB }));
        OtmrLiveFrame frame = Assert.Single(assembler.Append(new byte[] { 0xFB, 0x38, 0x4A, 0xFF }));

        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }, frame.GetDataSnapshot());
    }

    [Fact]
    public void ArrowvaleLeadingFfThenSplitFbFbHeader_PreservesAndAssemblesTheFirstFrame()
    {
        var assembler = new OtmrLiveFrameAssembler();

        Assert.Empty(assembler.Append(new byte[] { 0xFF, 0xFB }));
        Assert.Equal(1, assembler.BufferedByteCount);
        OtmrLiveFrame frame = Assert.Single(assembler.Append(
            new byte[] { 0xFB, 0x38, 0x89, 0x38, 0x88, 0xFF }));

        Assert.Equal(
            new byte[] { 0xFB, 0xFB, 0x38, 0x89, 0x38, 0x88, 0xFF },
            frame.GetDataSnapshot());
        Assert.Equal(0, assembler.BufferedByteCount);
    }

    [Fact]
    public void D2StatusBytesBetweenFrames_AreDiscardedWithoutLosingEitherFrame()
    {
        var assembler = new OtmrLiveFrameAssembler();

        IReadOnlyList<OtmrLiveFrame> frames = assembler.Append(new byte[]
        {
            0xFB, 0xFB, 0x38, 0x89, 0xFF,
            0xD2, 0x0C,
            0xFB, 0xFB, 0x38, 0x88, 0xFF
        });

        Assert.Equal(2, frames.Count);
        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x89, 0xFF }, frames[0].GetDataSnapshot());
        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x88, 0xFF }, frames[1].GetDataSnapshot());
    }

    [Fact]
    public void PartialFrame_RemainsBufferedUntilTerminatorArrives()
    {
        var assembler = new OtmrLiveFrameAssembler();

        Assert.Empty(assembler.Append(new byte[] { 0xFB, 0xFB, 0x38, 0x4B }));
        Assert.Equal(4, assembler.BufferedByteCount);
        Assert.Empty(assembler.Append(new byte[] { 0x38, 0x4A }));
        Assert.Equal(6, assembler.BufferedByteCount);

        Assert.Single(assembler.Append(new byte[] { 0xFF }));
        Assert.Equal(0, assembler.BufferedByteCount);
    }

    [Fact]
    public void AppendAnalysisCorrelatesSplitFrameTerminatorAndTrailingOutsideBytes()
    {
        var assembler = new OtmrLiveFrameAssembler();

        OtmrLiveFrameAppendAnalysis partial = assembler.AppendWithAnalysis(
            new byte[] { 0xFB, 0xFB, 0x0C });
        OtmrLiveFrameAppendAnalysis completing = assembler.AppendWithAnalysis(
            new byte[] { 0xFF, 0xD2, 0x07 });

        Assert.True(partial.HasPartialLiveFrame);
        Assert.Empty(partial.CompletedFrames);
        Assert.Empty(partial.OutsideSegments);
        OtmrLiveFrameCompletion completion = Assert.Single(completing.CompletedFrames);
        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x0C, 0xFF }, completion.Frame.GetDataSnapshot());
        Assert.Equal(0, completion.TerminatorChunkOffset);
        OtmrRxOutsideSegment trailing = Assert.Single(completing.OutsideSegments);
        Assert.Equal(1, trailing.StartOffset);
        Assert.Equal(new byte[] { 0xD2, 0x07 }, trailing.Data.ToArray());
        Assert.False(completing.HasPartialLiveFrame);
    }

    [Fact]
    public void AppendAnalysisReportsPureOutsideBytesAndMalformedResynchronisation()
    {
        var assembler = new OtmrLiveFrameAssembler();

        OtmrLiveFrameAppendAnalysis outside = assembler.AppendWithAnalysis(new byte[] { 0xD2, 0x28 });
        OtmrLiveFrameAppendAnalysis malformed = assembler.AppendWithAnalysis(
            new byte[] { 0xFB, 0xFB, 0x01, 0xFB, 0xFB, 0x0C, 0xFF });

        Assert.Equal(new byte[] { 0xD2, 0x28 }, Assert.Single(outside.OutsideSegments).Data.ToArray());
        Assert.Empty(outside.CompletedFrames);
        Assert.Equal(1, malformed.MalformedCandidateCount);
        Assert.Equal(
            new byte[] { 0xFB, 0xFB, 0x0C, 0xFF },
            Assert.Single(malformed.CompletedFrames).Frame.GetDataSnapshot());
    }

    [Fact]
    public void NewHeaderInsideIncompleteFrame_ResynchronisesCleanly()
    {
        var assembler = new OtmrLiveFrameAssembler();

        OtmrLiveFrame frame = Assert.Single(assembler.Append(
            new byte[] { 0xFB, 0xFB, 0x10, 0x20, 0xFB, 0xFB, 0x38, 0x4A, 0xFF }));

        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }, frame.GetDataSnapshot());
    }

    [Fact]
    public void RecordedCaptureRxCallbacks_ProduceCompleteObservedFrames()
    {
        string fixture = FindFixture("OTMR_CAPTURE_20260821_085017.jsonl");
        var assembler = new OtmrLiveFrameAssembler();
        var frames = new List<OtmrLiveFrame>();
        int rxCallbacks = 0;

        foreach (string line in File.ReadLines(fixture).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            using JsonDocument json = JsonDocument.Parse(line);
            if (!string.Equals(json.RootElement.GetProperty("Direction").GetString(), "RX", StringComparison.OrdinalIgnoreCase))
                continue;

            rxCallbacks++;
            byte[] chunk = ParseHex(json.RootElement.GetProperty("Hex").GetString() ?? string.Empty);
            frames.AddRange(assembler.Append(chunk));
        }

        Assert.Equal(134, rxCallbacks);
        Assert.Equal(24, frames.Count);
        Assert.Equal(0, assembler.BufferedByteCount);
        Assert.All(frames, frame =>
        {
            byte[] data = frame.GetDataSnapshot();
            Assert.True(data.Length >= 3);
            Assert.Equal(0xFB, data[0]);
            Assert.Equal(0xFB, data[1]);
            Assert.Equal(0xFF, data[^1]);
        });
        Assert.Contains(frames, frame => frame.GetDataSnapshot().Contains((byte)0x38));
    }

    private static byte[] ParseHex(string hex) => hex
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => Convert.ToByte(value, 16))
        .ToArray();

    private static string FindFixture(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "TestData", fileName);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new Xunit.Sdk.XunitException($"Replay fixture '{fileName}' was not found.");
    }
}
