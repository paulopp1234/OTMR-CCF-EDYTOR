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
        Assert.False(settings.RtsEnable);
        Assert.False(settings.DtrEnable);
    }

    [Fact]
    public async Task ConnectOpensIdleWithRtsAndDtrLowAndSendsNothing()
    {
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport);

        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7", dtrHigh: true) with { RtsEnable = true });

        Assert.Equal(OtmrLiveState.ConnectedIdle, service.State);
        Assert.False(service.IsLiveActive);
        OtmrSerialSettings settings = Assert.Single(transport.ConnectionSettings);
        Assert.False(settings.RtsEnable);
        Assert.False(settings.DtrEnable);
        Assert.Empty(transport.Transmissions);
        Assert.Empty(service.GetCaptureSnapshot());

        transport.EmitRx(new byte[] { 0xFB, 0xFB, 0x12, 0x34, 0xFF });
        Assert.Equal(OtmrLiveState.ConnectedIdle, service.State);
        Assert.False(service.IsLiveActive);
    }

    [Fact]
    public async Task ExplicitStartUsesProvenQueryThenCandidateAndRequiresCompleteLiveFrame()
    {
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport, FastStartTiming());
        var states = new List<OtmrLiveState>();
        service.StateChanged += (_, args) => states.Add(args.State);
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync();
        Assert.Equal(OtmrLiveState.QuerySent, service.State);
        Assert.Equal(OtmrLiveStartProtocol.ProvenQueryFrame.ToArray(), Assert.Single(transport.Transmissions));

        byte[] reply = OtmrLiveStartProtocol.ExpectedReplyPrefix.ToArray();
        transport.EmitRx(reply[..5]);
        Assert.Equal(OtmrLiveState.QuerySent, service.State);
        transport.EmitRx(reply[5..]);
        await start;

        Assert.Equal(OtmrLiveState.WaitingForLiveFrames, service.State);
        Assert.False(service.IsLiveActive);
        Assert.Equal(2, transport.ConnectionSettings.Count);
        Assert.False(transport.ConnectionSettings[0].RtsEnable);
        Assert.False(transport.ConnectionSettings[0].DtrEnable);
        Assert.False(transport.ConnectionSettings[1].RtsEnable);
        Assert.True(transport.ConnectionSettings[1].DtrEnable);
        Assert.Equal(new[]
        {
            OtmrLiveStartProtocol.ProvenQueryFrame.ToArray(),
            OtmrLiveStartProtocol.CandidateLiveStartFrame.ToArray()
        }, transport.Transmissions, ByteArrayComparer.Instance);
        Assert.Equal(new[] { "CONNECT:DTR=LOW", "TX:01-01", "TX:01-07", "DISCONNECT", "CONNECT:DTR=HIGH" },
            transport.Operations);

        transport.EmitRx(new byte[] { 0xFB, 0xFB, 0x12, 0x34 });
        Assert.Equal(OtmrLiveState.WaitingForLiveFrames, service.State);
        transport.EmitRx(new byte[] { 0x56, 0xFF });
        Assert.Equal(OtmrLiveState.LiveActive, service.State);
        Assert.True(service.IsLiveActive);
        Assert.Contains(OtmrLiveState.OtmrReplied, states);
        Assert.Contains(OtmrLiveState.StartingLive, states);
        Assert.Contains(OtmrLiveState.WaitingForLiveFrames, states);
        Assert.Contains(OtmrLiveState.LiveActive, states);

        OtmrCaptureEntry[] tx = service.GetCaptureSnapshot().Where(entry => entry.Direction == OtmrDirection.Tx).ToArray();
        Assert.Equal(2, tx.Length);
        Assert.Equal("Proven OTMR interrogation/query frame", tx[0].Interpretation);
        Assert.Contains("CANDIDATE", tx[1].Interpretation, StringComparison.Ordinal);

        await service.DisconnectAsync();
        Assert.Equal(OtmrLiveState.Disconnected, service.State);
        Assert.False(service.IsLiveActive);
    }

    [Fact]
    public async Task MissingProvenReplyNeverSendsCandidateOrReopensPort()
    {
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(
            transport,
            FastStartTiming() with { QueryReplyTimeout = TimeSpan.FromMilliseconds(50) });
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() => service.StartLiveAsync());

        Assert.Contains("candidate live-start frame was not sent", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OtmrLiveState.Error, service.State);
        Assert.Single(transport.Transmissions);
        Assert.Single(transport.ConnectionSettings);
        Assert.DoesNotContain(transport.Operations, operation => operation == "TX:01-07");
    }

    [Fact]
    public async Task ImmediateCompleteFrameDuringDtrHighReopenActivatesLiveState()
    {
        using var transport = new FakeTransport { EmitCompleteFrameOnDtrHighConnect = true };
        using var service = new OtmrLiveService(transport, FastStartTiming());
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync();
        transport.EmitRx(OtmrLiveStartProtocol.ExpectedReplyPrefix.ToArray());
        await start;

        Assert.Equal(OtmrLiveState.LiveActive, service.State);
        Assert.True(service.IsLiveActive);
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
    public void FragmentedTransportReceive_EmitsOneCompleteLiveFrame()
    {
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport);
        var received = new List<OtmrLiveFrame>();
        service.FrameReceived += (_, args) => received.Add(args.Frame);

        transport.EmitRx(new byte[] { 0xFB, 0xFB, 0x38, 0x4B });
        transport.EmitRx(new byte[] { 0x38, 0x4A });
        transport.EmitRx(new byte[] { 0xFF });

        OtmrLiveFrame frame = Assert.Single(received);
        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4B, 0x38, 0x4A, 0xFF }, frame.GetDataSnapshot());
        Assert.Equal(3, service.GetCaptureSnapshot().Count);
    }

    [Fact]
    public void TransportTxEvent_IsCapturedByteForByte()
    {
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport);

        transport.EmitTx(new byte[] { 0xFC, 0xAB, 0x01 });

        OtmrCaptureEntry entry = Assert.Single(service.GetCaptureSnapshot());
        Assert.Equal(OtmrDirection.Tx, entry.Direction);
        Assert.Equal(new byte[] { 0xFC, 0xAB, 0x01 }, entry.GetDataSnapshot());
        Assert.Null(entry.Interpretation);
    }

    [Fact]
    public async Task JsonLinesCaptureWriter_PreservesTimestampDirectionAndRawHex()
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

    [Fact]
    public async Task TextCaptureWriter_IsHumanReadableAndPreservesRawHex()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"otmr_{Guid.NewGuid():N}.txt");
        var entries = new[]
        {
            new OtmrCaptureEntry(
                new DateTimeOffset(2026, 8, 20, 20, 11, 42, TimeSpan.Zero).AddMilliseconds(134),
                OtmrDirection.Tx,
                new byte[] { 0xFC, 0xAB }),
            new OtmrCaptureEntry(
                new DateTimeOffset(2026, 8, 20, 20, 11, 42, TimeSpan.Zero).AddMilliseconds(159),
                OtmrDirection.Rx,
                new byte[] { 0x01, 0x01, 0x00 })
        };

        try
        {
            await OtmrCaptureWriter.WriteTextAsync(temp, entries);
            string text = await File.ReadAllTextAsync(temp);

            Assert.Contains("OTMR RAW CAPTURE", text);
            Assert.Contains("TX", text);
            Assert.Contains("FC AB", text);
            Assert.Contains("RX", text);
            Assert.Contains("01 01 00", text);
            Assert.Contains("2026-08-20T20:11:42.134", text);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private sealed class FakeTransport : IOtmrTransport
    {
        public List<OtmrSerialSettings> ConnectionSettings { get; } = new();
        public List<byte[]> Transmissions { get; } = new();
        public List<string> Operations { get; } = new();
        public bool EmitCompleteFrameOnDtrHighConnect { get; init; }
        public bool IsConnected { get; private set; }
        public event EventHandler<OtmrBytesReceivedEventArgs>? BytesReceived;
        public event EventHandler<OtmrBytesTransmittedEventArgs>? BytesTransmitted;
        public event EventHandler<OtmrTransportErrorEventArgs>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(OtmrSerialSettings settings, CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            ConnectionSettings.Add(settings);
            Operations.Add($"CONNECT:DTR={(settings.DtrEnable ? "HIGH" : "LOW")}");
            if (settings.DtrEnable && EmitCompleteFrameOnDtrHighConnect)
                EmitRx(new byte[] { 0xFB, 0xFB, 0x42, 0xFF });
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            Operations.Add("DISCONNECT");
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            byte[] bytes = data.ToArray();
            Transmissions.Add(bytes);
            Operations.Add(bytes.AsSpan().SequenceEqual(OtmrLiveStartProtocol.ProvenQueryFrame.Span)
                ? "TX:01-01"
                : bytes.AsSpan().SequenceEqual(OtmrLiveStartProtocol.CandidateLiveStartFrame.Span)
                    ? "TX:01-07"
                    : "TX:OTHER");
            BytesTransmitted?.Invoke(this, new OtmrBytesTransmittedEventArgs(bytes));
            return Task.CompletedTask;
        }

        public void EmitRx(byte[] bytes) =>
            BytesReceived?.Invoke(this, new OtmrBytesReceivedEventArgs(bytes));

        public void EmitTx(byte[] bytes) =>
            BytesTransmitted?.Invoke(this, new OtmrBytesTransmittedEventArgs(bytes));

        public void Dispose()
        {
        }
    }

    private static OtmrLiveStartTiming FastStartTiming() => new(
        TimeSpan.FromSeconds(1),
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero);

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();
        public bool Equals(byte[]? x, byte[]? y) => x is not null && y is not null && x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) => obj.Aggregate(17, (hash, value) => hash * 31 + value);
    }
}
