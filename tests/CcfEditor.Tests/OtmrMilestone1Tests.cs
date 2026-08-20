using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Transport;

namespace CcfEditor.Tests;

public sealed class OtmrMilestone1Tests
{
    [Fact]
    public void ProvenBenchSettings_Are38400_8N1()
    {
        OtmrSerialSettings settings = OtmrSerialSettings.Class171Bench("COM7");

        Assert.Equal("COM7", settings.PortName);
        Assert.Equal(38400, settings.BaudRate);
        Assert.Equal(8, settings.DataBits);
        Assert.Equal(System.IO.Ports.Parity.None, settings.Parity);
        Assert.Equal(System.IO.Ports.StopBits.One, settings.StopBits);
    }

    [Fact]
    public void RawReceiveChunks_ArePreservedExactlyWithoutProtocolGuessing()
    {
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport);

        transport.EmitRx(new byte[] { 0x01, 0x01 });
        transport.EmitRx(new byte[] { 0x00, 0x01, 0xFC, 0xAB });

        IReadOnlyList<OtmrCaptureEntry> entries = service.GetCaptureSnapshot();
        Assert.Equal(2, entries.Count);
        Assert.Equal(new byte[] { 0x01, 0x01 }, entries[0].GetDataSnapshot());
        Assert.Equal(new byte[] { 0x00, 0x01, 0xFC, 0xAB }, entries[1].GetDataSnapshot());
        Assert.Equal(OtmrDirection.Rx, entries[0].Direction);
        Assert.Equal(OtmrDirection.Rx, entries[1].Direction);
        Assert.Null(entries[0].Interpretation);
        Assert.Null(entries[1].Interpretation);
    }

    [Fact]
    public async Task CaptureWriter_PreservesTimestampDirectionAndRawHex()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"otmr_{Guid.NewGuid():N}.jsonl");
        var entry = new OtmrCaptureEntry(
            new DateTimeOffset(2026, 8, 20, 20, 11, 42, TimeSpan.Zero).AddMilliseconds(134),
            OtmrDirection.Rx,
            new byte[] { 0x01, 0x01, 0x00, 0xFC, 0xAB });

        try
        {
            await OtmrCaptureWriter.WriteJsonLinesAsync(temp, new[] { entry });
            string text = await File.ReadAllTextAsync(temp);

            Assert.Contains("RX", text);
            Assert.Contains("01 01 00 FC AB", text);
            Assert.Contains("2026-08-20", text);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private sealed class FakeTransport : IOtmrTransport
    {
        public bool IsConnected { get; private set; }
        public event EventHandler<OtmrBytesReceivedEventArgs>? BytesReceived;
        public event EventHandler<OtmrTransportErrorEventArgs>? ErrorOccurred;

        public Task ConnectAsync(OtmrSerialSettings settings, CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Milestone 1 test transport does not send protocol commands.");

        public void EmitRx(byte[] bytes) =>
            BytesReceived?.Invoke(this, new OtmrBytesReceivedEventArgs(bytes));

        public void Dispose()
        {
        }
    }
}
