using System.Text.RegularExpressions;
using CcfEditor.Core;
using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Storage;
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
    public void EveryProductionTxConstantMatchesAnAuthoritativeAnalyserWriteByteForByte()
    {
        IReadOnlyList<byte[]> hhdWrites = LoadHhdWrites();

        Assert.Equal(
            hhdWrites.Take(11),
            OtmrLiveStartProtocol.SafeInterrogationWrites.Select(value => value.ToArray()),
            ByteArrayComparer.Instance);
        Assert.Equal(hhdWrites[^1], OtmrLiveStartProtocol.FinalLiveStart07.ToArray());
    }

    [Fact]
    public void CapturedCheckByte_IsPayloadSumModulo256ForEveryHhdTxAndSoftwareRxFrame()
    {
        foreach (byte[] write in LoadHhdWrites())
            Assert.True(OtmrProtocolDerivation.HasValidPayloadCheckByte(write), BitConverter.ToString(write));

        foreach (byte[] reply in LoadCapturedReplies().Values)
            Assert.True(OtmrProtocolDerivation.HasValidPayloadCheckByte(reply), BitConverter.ToString(reply));
    }

    [Fact]
    public void ReadDataAcknowledgementGenerator_ReproducesAllSixCapturedWritesIncluding000301()
    {
        IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
        IReadOnlyList<byte[]> writes = LoadHhdWrites();
        (byte Transaction, int WriteIndex)[] mappings =
        {
            (0x02, 1), (0x04, 3), (0x06, 5),
            (0x08, 7), (0x0A, 9), (0x0C, 11)
        };

        foreach ((byte transaction, int writeIndex) in mappings)
        {
            Assert.Equal(
                writes[writeIndex],
                OtmrProtocolDerivation.CreateReadDataAcknowledgement(replies[transaction]));
        }
    }

    [Fact]
    public void UnmodifiedConfigurationPage05Generator_Reproduces000401FromCurrentRx0A()
    {
        byte[] currentRecorderPage = LoadCapturedReplies()[0x0A];
        byte[] capturedWrite = LoadHhdWrites()[16];

        Assert.Equal(
            capturedWrite,
            OtmrProtocolDerivation.CreateUnmodifiedConfigurationPage05Write(currentRecorderPage));
        Assert.Equal(Payload(currentRecorderPage), Payload(capturedWrite));
    }

    [Fact]
    public void BlockedLargeWrites_HaveTheCapturedRxCopyDeleteInsertAndModifyLayout()
    {
        IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
        IReadOnlyList<byte[]> writes = LoadHhdWrites();
        byte[] rx02 = Payload(replies[0x02]);
        byte[] tx312 = Payload(writes[12]);
        Assert.Equal(new byte[] { 0x02 }, tx312[..1]);
        Assert.Equal(rx02[5..134], tx312[1..130]);
        Assert.Equal(0x04, rx02[0x86]);
        Assert.Equal(0xFA, tx312[0x82]);
        Assert.Equal(rx02[135..184], tx312[131..180]);
        Assert.Equal(0x00, rx02[0xB8]);
        Assert.Equal(0x03, tx312[0xB4]);
        Assert.Equal(rx02[185..255], tx312[181..251]);
        Assert.Equal(new byte[4], tx312[251..255]);

        byte[] rx04 = Payload(replies[0x04]);
        byte[] tx335 = Payload(writes[13]);
        Assert.Equal(rx04[..0x43], tx335[..0x43]);
        Assert.Equal(new byte[4], rx04[0x43..0x47]);
        Assert.Equal(rx04[0x47..], tx335[0x43..0xFB]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x03 }, tx335[0xFB..]);

        byte[] rx06 = Payload(replies[0x06]);
        byte[] tx357 = Payload(writes[14]);
        Assert.Equal(rx06[4..0x6B], tx357[..0x67]);
        Assert.Equal(new byte[] { 0x01, 0x07 }, rx06[0x6B..0x6D]);
        Assert.Equal(rx06[0x6D..0x83], tx357[0x67..0x7D]);
        Assert.Equal(new byte[2], tx357[0x7D..0x7F]);
        Assert.Equal(rx06[0x83..0xCB], tx357[0x7F..0xC7]);
        Assert.Equal(new byte[] { 0x03, 0x02 }, rx06[0xCB..0xCD]);
        Assert.Equal(rx06[0xCD..0xD3], tx357[0xC7..0xCD]);
        Assert.Equal(new byte[2], tx357[0xCD..0xCF]);
        Assert.Equal(rx06[0xD3..0xEB], tx357[0xCF..0xE7]);
        Assert.Equal(new byte[] { 0x01, 0x13 }, rx06[0xEB..0xED]);
        Assert.Equal(rx06[0xED..], tx357[0xE7..0xF9]);
        Assert.Equal(new byte[6], tx357[0xF9..]);

        byte[] rx08 = Payload(replies[0x08]);
        byte[] tx378 = Payload(writes[15]);
        Assert.Equal(rx08[..0x08], tx378[..0x08]);
        Assert.Equal(new byte[4], rx08[0x08..0x0C]);
        Assert.Equal(rx08[0x0C..0x5C], tx378[0x08..0x58]);
        Assert.Equal(new byte[] { 0x03, 0x02 }, rx08[0x5C..0x5E]);
        Assert.Equal(rx08[0x5E..0x64], tx378[0x58..0x5E]);
        Assert.Equal(new byte[2], tx378[0x5E..0x60]);
        Assert.Equal(rx08[0x64..0x74], tx378[0x60..0x70]);
        Assert.Equal(new byte[] { 0x01, 0x12 }, rx08[0x74..0x76]);
        Assert.Equal(rx08[0x76..0x94], tx378[0x70..0x8E]);
        Assert.Equal(new byte[2], tx378[0x8E..0x90]);
        Assert.Equal(rx08[0x94..], tx378[0x90..0xFB]);
        Assert.Equal(new byte[4], tx378[0xFB..]);

        Assert.Equal(Payload(replies[0x0A]), Payload(writes[16]));

        byte[] rx0C = Payload(replies[0x0C]);
        byte[] tx423 = Payload(writes[17]);
        Assert.Equal(rx0C[..0xC4], tx423[..0xC4]);
        Assert.Equal(new byte[5], rx0C[0xC4..0xC9]);
        Assert.Equal(0x40, tx423[0xC4]);
        Assert.Equal(rx0C[0xC9], tx423[0xC5]);
    }

    [Fact]
    public void BlockedWritePayloads_ContainProvenCrossPageCopiesFromCurrentRecorder()
    {
        IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
        IReadOnlyList<byte[]> writes = LoadHhdWrites();
        byte[] rx04 = Payload(replies[0x04]);
        byte[] rx06 = Payload(replies[0x06]);
        byte[] rx08 = Payload(replies[0x08]);
        byte[] rx0A = Payload(replies[0x0A]);

        Assert.Equal(rx04[..4], Payload(writes[12])[0xFB..]);

        byte[] tx335 = Payload(writes[13]);
        Assert.Equal(rx04[4..], tx335[..0xFB]);
        Assert.Equal(rx06[..4], tx335[0xFB..]);

        Assert.Equal(rx08[..6], Payload(writes[14])[0xF9..]);
        Assert.Equal(rx0A[..4], Payload(writes[15])[0xFB..]);
    }

    [Fact]
    public void SecondHhdStartAndStop_ResolveTwelveBytesAndLeaveTwoPhaseDependent()
    {
        IReadOnlyList<byte[]> oldStartTx = LoadHhdWrites();
        IReadOnlyList<byte[]> newStartTx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Write", 41, 253);
        IReadOnlyList<byte[]> newStopTx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Write", 1033, 1245);
        IReadOnlyList<byte[]> newStartRx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Read", 44, 238);
        IReadOnlyList<byte[]> newStopRx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Read", 1036, 1230);

        Assert.Equal(19, newStartTx.Count);
        Assert.Equal(19, newStopTx.Count);
        Assert.Equal(oldStartTx, newStartTx, ByteArrayComparer.Instance);
        Assert.Equal(newStartRx, newStopRx, ByteArrayComparer.Instance);

        (int Frame, int Offset)[] resolved =
        {
            (12, 0x009),
            (14, 0x086), (14, 0x087), (14, 0x0D6), (14, 0x0D7),
            (15, 0x00F), (15, 0x010), (15, 0x067), (15, 0x068),
            (15, 0x097), (15, 0x098),
            (17, 0x0CD)
        };
        foreach ((int frame, int offset) in resolved)
        {
            Assert.Equal(oldStartTx[frame][offset], newStartTx[frame][offset]);
            Assert.Equal(oldStartTx[frame][offset], newStopTx[frame][offset]);
        }

        byte[] currentRx02 = Payload(newStartRx[1]);
        Assert.Equal(0x01, currentRx02[0x86]);
        Assert.Equal(0x03, currentRx02[0xB8]);
        Assert.Equal(0xFA, newStartTx[12][0x08B]);
        Assert.Equal(0x03, newStartTx[12][0x0BD]);
        Assert.Equal(0xFD, newStopTx[12][0x08B]);
        Assert.Equal(0x00, newStopTx[12][0x0BD]);
        Assert.Equal((byte)(0xFE - currentRx02[0x86]), newStopTx[12][0x08B]);
        Assert.Equal((byte)(0x03 - currentRx02[0xB8]), newStopTx[12][0x0BD]);
        Assert.NotEqual(newStartTx[12][0x08B], newStopTx[12][0x08B]);
        Assert.NotEqual(newStartTx[12][0x0BD], newStopTx[12][0x0BD]);
    }

    [Fact]
    public void Page01StartGenerator_UsesSelectedCcfValuesAndReproducesBothCapturedStarts()
    {
        IReadOnlyDictionary<byte, byte[]> oldReplies = LoadCapturedReplies();
        IReadOnlyList<byte[]> oldWrites = LoadHhdWrites();
        IReadOnlyList<byte[]> newStartRx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Read", 44, 238);
        IReadOnlyList<byte[]> newStartTx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Write", 41, 253);
        byte[] selectedCcf = File.ReadAllBytes(FindTestDataFile("CLASS171_GUI_TEST.ccf"));
        byte[] newPage01 = Assert.Single(newStartRx, frame => frame[1] == 0x02 && frame.Length == 0x10B);
        byte[] newPage02 = Assert.Single(newStartRx, frame => frame[1] == 0x04 && frame.Length == 0x10B);

        Assert.Equal(0x04, selectedCcf[0x0231]);
        Assert.Equal(0x00, selectedCcf[0x0263]);
        Assert.Equal(
            oldWrites[12],
            OtmrProtocolDerivation.CreateConfigurationPage01WriteFromCcf(
                oldReplies[0x02], oldReplies[0x04], selectedCcf));
        Assert.Equal(
            newStartTx[12],
            OtmrProtocolDerivation.CreateConfigurationPage01WriteFromCcf(
                newPage01, newPage02, selectedCcf));
        Assert.Equal(0xFA, newStartTx[12][0x08B]);
        Assert.Equal(0x03, newStartTx[12][0x0BD]);
    }

    [Fact]
    public void Page01Encoding_WithCachedRecorderValues_ReproducesCapturedStopRestoration()
    {
        IReadOnlyList<byte[]> stopRx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Read", 1036, 1230);
        IReadOnlyList<byte[]> stopTx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Write", 1033, 1245);
        byte[] stopPage01 = Assert.Single(stopRx, frame => frame[1] == 0x02 && frame.Length == 0x10B);
        byte[] stopPage02 = Assert.Single(stopRx, frame => frame[1] == 0x04 && frame.Length == 0x10B);
        byte[] currentPage01 = Payload(stopPage01);

        Assert.Equal(0x01, currentPage01[0x86]);
        Assert.Equal(0x03, currentPage01[0xB8]);
        Assert.Equal(
            stopTx[12],
            OtmrProtocolDerivation.CreateConfigurationPage01Write(
                stopPage01, stopPage02, currentPage01[0x86], currentPage01[0xB8]));
        Assert.Equal(0xFD, stopTx[12][0x08B]);
        Assert.Equal(0x00, stopTx[12][0x0BD]);
    }

    [Fact]
    public void CompleteStartConfigurationGenerator_ReproducesBothAuthoritativeStartsWithFullProvenance()
    {
        IReadOnlyDictionary<byte, byte[]> oldReplies = LoadCapturedReplies();
        IReadOnlyList<byte[]> oldCaptured = LoadHhdWrites().Skip(11).Take(7).ToArray();
        IReadOnlyList<byte[]> newStartRx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Read", 44, 238);
        IReadOnlyList<byte[]> newStartTx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Write", 41, 253);
        IReadOnlyDictionary<byte, byte[]> newReplies = SelectRecorderDataReplies(newStartRx);
        IReadOnlyList<byte[]> newCaptured = newStartTx.Skip(11).Take(7).ToArray();
        byte[] selectedCcf = File.ReadAllBytes(FindTestDataFile("CLASS171_GUI_TEST.ccf"));

        OtmrGeneratedConfigurationExchange oldGenerated =
            OtmrConfigurationExchangeGenerator.CreateStart(oldReplies, selectedCcf);
        OtmrGeneratedConfigurationExchange newGenerated =
            OtmrConfigurationExchangeGenerator.CreateStart(newReplies, selectedCcf);

        AssertGeneratedExchange(oldCaptured, oldGenerated);
        AssertGeneratedExchange(newCaptured, newGenerated);
        Assert.Equal(new byte[] { 0x0C, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 },
            newGenerated.Writes.Select(write => write.Bytes[1]).ToArray());
        Assert.Equal(new[] { 13, 0x10B, 0x10B, 0x10B, 0x10B, 0x10B, 0xD2 },
            newGenerated.Writes.Select(write => write.Bytes.Length).ToArray());

        OtmrGeneratedConfigurationWrite page01 = newGenerated.Writes[1];
        Assert.Equal(0xFA, page01.Bytes[0x08B]);
        Assert.Equal(0x03, page01.Bytes[0x0BD]);
        Assert.Equal(OtmrConfigurationByteSource.SelectedCcf, page01.Provenance[0x08B].Source);
        Assert.Equal(OtmrConfigurationByteSource.SelectedCcf, page01.Provenance[0x0BD].Source);
        Assert.Contains("0x0231", page01.Provenance[0x08B].SourceReference);
        Assert.Contains("0x0263", page01.Provenance[0x0BD].SourceReference);

        byte[] variedCcf = selectedCcf.ToArray();
        variedCcf[0x0231] = 0x19;
        variedCcf[0x0263] = 0xF7;
        OtmrGeneratedConfigurationWrite variedPage01 =
            OtmrConfigurationExchangeGenerator.CreateStart(newReplies, variedCcf).Writes[1];
        Assert.Equal(unchecked((byte)(0xFE - 0x19)), variedPage01.Bytes[0x08B]);
        Assert.Equal(unchecked((byte)(0x03 - 0xF7)), variedPage01.Bytes[0x0BD]);
    }

    [Fact]
    public void CompleteStopRestorationGenerator_ReproducesAuthoritativeCleanupExchange()
    {
        IReadOnlyList<byte[]> stopRx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Read", 1036, 1230);
        IReadOnlyList<byte[]> stopTx = LoadHhdProtocolFrames(
            "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt", "Write", 1033, 1245);
        IReadOnlyDictionary<byte, byte[]> replies = SelectRecorderDataReplies(stopRx);
        IReadOnlyList<byte[]> captured = stopTx.Skip(11).Take(7).ToArray();

        OtmrGeneratedConfigurationExchange generated =
            OtmrConfigurationExchangeGenerator.CreateStopRestoration(replies);

        AssertGeneratedExchange(captured, generated);
        OtmrGeneratedConfigurationWrite page01 = generated.Writes[1];
        Assert.Equal(0xFD, page01.Bytes[0x08B]);
        Assert.Equal(0x00, page01.Bytes[0x0BD]);
        Assert.Equal(OtmrConfigurationByteSource.RecorderRx, page01.Provenance[0x08B].Source);
        Assert.Equal(OtmrConfigurationByteSource.RecorderRx, page01.Provenance[0x0BD].Source);
        Assert.Contains("cached RX 01 02 payload 0x86", page01.Provenance[0x08B].SourceReference);
        Assert.Contains("cached RX 01 02 payload 0xB8", page01.Provenance[0x0BD].SourceReference);
    }

    [Fact]
    public void CapturedStop_ClosesLiveThenPerformsLowDtrCleanupExchangeAndFinalClose()
    {
        const string filename = "HHD_Serial_Trace_20260825_START_LIVE_STOP.txt";
        string trace = File.ReadAllText(FindTestDataFile(filename));
        byte[] liveRead = LoadHhdRequestBytes(filename, "Read", 294);
        byte[] expectedLastFrame =
            { 0xFB, 0xFB, 0x38, 0x8F, 0x38, 0x90, 0x38, 0x8F, 0x38, 0x90, 0xFF };

        Assert.Equal(0xD3, liveRead.Length);
        Assert.Equal(expectedLastFrame, liveRead[^expectedLastFrame.Length..]);
        Assert.Empty(LoadHhdRequestBlocks(filename, "Write", 254, 990));

        DateTime closeLive = LoadHhdTimestamp(filename, 991);
        Assert.Equal(TimeSpan.FromTicks(2_003_279), LoadHhdTimestamp(filename, 993) - closeLive);
        Assert.Equal(TimeSpan.FromTicks(2_253_365), LoadHhdTimestamp(filename, 994) - closeLive);
        Assert.Equal(TimeSpan.FromTicks(2_425_821), LoadHhdTimestamp(filename, 1033) - closeLive);
        Assert.Equal(
            TimeSpan.FromTicks(45_618),
            LoadHhdTimestamp(filename, 1247) - LoadHhdTimestamp(filename, 1245));

        string cleanupSetup = trace[trace.IndexOf("000993:", StringComparison.Ordinal)..
            trace.IndexOf("001033:", StringComparison.Ordinal)];
        Assert.Contains("Baud Rate=38400", cleanupSetup);
        Assert.Contains("IOCTL_SERIAL_CLR_RTS", cleanupSetup);
        Assert.Contains("IOCTL_SERIAL_CLR_DTR", cleanupSetup);
        Assert.Contains("WordLength=8", cleanupSetup);
        Assert.Contains("StopBits=1 stop bit", cleanupSetup);
        Assert.Contains("Parity=No parity", cleanupSetup);
        Assert.Equal(3, Regex.Matches(cleanupSetup, "IOCTL_SERIAL_PURGE").Count);

        IReadOnlyList<byte[]> stopFrames = LoadHhdProtocolFrames(filename, "Write", 1033, 1245);
        Assert.Equal(19, stopFrames.Count);
        Assert.Equal(OtmrLiveStartProtocol.Query01.ToArray(), stopFrames[0]);
        Assert.Equal(OtmrLiveStartProtocol.FinalLiveStart07.ToArray(), stopFrames[^1]);
        Assert.Contains("001247: Close Request (DOWN)", trace);
        Assert.DoesNotContain("Create Request (DOWN)", trace[(trace.IndexOf("001247:", StringComparison.Ordinal) + 1)..]);
    }

    [Fact]
    public async Task SelectedCcfIsRequiredAndFailureSendsNoConfigurationOrInterrogationTx()
    {
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport, FastStartTiming());
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        OtmrConfigurationPreflightException error =
            await Assert.ThrowsAsync<OtmrConfigurationPreflightException>(() => service.StartLiveAsync());

        Assert.Contains("explicitly selected", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OtmrLiveState.RecorderConfigurationWriteBlocked, service.State);
        Assert.Empty(transport.Transmissions);
        Assert.Single(transport.ConnectionSettings);
        Assert.DoesNotContain("DISCONNECT", transport.Operations);
    }

    [Fact]
    public async Task FinalLiveCommand_IsImpossibleBeforeCompleteOrdered01_13()
    {
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport, FastStartTiming());
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync(LoadSelectedCcf());
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_01);
        transport.EmitRx(OtmrLiveStartProtocol.Reply13.ToArray());

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => start);
        Assert.Contains("Expected OTMR stage 01 01", error.Message);
        Assert.Single(transport.Transmissions);
        Assert.DoesNotContain(transport.Transmissions,
            bytes => bytes.AsSpan().SequenceEqual(OtmrLiveStartProtocol.FinalLiveStart07.Span));
        Assert.Equal(OtmrLiveState.Error, service.State);
    }

    [Fact]
    public async Task ExactCapturedEvidence_ProvesPost01_13CloseAndImmediateDtrHighReopen()
    {
        IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport, new OtmrLiveStartTiming(
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1)));
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync(LoadSelectedCcf());
        await DriveSafeInterrogationAsync(transport, service, replies);
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_0D);
        Assert.Equal(13, transport.Transmissions.Count);

        int expectedTxCount = 13;
        foreach (byte transaction in new byte[] { 0x0D, 0x0E, 0x0F, 0x10, 0x11 })
        {
            transport.EmitRx(replies[transaction]);
            expectedTxCount++;
            await WaitForTransmissionCountAsync(transport, expectedTxCount);
            Assert.DoesNotContain(transport.Transmissions,
                bytes => bytes.AsSpan().SequenceEqual(OtmrLiveStartProtocol.FinalLiveStart07.Span));
        }

        transport.EmitRx(replies[0x12]);
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_13);
        Assert.Equal(18, transport.Transmissions.Count);
        Assert.DoesNotContain(transport.Transmissions,
            bytes => bytes.AsSpan().SequenceEqual(OtmrLiveStartProtocol.FinalLiveStart07.Span));

        transport.EmitRx(replies[0x13]);
        await start;

        Assert.Equal(19, transport.Transmissions.Count);
        Assert.Equal(OtmrLiveStartProtocol.FinalLiveStart07.ToArray(), transport.Transmissions[^1]);
        Assert.Equal(
            LoadHhdWrites().Skip(11).Take(7),
            transport.Transmissions.Skip(11).Take(7),
            ByteArrayComparer.Instance);
        Assert.Equal(new[]
        {
            "TX:01-07-00-01-01-01-02-01-02-13-03-13-04",
            "DISCONNECT",
            "CONNECT:RTS=LOW:DTR=HIGH"
        }, transport.Operations.TakeLast(3));
        Assert.Equal(TimeSpan.FromMilliseconds(17.6), OtmrLiveStartTiming.HardwareDefault.FinalCommandToCloseDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(80.5), OtmrLiveStartTiming.HardwareDefault.FinalReplyToFinalCommandDelay);
        Assert.Equal(2, transport.ConnectionSettings.Count);
        Assert.False(transport.ConnectionSettings[1].RtsEnable);
        Assert.True(transport.ConnectionSettings[1].DtrEnable);
        Assert.Equal(OtmrLiveState.LiveReady, service.State);
        Assert.False(service.IsLiveActive);
        Assert.True(service.IsLiveReady);
        Assert.Equal(0, service.CompleteLiveFrameCount);
        var deliveredFrames = new List<OtmrLiveFrame>();
        service.FrameReceived += (_, e) => deliveredFrames.Add(e.Frame);

        string[] transitionDiagnostics = service.GetDiagnosticSnapshot()
            .Select(entry => entry.Message)
            .Where(message => message.StartsWith("LIVE TRANSITION", StringComparison.Ordinal))
            .ToArray();
        Assert.Collection(
            transitionDiagnostics,
            message => Assert.StartsWith("LIVE TRANSITION 1/11: final 01 07 write begins", message),
            message => Assert.StartsWith("LIVE TRANSITION 2/11: final 01 07 write returned", message),
            message => Assert.StartsWith("LIVE TRANSITION 3/11: 1.0000 ms delay begins", message),
            message => Assert.StartsWith("LIVE TRANSITION 4/11: 1.0000 ms delay ends", message),
            message => Assert.StartsWith("LIVE TRANSITION: transport disconnect begins", message),
            message => Assert.StartsWith("LIVE TRANSITION: transport disconnect returned", message),
            message => Assert.StartsWith("LIVE TRANSITION 7/11: reopen begins", message),
            message => Assert.StartsWith("LIVE TRANSITION: transport reopen returned", message));

        transport.EmitRx(new byte[] { 0xFF, 0xD2, 0x0C, 0xFB });
        Assert.Equal(OtmrLiveState.LiveReady, service.State);
        transport.EmitRx(new byte[] { 0xFB, 0x38, 0x4B });
        transport.EmitRx(new byte[] { 0x38, 0x4A });
        Assert.Equal(OtmrLiveState.LiveReady, service.State);
        transport.EmitRx(new byte[] { 0xFF });
        Assert.Equal(OtmrLiveState.LiveActive, service.State);
        Assert.True(service.IsLiveActive);
        Assert.Equal(1, service.CompleteLiveFrameCount);
        Assert.Single(deliveredFrames);
        Assert.Equal(new byte[] { 0xFB, 0xFB, 0x38, 0x4B, 0x38, 0x4A, 0xFF },
            deliveredFrames[0].GetDataSnapshot());
        Assert.Contains(service.GetDiagnosticSnapshot(), entry =>
            entry.Message.StartsWith("LIVE_FRAME_ASSEMBLER_BUFFER", StringComparison.Ordinal));
        Assert.Contains(service.GetDiagnosticSnapshot(), entry =>
            entry.Message.StartsWith("LIVE_FRAME_ASSEMBLED", StringComparison.Ordinal) &&
            entry.Message.Contains("total=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FinalLiveCommand_WaitsForCapturedPost01_13Interval()
    {
        IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport, new OtmrLiveStartTiming(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.Zero));
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync(LoadSelectedCcf());
        await DriveSafeInterrogationAsync(transport, service, replies);
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_0D);
        int expectedTxCount = 13;
        foreach (byte transaction in new byte[] { 0x0D, 0x0E, 0x0F, 0x10, 0x11 })
        {
            transport.EmitRx(replies[transaction]);
            await WaitForTransmissionCountAsync(transport, ++expectedTxCount);
        }
        transport.EmitRx(replies[0x12]);
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_13);

        transport.EmitRx(replies[0x13]);
        await WaitForAsync(() => service.GetDiagnosticSnapshot().Any(entry =>
            entry.Message.StartsWith("FINAL_0113_TO_0107_WAIT_BEGIN", StringComparison.Ordinal)));

        Assert.DoesNotContain(transport.Transmissions,
            bytes => bytes.AsSpan().SequenceEqual(OtmrLiveStartProtocol.FinalLiveStart07.Span));
        await Task.Delay(25);
        Assert.DoesNotContain(transport.Transmissions,
            bytes => bytes.AsSpan().SequenceEqual(OtmrLiveStartProtocol.FinalLiveStart07.Span));

        await start;
        Assert.Equal(OtmrLiveStartProtocol.FinalLiveStart07.ToArray(), transport.Transmissions[^1]);
        string[] diagnostics = service.GetDiagnosticSnapshot().Select(entry => entry.Message).ToArray();
        Assert.Contains(diagnostics, message => message.StartsWith("FINAL_0113_COMPLETE", StringComparison.Ordinal));
        Assert.Contains(diagnostics, message => message.StartsWith("FINAL_0113_TO_0107_WAIT_BEGIN", StringComparison.Ordinal));
        Assert.Contains(diagnostics, message => message.StartsWith("FINAL_0113_TO_0107_WAIT_END", StringComparison.Ordinal));
        Assert.Contains(diagnostics, message => message.StartsWith("FINAL_0107_TX_BEGIN", StringComparison.Ordinal));
    }

    [Fact]
    public void StartTiming_SeparatesCapturedPostReplyAndPostCommandIntervals()
    {
        OtmrLiveStartTiming hardware = OtmrLiveStartTiming.HardwareDefault;

        Assert.InRange(hardware.FinalReplyToFinalCommandDelay.TotalMilliseconds, 80.4, 80.6);
        Assert.Equal(TimeSpan.FromMilliseconds(17.6), hardware.FinalCommandToCloseDelay);
        Assert.NotEqual(hardware.FinalReplyToFinalCommandDelay, hardware.FinalCommandToCloseDelay);
        Assert.Equal(TimeSpan.Zero, FastStartTiming().FinalReplyToFinalCommandDelay);
        Assert.Equal(TimeSpan.Zero, FastStartTiming().FinalCommandToCloseDelay);
    }

    [Fact]
    public async Task GeneratedConfigurationWritesAreReplyGatedAndWrongReplyStopsAllLaterTx()
    {
        IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport, FastStartTiming());
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync(LoadSelectedCcf());
        await DriveSafeInterrogationAsync(transport, service, replies);
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_0D);
        Assert.Equal(13, transport.Transmissions.Count);

        transport.EmitRx(replies[0x0E]);
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => start);

        Assert.Contains("Expected OTMR stage 01 0D", error.Message);
        Assert.Equal(13, transport.Transmissions.Count);
        Assert.DoesNotContain(transport.Transmissions,
            bytes => bytes.AsSpan().SequenceEqual(OtmrLiveStartProtocol.FinalLiveStart07.Span));
        Assert.Equal(OtmrLiveState.Error, service.State);
    }

    [Fact]
    public async Task GeneratedConfigurationReplyTimeoutDoesNotAdvanceOrAutoRestore()
    {
        IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(
            transport,
            FastStartTiming() with { StageReplyTimeout = TimeSpan.FromMilliseconds(50) });
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync(LoadSelectedCcf());
        await DriveSafeInterrogationAsync(transport, service, replies);
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_0D);
        TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() => start);

        Assert.Contains("01 0D", error.Message);
        Assert.Equal(13, transport.Transmissions.Count);
        Assert.Single(transport.ConnectionSettings);
        Assert.DoesNotContain("DISCONNECT", transport.Operations);
        Assert.Equal(OtmrLiveState.Error, service.State);
    }

    [Fact]
    public async Task StopLiveClosesPortWithoutTxAndSeparateRestoreUsesFrozenOriginalPages()
    {
        IReadOnlyDictionary<byte, byte[]> originalReplies = LoadCapturedReplies();
        IReadOnlyDictionary<byte, byte[]> changedReplies = CreateRepliesWithChangedPage01Fields(
            originalReplies, value0231: 0x01, value0263: 0x03);
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport, FastStartTiming(), FastRestoreTiming());
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync(LoadSelectedCcf());
        await DriveSafeInterrogationAsync(transport, service, originalReplies);
        await DriveGeneratedReplyStagesAsync(transport, service, originalReplies, start);
        transport.EmitRx(new byte[] { 0xFB, 0xFB, 0x44, 0xFF });
        Assert.Equal(OtmrLiveState.LiveActive, service.State);

        int txBeforeStop = transport.Transmissions.Count;
        await service.StopLiveAsync();
        Assert.Equal(txBeforeStop, transport.Transmissions.Count);
        Assert.Equal(OtmrLiveState.NotLive, service.State);
        Assert.False(transport.IsConnected);
        Assert.Equal("DISCONNECT", transport.Operations[^1]);

        Task restore = service.StopAndRestoreAsync();
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_01);
        Assert.False(transport.ConnectionSettings[^1].RtsEnable);
        Assert.False(transport.ConnectionSettings[^1].DtrEnable);
        await DriveSafeInterrogationAsync(transport, service, changedReplies);
        await DriveGeneratedReplyStagesAsync(transport, service, changedReplies, restore);

        OtmrGeneratedConfigurationExchange expectedRestoration =
            OtmrConfigurationExchangeGenerator.CreateStopRestoration(originalReplies);
        Assert.Equal(
            expectedRestoration.Writes.Select(write => write.Bytes),
            transport.Transmissions.Skip(30).Take(7),
            ByteArrayComparer.Instance);
        Assert.NotEqual(0xFD, transport.Transmissions[31][0x08B]);
        Assert.Equal(0xFA, transport.Transmissions[31][0x08B]);
        Assert.Equal(0x03, transport.Transmissions[31][0x0BD]);
        Assert.Equal(OtmrLiveState.NotLive, service.State);
        Assert.False(transport.IsConnected);
        Assert.Contains("RESTORE final 01 07", service.GetCaptureSnapshot().Last(entry =>
            entry.Direction == OtmrDirection.Tx).Interpretation ?? string.Empty);
    }

    [Fact]
    public async Task StopLiveFromLiveReadyClosesPortWithoutInventingTx()
    {
        IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport, FastStartTiming());
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync(LoadSelectedCcf());
        await DriveSafeInterrogationAsync(transport, service, replies);
        await DriveGeneratedReplyStagesAsync(transport, service, replies, start);
        Assert.Equal(OtmrLiveState.LiveReady, service.State);
        Assert.Equal(0, service.CompleteLiveFrameCount);

        int transmissions = transport.Transmissions.Count;
        await service.StopLiveAsync();

        Assert.Equal(transmissions, transport.Transmissions.Count);
        Assert.Equal(OtmrLiveState.NotLive, service.State);
        Assert.False(transport.IsConnected);
        Assert.Equal("DISCONNECT", transport.Operations[^1]);
    }

    [Fact]
    public async Task GeneratedWriteDiagnosticsAndRawCaptureCoverEveryHardwareTx()
    {
        IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport, FastStartTiming());
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync(LoadSelectedCcf());
        await DriveSafeInterrogationAsync(transport, service, replies);
        await DriveGeneratedReplyStagesAsync(transport, service, replies, start);

        IReadOnlyList<OtmrProtocolDiagnosticEntry> diagnostics = service.GetDiagnosticSnapshot();
        foreach (int number in Enumerable.Range(1, 7))
        {
            Assert.Contains(diagnostics, entry => entry.Message.Contains($"START WRITE {number}/7", StringComparison.Ordinal));
        }
        Assert.Contains(diagnostics, entry => entry.Message.Contains("reply 01 12 received", StringComparison.Ordinal));
        Assert.Equal(transport.Transmissions.Count,
            service.GetCaptureSnapshot().Count(entry => entry.Direction == OtmrDirection.Tx));
        Assert.All(
            service.GetCaptureSnapshot().Where(entry => entry.Direction == OtmrDirection.Tx).Skip(11).Take(7),
            entry => Assert.Contains("START WRITE", entry.Interpretation));
    }

    [Fact]
    public async Task IncompatibleSelectedCcfFailsBeforeAnyGeneratedConfigurationWrite()
    {
        IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
        CcfDocument incompatible = LoadSelectedCcf();
        CcfEditService.SetHeaderAscii(incompatible, CcfFieldDefinitions.Header.SerialCore, 8, "BAD171");
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(transport, FastStartTiming());
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        Task start = service.StartLiveAsync(incompatible);
        await DriveSafeInterrogationAsync(transport, service, replies);
        OtmrConfigurationPreflightException error =
            await Assert.ThrowsAsync<OtmrConfigurationPreflightException>(() => start);

        Assert.Contains("identity compatibility failed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(11, transport.Transmissions.Count);
        Assert.DoesNotContain(transport.Transmissions.Skip(11), _ => true);
        Assert.Equal(OtmrLiveState.RecorderConfigurationWriteBlocked, service.State);
    }

    [Fact]
    public async Task MissingFirstReplyNeverSendsLaterCommandOrReopensPort()
    {
        using var transport = new FakeTransport();
        using var service = new OtmrLiveService(
            transport,
            FastStartTiming() with { StageReplyTimeout = TimeSpan.FromMilliseconds(50) });
        await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

        TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() => service.StartLiveAsync(LoadSelectedCcf()));

        Assert.Contains("01 01", error.Message);
        Assert.Single(transport.Transmissions);
        Assert.Single(transport.ConnectionSettings);
        Assert.DoesNotContain("DISCONNECT", transport.Operations);
        Assert.Equal(OtmrLiveState.Error, service.State);
    }

    [Fact]
    public async Task SqliteRecordingPreservesEveryCapturedTxAndFragmentedRxChunk()
    {
        string folder = Path.Combine(Path.GetTempPath(), "otmr-live-state-" + Guid.NewGuid().ToString("N"));
        string database = Path.Combine(folder, "OTMR_RCM.db");
        Directory.CreateDirectory(folder);
        try
        {
            await using var store = new SqliteOtmrRecordingStore(database);
            Guid sessionId = await store.StartSessionAsync(new OtmrRecordingSessionContext
            {
                SoftwareVersion = "captured-state-machine-test",
                ComPort = "COM7"
            });
            using var transport = new FakeTransport();
            using var service = new OtmrLiveService(transport, FastStartTiming());
            service.SetRecordingStore(store);
            await service.ConnectAsync(OtmrSerialSettings.Class171Bench("COM7"));

            IReadOnlyDictionary<byte, byte[]> replies = LoadCapturedReplies();
            Task start = service.StartLiveAsync(LoadSelectedCcf());
            await DriveSafeInterrogationAsync(transport, service, replies, fragmentEveryFrame: true);
            await DriveGeneratedReplyStagesAsync(transport, service, replies, start);

            OtmrCaptureEntry[] captured = service.GetCaptureSnapshot().ToArray();
            await store.StopSessionAsync(DateTimeOffset.UtcNow);
            OtmrSessionUploadPackage package = await store.BuildUploadPackageAsync(sessionId);

            Assert.Equal(captured.Length, package.RawEntries.Count);
            for (int i = 0; i < captured.Length; i++)
            {
                Assert.Equal(captured[i].Direction.ToString().ToUpperInvariant(), package.RawEntries[i].Direction);
                Assert.Equal(captured[i].GetDataSnapshot(), package.RawEntries[i].Data);
            }
            Assert.Equal(19, package.RawEntries.Count(entry => entry.Direction == "TX"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
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
    public async Task CaptureWritersPreserveTimestampDirectionAndRawHex()
    {
        string folder = Path.Combine(Path.GetTempPath(), "otmr-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string jsonPath = Path.Combine(folder, "capture.jsonl");
        string textPath = Path.Combine(folder, "capture.txt");
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
            await OtmrCaptureWriter.WriteJsonLinesAsync(jsonPath, entries);
            await OtmrCaptureWriter.WriteTextAsync(textPath, entries);

            string json = await File.ReadAllTextAsync(jsonPath);
            string text = await File.ReadAllTextAsync(textPath);
            Assert.Contains("FC AB", json);
            Assert.Contains("OTMR RAW CAPTURE", text);
            Assert.Contains("TX", text);
            Assert.Contains("RX", text);
            Assert.Contains("2026-08-20T20:11:42.134", text);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task ProveReplyGatedPairAsync(
        FakeTransport transport,
        OtmrLiveService service,
        IReadOnlyDictionary<byte, byte[]> replies,
        byte smallReply,
        byte dataReply,
        int txCountBeforeData,
        ReadOnlyMemory<byte> expectedFirst,
        ReadOnlyMemory<byte> expectedSecond)
    {
        transport.EmitRx(replies[smallReply]);
        await WaitForAsync(() => service.State == StateFor(dataReply));
        Assert.Equal(txCountBeforeData, transport.Transmissions.Count);

        EmitFragmented(transport, replies[dataReply], 9, 73);
        await WaitForTransmissionCountAsync(transport, txCountBeforeData + 2);
        Assert.Equal(expectedFirst.ToArray(), transport.Transmissions[^2]);
        Assert.Equal(expectedSecond.ToArray(), transport.Transmissions[^1]);
    }

    private static async Task DriveSafeInterrogationAsync(
        FakeTransport transport,
        OtmrLiveService service,
        IReadOnlyDictionary<byte, byte[]> replies,
        bool fragmentEveryFrame = false)
    {
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_01);
        foreach (byte transaction in Enumerable.Range(1, 12).Select(value => (byte)value))
        {
            if (fragmentEveryFrame || transaction is 0x02 or 0x04 or 0x06 or 0x08 or 0x0A or 0x0C)
                EmitFragmented(transport, replies[transaction], 1, 8, 37);
            else
                transport.EmitRx(replies[transaction]);

            if (transaction < 0x0C)
                await WaitForAsync(() => service.State == StateFor((byte)(transaction + 1)));
        }
    }

    private static async Task DriveGeneratedReplyStagesAsync(
        FakeTransport transport,
        OtmrLiveService service,
        IReadOnlyDictionary<byte, byte[]> replies,
        Task operation)
    {
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_0D);
        int transmissions = transport.Transmissions.Count;
        int finalCommandsBefore = transport.Transmissions.Count(
            bytes => bytes.AsSpan().SequenceEqual(OtmrLiveStartProtocol.FinalLiveStart07.Span));
        foreach (byte transaction in new byte[] { 0x0D, 0x0E, 0x0F, 0x10, 0x11 })
        {
            transport.EmitRx(replies[transaction]);
            transmissions++;
            await WaitForTransmissionCountAsync(transport, transmissions);
        }
        transport.EmitRx(replies[0x12]);
        await WaitForAsync(() => service.State == OtmrLiveState.WaitingFor01_13);
        Assert.Equal(finalCommandsBefore, transport.Transmissions.Count(
            bytes => bytes.AsSpan().SequenceEqual(OtmrLiveStartProtocol.FinalLiveStart07.Span)));
        transport.EmitRx(replies[0x13]);
        await operation;
    }

    private static IReadOnlyDictionary<byte, byte[]> CreateRepliesWithChangedPage01Fields(
        IReadOnlyDictionary<byte, byte[]> source,
        byte value0231,
        byte value0263)
    {
        Dictionary<byte, byte[]> changed = source.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        byte[] page01 = changed[0x02];
        page01[9 + 0x86] = value0231;
        page01[9 + 0xB8] = value0263;
        page01[^2] = OtmrProtocolDerivation.ComputePayloadCheckByte(page01.AsSpan(9, page01.Length - 12));
        return changed;
    }

    private static OtmrLiveState StateFor(byte transaction) => transaction switch
    {
        0x01 => OtmrLiveState.WaitingFor01_01,
        0x02 => OtmrLiveState.WaitingFor01_02,
        0x03 => OtmrLiveState.WaitingFor01_03,
        0x04 => OtmrLiveState.WaitingFor01_04,
        0x05 => OtmrLiveState.WaitingFor01_05,
        0x06 => OtmrLiveState.WaitingFor01_06,
        0x07 => OtmrLiveState.WaitingFor01_07,
        0x08 => OtmrLiveState.WaitingFor01_08,
        0x09 => OtmrLiveState.WaitingFor01_09,
        0x0A => OtmrLiveState.WaitingFor01_0A,
        0x0B => OtmrLiveState.WaitingFor01_0B,
        0x0C => OtmrLiveState.WaitingFor01_0C,
        _ => throw new ArgumentOutOfRangeException(nameof(transaction))
    };

    private static void EmitFragmented(FakeTransport transport, byte[] bytes, params int[] splitPoints)
    {
        int offset = 0;
        foreach (int splitPoint in splitPoints.Where(value => value > offset && value < bytes.Length))
        {
            transport.EmitRx(bytes[offset..splitPoint]);
            offset = splitPoint;
        }
        transport.EmitRx(bytes[offset..]);
    }

    private static async Task WaitForTransmissionCountAsync(FakeTransport transport, int count) =>
        await WaitForAsync(() => transport.Transmissions.Count == count);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
            await Task.Delay(1, timeout.Token);
    }

    private static IReadOnlyDictionary<byte, byte[]> LoadCapturedReplies()
    {
        string path = FindTestDataFile("OTMR_RAW_CAPTURE_20260824.txt");
        var assembler = new OtmrProtocolFrameAssembler();
        var replies = new Dictionary<byte, byte[]>();
        foreach (string line in File.ReadLines(path))
        {
            string[] parts = Regex.Split(line, "  +", RegexOptions.CultureInvariant);
            if (parts.Length < 3 || parts[1] != "RX" || !parts[0].StartsWith("2026-08-24T14:32:", StringComparison.Ordinal))
                continue;
            byte[] chunk = parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => Convert.ToByte(value, 16))
                .ToArray();
            foreach (byte[] frame in assembler.Append(chunk))
                replies[frame[1]] = frame;
        }

        Assert.Equal(19, replies.Count);
        return replies;
    }

    private static byte[] Payload(byte[] frame) => frame.AsSpan(9, frame.Length - 12).ToArray();

    private static IReadOnlyDictionary<byte, byte[]> SelectRecorderDataReplies(IEnumerable<byte[]> frames)
    {
        var replies = new Dictionary<byte, byte[]>();
        foreach ((byte transaction, int length) in new[]
        {
            (Transaction: (byte)0x02, Length: 0x10B),
            (Transaction: (byte)0x04, Length: 0x10B),
            (Transaction: (byte)0x06, Length: 0x10B),
            (Transaction: (byte)0x08, Length: 0x10B),
            (Transaction: (byte)0x0A, Length: 0x10B),
            (Transaction: (byte)0x0C, Length: 0xD6)
        })
        {
            replies[transaction] = Assert.Single(
                frames,
                frame => frame[1] == transaction && frame.Length == length);
        }
        return replies;
    }

    private static void AssertGeneratedExchange(
        IReadOnlyList<byte[]> captured,
        OtmrGeneratedConfigurationExchange generated)
    {
        Assert.Equal(7, captured.Count);
        Assert.Equal(7, generated.Writes.Count);
        for (int index = 0; index < generated.Writes.Count; index++)
        {
            OtmrGeneratedConfigurationWrite write = generated.Writes[index];
            Assert.Equal(captured[index], write.Bytes);
            Assert.Equal(write.Bytes.Length, write.Provenance.Count);
            Assert.Equal(0x01, write.Bytes[0]);
            Assert.Equal(0x02, write.Bytes[8]);
            Assert.Equal(0x03, write.Bytes[^3]);
            Assert.Equal(0x04, write.Bytes[^1]);
            Assert.Equal(
                OtmrProtocolDerivation.ComputePayloadCheckByte(
                    write.Bytes.AsSpan(9, write.Bytes.Length - 12)),
                write.Bytes[^2]);
            Assert.All(write.Provenance, provenance =>
            {
                Assert.InRange(provenance.FrameOffset, 0, write.Bytes.Length - 1);
                Assert.Equal(write.Bytes[provenance.FrameOffset], provenance.Value);
                Assert.False(string.IsNullOrWhiteSpace(provenance.SourceReference));
            });
        }
    }

    private static IReadOnlyList<byte[]> LoadHhdWrites()
    {
        string path = FindTestDataFile("HHD_Serial_Trace_20260824_143201.txt");
        var writes = new List<byte[]>();
        List<byte>? current = null;
        foreach (string line in File.ReadLines(path))
        {
            if (Regex.IsMatch(line, @"^\d+: Write Request \(DOWN\),", RegexOptions.CultureInvariant))
            {
                if (current is not null)
                    writes.Add(current.ToArray());
                current = new List<byte>();
                continue;
            }

            Match bytes = Regex.Match(
                line,
                @"^\s(?<hex>[0-9A-F]{2}(?: [0-9A-F]{2}){0,15})\s{2,}",
                RegexOptions.CultureInvariant);
            if (current is not null && bytes.Success)
            {
                current.AddRange(bytes.Groups["hex"].Value.Split(' ').Select(value => Convert.ToByte(value, 16)));
            }
            else if (current is not null && line.Length == 0)
            {
                writes.Add(current.ToArray());
                current = null;
            }
        }
        if (current is not null)
            writes.Add(current.ToArray());

        Assert.Equal(19, writes.Count);
        return writes;
    }

    private sealed record HhdRequestBlock(int Event, byte[] Bytes);

    private static IReadOnlyList<HhdRequestBlock> LoadHhdRequestBlocks(
        string filename,
        string direction,
        int minimumEvent,
        int maximumEvent)
    {
        string path = FindTestDataFile(filename);
        var blocks = new List<HhdRequestBlock>();
        int? currentEvent = null;
        List<byte>? currentBytes = null;
        foreach (string line in File.ReadLines(path))
        {
            Match header = Regex.Match(
                line,
                $@"^(?<event>\d+): {Regex.Escape(direction)} Request \((?:DOWN|UP)\),",
                RegexOptions.CultureInvariant);
            if (header.Success)
            {
                if (currentEvent is not null && currentBytes is not null &&
                    currentEvent >= minimumEvent && currentEvent <= maximumEvent)
                {
                    blocks.Add(new HhdRequestBlock(currentEvent.Value, currentBytes.ToArray()));
                }
                currentEvent = int.Parse(header.Groups["event"].Value, System.Globalization.CultureInfo.InvariantCulture);
                currentBytes = new List<byte>();
                continue;
            }

            Match bytes = Regex.Match(
                line,
                @"^\s(?<hex>[0-9A-F]{2}(?: [0-9A-F]{2}){0,15})\s{2,}",
                RegexOptions.CultureInvariant);
            if (currentBytes is not null && bytes.Success)
            {
                currentBytes.AddRange(bytes.Groups["hex"].Value.Split(' ')
                    .Select(value => Convert.ToByte(value, 16)));
            }
            else if (currentEvent is not null && currentBytes is not null && line.Length == 0)
            {
                if (currentEvent >= minimumEvent && currentEvent <= maximumEvent)
                    blocks.Add(new HhdRequestBlock(currentEvent.Value, currentBytes.ToArray()));
                currentEvent = null;
                currentBytes = null;
            }
        }
        if (currentEvent is not null && currentBytes is not null &&
            currentEvent >= minimumEvent && currentEvent <= maximumEvent)
        {
            blocks.Add(new HhdRequestBlock(currentEvent.Value, currentBytes.ToArray()));
        }
        return blocks;
    }

    private static IReadOnlyList<byte[]> LoadHhdProtocolFrames(
        string filename,
        string direction,
        int minimumEvent,
        int maximumEvent)
    {
        byte[] stream = LoadHhdRequestBlocks(filename, direction, minimumEvent, maximumEvent)
            .SelectMany(block => block.Bytes)
            .ToArray();
        var frames = new List<byte[]>();
        int offset = 0;
        while (offset < stream.Length)
        {
            while (offset < stream.Length && stream[offset] != 0x01)
                offset++;
            if (offset + 9 > stream.Length)
                break;
            int length = 12 + stream[offset + 7];
            if (offset + length > stream.Length)
                break;
            byte[] frame = stream[offset..(offset + length)];
            if (frame[8] == 0x02 && frame[^3] == 0x03 && frame[^1] == 0x04)
            {
                frames.Add(frame);
                offset += length;
            }
            else
            {
                offset++;
            }
        }
        return frames;
    }

    private static byte[] LoadHhdRequestBytes(string filename, string direction, int eventNumber) =>
        Assert.Single(LoadHhdRequestBlocks(filename, direction, eventNumber, eventNumber)).Bytes;

    private static DateTime LoadHhdTimestamp(string filename, int eventNumber)
    {
        string trace = File.ReadAllText(FindTestDataFile(filename));
        Match match = Regex.Match(
            trace,
            $@"(?m)^{eventNumber:D6}: .+?, (?<timestamp>\d{{4}}-\d{{2}}-\d{{2}} \d{{2}}:\d{{2}}:\d{{2}}\.\d{{7}}) ",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Missing HHD event {eventNumber:D6}.");
        return DateTime.ParseExact(
            match.Groups["timestamp"].Value,
            "yyyy-MM-dd HH:mm:ss.fffffff",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FindTestDataFile(string filename)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "TestData", filename);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Required authoritative OTMR capture was not found: {filename}");
    }

    private static CcfDocument LoadSelectedCcf() =>
        CcfParser.Load(FindTestDataFile("CLASS171_GUI_TEST.ccf"));

    private sealed class FakeTransport : IOtmrTransport
    {
        private readonly object _sync = new();
        public List<OtmrSerialSettings> ConnectionSettings { get; } = new();
        public List<byte[]> Transmissions { get; } = new();
        public List<string> Operations { get; } = new();
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
            lock (_sync)
            {
                IsConnected = true;
                ConnectionSettings.Add(settings);
                Operations.Add($"CONNECT:RTS={(settings.RtsEnable ? "HIGH" : "LOW")}:DTR={(settings.DtrEnable ? "HIGH" : "LOW")}");
            }
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                IsConnected = false;
                Operations.Add("DISCONNECT");
            }
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            byte[] bytes = data.ToArray();
            lock (_sync)
            {
                Transmissions.Add(bytes);
                Operations.Add("TX:" + BitConverter.ToString(bytes));
            }
            BytesTransmitted?.Invoke(this, new OtmrBytesTransmittedEventArgs(bytes));
            return Task.CompletedTask;
        }

        public void EmitRx(byte[] bytes) =>
            BytesReceived?.Invoke(this, new OtmrBytesReceivedEventArgs(bytes));

        public void Dispose()
        {
        }
    }

    private static OtmrLiveStartTiming FastStartTiming() => new(
        TimeSpan.FromSeconds(1),
        TimeSpan.Zero,
        TimeSpan.Zero);

    private static OtmrLiveRestoreTiming FastRestoreTiming() => new(
        TimeSpan.Zero,
        TimeSpan.Zero);

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();
        public bool Equals(byte[]? x, byte[]? y) => x is not null && y is not null && x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) => obj.Aggregate(17, (hash, value) => hash * 31 + value);
    }
}
