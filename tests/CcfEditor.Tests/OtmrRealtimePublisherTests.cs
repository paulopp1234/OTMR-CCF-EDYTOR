using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;
using CcfEditor.Otmr.Sync;
using CcfEditor.Otmr.Transport;
using CcfEditor.WinForms;

namespace CcfEditor.Tests;

public sealed class OtmrRealtimePublisherTests
{
    [Fact]
    public async Task NoRealtimeRequestOccursBeforeExplicitEnable()
    {
        var sent = new ConcurrentQueue<OtmrRealtimeUpdateRequest>();
        await using var publisher = Publisher(sent);

        bool queued = publisher.TryPublish(Decoded("987654", value: 1));
        bool sessionQueued = publisher.TryStartSession(new(
            "987654", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D")));
        await Task.Delay(50);

        Assert.False(queued);
        Assert.False(sessionQueued);
        Assert.Empty(sent);
        Assert.False(publisher.Status.Enabled);
    }

    [Fact]
    public async Task EnabledPublisherWithoutReliableVehicleIdentityDoesNotSend()
    {
        var sent = new ConcurrentQueue<OtmrRealtimeUpdateRequest>();
        await using var publisher = Publisher(sent);
        publisher.Configure(Configuration(enabled: true));
        OtmrRealtimeDecodedState decoded = Decoded("temporary", value: 1) with { VehicleIdentifier = null };

        Assert.False(publisher.TryPublish(decoded));
        Assert.Empty(sent);
        Assert.Equal("REALTIME NOT SENT — VEHICLE ID UNKNOWN", publisher.Status.StatusReason);
    }

    [Fact]
    public async Task EnabledPublisherWithoutLiveConnectionIdentityDoesNotSend()
    {
        var sent = new ConcurrentQueue<OtmrRealtimeUpdateRequest>();
        await using var publisher = Publisher(sent);
        publisher.Configure(Configuration(enabled: true));

        Assert.False(publisher.TryPublish(
            Decoded("987654", value: 1) with { SourceConnectionId = null }));
        Assert.Empty(sent);
        Assert.Contains("LIVE CONNECTION ID UNKNOWN", publisher.Status.StatusReason, StringComparison.Ordinal);
    }

    [Fact]
    public void RcmLiveControlForwardsTheExistingDecoderOutputFromAnAssembledFrame()
    {
        RunInStaThread(() =>
        {
            using var control = new OtmrRcmLiveControl();
            RcmPinProfile pin = RcmVerifiedLiveDecoderTests.VerifiedPin("A", 3, 0, 0, 1);
            RcmProfile profile = RcmVerifiedLiveDecoderTests.Profile(pin);
            VerifiedLiveStateDecodedEventArgs? reported = null;
            control.VerifiedLiveStateDecoded += (_, e) => reported = e;
            control.SetActiveRcmProfile(profile, "verified-profile.json");
            control.SetSourceConnectionId(Guid.NewGuid().ToString("D"));
            OtmrLiveFrame frame = Assert.Single(new OtmrLiveFrameAssembler().Append(
                new byte[] { 0xFB, 0xFB, 0x38, 0x01, 0xFF }));

            control.ReportRawLiveFrame(DateTimeOffset.UtcNow, frame);

            RcmVerifiedLiveSignal expected = Assert.Single(new RcmVerifiedLiveDecoder().Decode(frame, profile));
            RcmVerifiedLiveSignal actual = Assert.Single(Assert.IsType<VerifiedLiveStateDecodedEventArgs>(reported).Signals);
            Assert.Equal(expected.PinId, actual.PinId);
            Assert.Equal(expected.State, actual.State);
            Assert.Equal(expected.RawObservedValue, actual.RawObservedValue);
            Assert.Equal(RcmVerificationStates.Verified, actual.VerificationStatus);
        });
    }

    [Fact]
    public void RcmLiveControlForwardsBothVerifiedSignalsFromOneAssembledFrame()
    {
        RunInStaThread(() =>
        {
            using var control = new OtmrRcmLiveControl();
            RcmPinProfile throttle1 = RcmVerifiedLiveDecoderTests.EventPin(
                "A", "Throttle 1", removed: 0x00, applied: 0x0C);
            RcmPinProfile forward = RcmVerifiedLiveDecoderTests.EventPin(
                "D", "Forward", removed: 0x03, applied: 0x0F);
            RcmProfile profile = RcmVerifiedLiveDecoderTests.Profile(throttle1, forward);
            VerifiedLiveStateDecodedEventArgs? reported = null;
            control.VerifiedLiveStateDecoded += (_, e) => reported = e;
            control.SetActiveRcmProfile(profile, "two-verified-signals.json");
            control.SetSourceConnectionId(Guid.NewGuid().ToString("D"));
            OtmrLiveFrame frame = Assert.Single(new OtmrLiveFrameAssembler().Append(
                new byte[] { 0xFB, 0xFB, 0x0C, 0x03, 0xFF }));

            control.ReportRawLiveFrame(DateTimeOffset.UtcNow, frame);

            VerifiedLiveStateDecodedEventArgs decoded =
                Assert.IsType<VerifiedLiveStateDecodedEventArgs>(reported);
            Assert.Equal(2, decoded.Signals.Count);
            Assert.Equal(
                new[] { "A:ACTIVE:12", "D:INACTIVE:3" },
                decoded.Signals.Select(signal =>
                    $"{signal.Pin}:{(signal.State == RcmDecodedElectricalState.Active ? "ACTIVE" : "INACTIVE")}:" +
                    $"{signal.RawObservedValue}").ToArray());
            DataGridView grid = Find<DataGridView>(control, "decodedSignalsGrid");
            Assert.Equal(2, grid.Rows.Count);
        });
    }

    [Fact]
    public void RcmLiveMaintainsCurrentObservedStatesWithinConnectionAndClearsForNewConnection()
    {
        RunInStaThread(() =>
        {
            using var control = new OtmrRcmLiveControl();
            RcmPinProfile throttle1 = RcmVerifiedLiveDecoderTests.EventPin(
                "A", "Throttle 1", removed: 0x00, applied: 0x0C);
            RcmPinProfile throttle2 = RcmVerifiedLiveDecoderTests.EventPin(
                "B", "Throttle 2", removed: 0x01, applied: 0x0D);
            RcmPinProfile forward = RcmVerifiedLiveDecoderTests.EventPin(
                "D", "Forward", removed: 0x03, applied: 0x0F);
            RcmProfile profile = RcmVerifiedLiveDecoderTests.Profile(throttle1, throttle2, forward);
            control.SetActiveRcmProfile(profile, "session-current-state.json");
            DataGridView grid = Find<DataGridView>(control, "decodedSignalsGrid");
            Label status = Find<Label>(control, "decodedSignalsStatusLabel");

            void Report(params byte[] records)
            {
                byte[] bytes = new byte[records.Length + 3];
                bytes[0] = 0xFB;
                bytes[1] = 0xFB;
                records.CopyTo(bytes, 2);
                bytes[^1] = 0xFF;
                control.ReportRawLiveFrame(
                    DateTimeOffset.UtcNow,
                    Assert.Single(new OtmrLiveFrameAssembler().Append(bytes)));
            }

            Dictionary<string, string> GridStates() => grid.Rows.Cast<DataGridViewRow>()
                .ToDictionary(
                    row => Convert.ToString(row.Cells["decodedPhysicalColumn"].Value)!,
                    row => Convert.ToString(row.Cells["decodedStateColumn"].Value)!);

            string connectionA = Guid.NewGuid().ToString("D");
            control.SetSourceConnectionId(connectionA);
            Assert.Empty(grid.Rows.Cast<DataGridViewRow>());

            Report(0x0C);
            Assert.Equal(new Dictionary<string, string> { ["J1-A"] = "ACTIVE" }, GridStates());
            Report(0x0D);
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["J1-A"] = "ACTIVE",
                    ["J1-B"] = "ACTIVE"
                },
                GridStates());
            Report(0x03);
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["J1-A"] = "ACTIVE",
                    ["J1-B"] = "ACTIVE",
                    ["J1-D"] = "INACTIVE"
                },
                GridStates());
            Report(0x00);
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["J1-A"] = "INACTIVE",
                    ["J1-B"] = "ACTIVE",
                    ["J1-D"] = "INACTIVE"
                },
                GridStates());
            Assert.Contains("Observed this session: 3", status.Text, StringComparison.Ordinal);
            Assert.Contains("Verified mappings available: 3", status.Text, StringComparison.Ordinal);
            Assert.Contains("Latest frame decoded: 1", status.Text, StringComparison.Ordinal);
            Assert.Contains("Last observed:",
                Convert.ToString(grid.Rows[0].Cells["decodedRawColumn"].ToolTipText),
                StringComparison.Ordinal);

            string connectionB = Guid.NewGuid().ToString("D");
            control.SetSourceConnectionId(connectionB);
            Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
            Assert.Contains("Observed this session: 0", status.Text, StringComparison.Ordinal);
            Report(0x01);
            Assert.Equal(new Dictionary<string, string> { ["J1-B"] = "INACTIVE" }, GridStates());

            string connectionC = Guid.NewGuid().ToString("D");
            control.SetSourceConnectionId(connectionC);
            Report(0x0C, 0x0D, 0x03);
            Assert.Equal(3, grid.Rows.Count);
            Report(0x00);
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["J1-A"] = "INACTIVE",
                    ["J1-B"] = "ACTIVE",
                    ["J1-D"] = "INACTIVE"
                },
                GridStates());
            Assert.Contains("Latest frame decoded: 1", status.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainFormRoutesTheRealAssembledFrameToRcmLiveAndExactlyOneRealtimePost()
    {
        string stage = "starting STA thread";
        RunInStaThread(() =>
        {
            stage = "creating profile";
            string folder = Path.Combine(Path.GetTempPath(), $"otmr-realtime-mainform-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            string profilePath = Path.Combine(folder, "runtime-verified-profile.json");
            string unverifiedProfilePath = Path.Combine(folder, "runtime-unverified-profile.json");
            try
            {
                RcmPinProfile pin = RcmVerifiedLiveDecoderTests.VerifiedPin("A", 2, bit: null, removed: 0, applied: 12);
                MakePersistableVerified(pin);
                RcmProfile profile = RcmVerifiedLiveDecoderTests.Profile(pin);
                RcmProfileJson.SaveAsync(profilePath, profile, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
                RcmPinProfile unverifiedPin = RcmVerifiedLiveDecoderTests.VerifiedPin(
                    "A", 2, bit: null, removed: 0, applied: 12);
                MakePersistableVerified(unverifiedPin);
                unverifiedPin.DecoderVerification.Status = RcmVerificationStates.NotVerified;
                unverifiedPin.DecoderVerification.VerifiedAt = null;
                unverifiedPin.Comparison.DecoderVerified = false;
                RcmProfileJson.SaveAsync(
                    unverifiedProfilePath,
                    RcmVerifiedLiveDecoderTests.Profile(unverifiedPin),
                    DateTimeOffset.UtcNow).GetAwaiter().GetResult();

                stage = "constructing MainForm";
                var posted = new ConcurrentQueue<(Uri Uri, OtmrRealtimeUpdateRequest Update)>();
                using var http = new HttpClient(new DelegateHandler(async (request, cancellationToken) =>
                {
                    string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    OtmrRealtimeUpdateRequest update = JsonSerializer.Deserialize<OtmrRealtimeUpdateRequest>(
                        body, OtmrApiV1Json.Options)!;
                    posted.Enqueue((request.RequestUri!, update));
                    return JsonResponse(new OtmrRealtimeUpdateAcknowledgement(
                        OtmrApiContract.Version,
                        update.VehicleIdentifier,
                        true,
                        update.TimestampUtc,
                        update.Signals.Count));
                }));
                var publisher = new OtmrRealtimePublisher(http);
                using var form = new MainForm();
                stage = "loading CCF and RCM profile";
                InvokeLoadCcf(form, FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf"));

                OtmrBenchControl bench = Find<OtmrBenchControl>(form, "otmrBenchControl");
                OtmrLiveControl live = Find<OtmrLiveControl>(form, "otmrLiveControl");
                OtmrRcmLiveControl rcmLive = Find<OtmrRcmLiveControl>(form, "otmrRcmLiveControl");
                OtmrServerSyncControl serverSync = Find<OtmrServerSyncControl>(form, "otmrServerSyncControl");
                Assert.Null(typeof(MainForm).GetField("_recordingStore", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form));
                Task loadProfile = bench.LoadRcmProfileAsync(profilePath);
                WaitUntilWithMessagePump(() => loadProfile.IsCompleted);
                loadProfile.GetAwaiter().GetResult();
                stage = "configuring publisher";
                serverSync.ConfigureRealtimePublisherForTests(publisher, Configuration(enabled: true));
                OtmrLiveService liveService = GetPrivateField<OtmrLiveService>(live, "_liveService");
                InvokePrivate(liveService, "SetState", OtmrLiveState.LiveReady);
                WaitUntilWithMessagePump(() => posted.Count == 1);
                OtmrRealtimeUpdateRequest sessionStart = posted.Single().Update;
                Assert.True(sessionStart.IsSessionStart);
                Assert.Empty(sessionStart.Signals);
                Assert.False(string.IsNullOrWhiteSpace(sessionStart.SourceConnectionId));
                Assert.Equal(liveService.SourceConnectionId, sessionStart.SourceConnectionId);

                int decodedNotifications = 0;
                VerifiedLiveStateDecodedEventArgs? decoded = null;
                rcmLive.VerifiedLiveStateDecoded += (_, e) =>
                {
                    decodedNotifications++;
                    decoded = e;
                };

                DataGridView rawGrid = Find<DataGridView>(live, "captureGrid");
                stage = "injecting partial frame";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFB, 0xFB, 0x0C });
                Assert.Single(posted);
                Assert.Equal(0, decodedNotifications);
                DataGridViewRow partialRow = Assert.Single(rawGrid.Rows.Cast<DataGridViewRow>());
                Assert.Equal("FB FB 0C", Convert.ToString(partialRow.Cells["bytesColumn"].Value));
                Assert.Equal("PARTIAL LIVE FRAME — waiting for FF",
                    Convert.ToString(partialRow.Cells["interpretationColumn"].Value));

                stage = "injecting terminating FF chunk";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFF });
                stage = "waiting for first POST";
                WaitUntilWithMessagePump(() => posted.Count == 2);

                Assert.Equal(1, decodedNotifications);
                RcmVerifiedLiveSignal decodedSignal = Assert.Single(Assert.IsType<VerifiedLiveStateDecodedEventArgs>(decoded).Signals);
                Assert.Equal(RcmDecodedElectricalState.Active, decodedSignal.State);
                Assert.Equal(RcmVerificationStates.Verified, decodedSignal.VerificationStatus);
                (Uri uri, OtmrRealtimeUpdateRequest update) = posted.Last();
                Assert.EndsWith("/api/v1/otmr/vehicles/171804/live", uri.AbsolutePath, StringComparison.Ordinal);
                Assert.Equal("171804", update.VehicleIdentifier);
                Assert.False(update.IsSessionStart);
                Assert.Equal(sessionStart.SourceConnectionId, update.SourceConnectionId);
                OtmrRealtimeSignalUpdate postedSignal = Assert.Single(update.Signals);
                Assert.Equal(decodedSignal.PinId, postedSignal.SignalId);
                Assert.Equal(OtmrRealtimeContract.Active, postedSignal.State);
                Assert.Equal(decodedSignal.RawObservedValue, postedSignal.RawValue);
                Assert.Equal(OtmrRealtimeContract.Verified, postedSignal.Verification);
                Assert.Equal(12, postedSignal.RawValue);

                DataGridView grid = Find<DataGridView>(rcmLive, "decodedSignalsGrid");
                Assert.Equal("ACTIVE", Convert.ToString(Assert.Single(grid.Rows.Cast<DataGridViewRow>())
                    .Cells["decodedStateColumn"].Value));
                Assert.True(serverSync.RealtimePublisherStatus.Enabled);
                Label diagnostics = Find<Label>(serverSync, "realtimeDiagnosticsLabel");
                Assert.Contains("runtime-verified-profile.json", diagnostics.Text, StringComparison.Ordinal);
                Assert.Contains("Verified mappings: 1", diagnostics.Text, StringComparison.Ordinal);
                Assert.Contains("Vehicle ID: 171804", diagnostics.Text, StringComparison.Ordinal);
                Assert.Contains("Decoded states: 1", diagnostics.Text, StringComparison.Ordinal);
                Assert.Equal(2, rawGrid.Rows.Count);
                DataGridViewRow completingRow = rawGrid.Rows[1];
                Assert.Equal("FF", Convert.ToString(completingRow.Cells["bytesColumn"].Value));
                string interpretation = Convert.ToString(
                    completingRow.Cells["interpretationColumn"].Value) ?? string.Empty;
                Assert.Equal(
                    "VERIFIED: J1-A Throttle 1=ACTIVE [12] | Payload: 0C",
                    interpretation);
                Assert.StartsWith("VERIFIED:", interpretation, StringComparison.Ordinal);
                Assert.Contains("J1-A", interpretation, StringComparison.Ordinal);
                Assert.Contains("Throttle 1", interpretation, StringComparison.Ordinal);
                Assert.Contains("ACTIVE", interpretation, StringComparison.Ordinal);
                Assert.Contains("[12]", interpretation, StringComparison.Ordinal);
                Assert.Equal(
                    "COMPLETE LIVE FRAME | Payload: 0C | VERIFIED: J1-A Throttle 1=ACTIVE [12]",
                    completingRow.Cells["interpretationColumn"].ToolTipText);

                stage = "injecting repeated split frame with trailing D2 bytes";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFB, 0xFB, 0x0C });
                Assert.Equal("PARTIAL LIVE FRAME — waiting for FF",
                    Convert.ToString(rawGrid.Rows[2].Cells["interpretationColumn"].Value));
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFF, 0xD2, 0x07 });
                stage = "waiting for deduplication";
                WaitUntilWithMessagePump(() =>
                    serverSync.RealtimePublisherStatus.LastResult.StartsWith("UNCHANGED", StringComparison.Ordinal));
                Assert.Equal(2, decodedNotifications);
                Assert.Equal(2, posted.Count);
                Assert.Equal(2, GetPrivateLong(form, "_realtimeFramesReceived"));
                Assert.Equal(2, GetPrivateLong(form, "_realtimeFramesDecoded"));
                string unchangedInterpretation = Convert.ToString(
                    rawGrid.Rows[3].Cells["interpretationColumn"].Value) ?? string.Empty;
                Assert.Equal(
                    "VERIFIED: J1-A Throttle 1=ACTIVE [12] (UNCHANGED) | Payload: 0C | " +
                    "Trailing: D2 07 (UNKNOWN)",
                    unchangedInterpretation);
                Assert.StartsWith("VERIFIED:", unchangedInterpretation, StringComparison.Ordinal);
                Assert.Contains(
                    "TRAILING RX AFTER FF: D2 07 — OUTSIDE LIVE FRAME; meaning UNKNOWN / UNMAPPED",
                    rawGrid.Rows[3].Cells["interpretationColumn"].ToolTipText,
                    StringComparison.Ordinal);

                stage = "injecting complete frame with unmapped payload positions";
                InjectTransportBytesThroughRealLiveService(
                    live,
                    new byte[] { 0xFB, 0xFB, 0x0C, 0x00, 0x0C, 0x00, 0x0C, 0xFF });
                WaitUntilWithMessagePump(() =>
                    serverSync.RealtimePublisherStatus.LastResult.StartsWith("UNCHANGED", StringComparison.Ordinal));
                PumpMessagesFor(TimeSpan.FromMilliseconds(100));
                Assert.Equal(2, posted.Count);
                string unmappedInterpretation = Convert.ToString(
                    rawGrid.Rows[4].Cells["interpretationColumn"].Value) ?? string.Empty;
                Assert.StartsWith("VERIFIED:", unmappedInterpretation, StringComparison.Ordinal);
                Assert.Contains("Payload: 0C 00 0C 00 0C", unmappedInterpretation, StringComparison.Ordinal);
                Assert.Contains("VERIFIED: J1-A Throttle 1=ACTIVE [12] (UNCHANGED)",
                    unmappedInterpretation, StringComparison.Ordinal);
                Assert.DoesNotContain("Unmapped payload bytes", unmappedInterpretation, StringComparison.Ordinal);
                Assert.DoesNotContain("Forward", unmappedInterpretation, StringComparison.OrdinalIgnoreCase);

                stage = "injecting changed inactive frame";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFB, 0xFB, 0x00, 0xFF });
                WaitUntilWithMessagePump(() => posted.Count == 3);
                Assert.Equal(sessionStart.SourceConnectionId, posted.Last().Update.SourceConnectionId);
                string inactiveInterpretation = Convert.ToString(
                    rawGrid.Rows[5].Cells["interpretationColumn"].Value) ?? string.Empty;
                Assert.Equal(
                    "VERIFIED: J1-A Throttle 1=INACTIVE [0] | Payload: 00",
                    inactiveInterpretation);
                Assert.StartsWith("VERIFIED:", inactiveInterpretation, StringComparison.Ordinal);

                stage = "injecting pure outside-frame D2 bytes";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xD2, 0x28 });
                Assert.Equal(
                    "OUTSIDE LIVE FRAME: D2 28 — UNKNOWN / UNMAPPED",
                    Convert.ToString(rawGrid.Rows[6].Cells["interpretationColumn"].Value));
                Assert.Equal(3, posted.Count);

                stage = "loading unverified profile";
                Task loadUnverifiedProfile = bench.LoadRcmProfileAsync(unverifiedProfilePath);
                WaitUntilWithMessagePump(() => loadUnverifiedProfile.IsCompleted);
                loadUnverifiedProfile.GetAwaiter().GetResult();
                stage = "injecting frame with unverified mapping";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFB, 0xFB, 0x0C, 0xFF });
                Application.DoEvents();
                Assert.Equal(3, posted.Count);
                Assert.Empty(rcmLive.GetDecodedSignalSnapshot());
                string unverifiedInterpretation = Convert.ToString(
                    rawGrid.Rows[7].Cells["interpretationColumn"].Value) ?? string.Empty;
                Assert.Contains("COMPLETE LIVE FRAME", unverifiedInterpretation, StringComparison.Ordinal);
                Assert.Contains("Payload: 0C", unverifiedInterpretation, StringComparison.Ordinal);
                Assert.Contains("No explicitly VERIFIED signal mapping", unverifiedInterpretation, StringComparison.Ordinal);
                Assert.Contains("Unmapped payload bytes: 1", unverifiedInterpretation, StringComparison.Ordinal);
                Assert.DoesNotContain("Throttle", unverifiedInterpretation, StringComparison.OrdinalIgnoreCase);

                stage = "injecting malformed candidate followed by a valid frame";
                InjectTransportBytesThroughRealLiveService(
                    live,
                    new byte[] { 0xFB, 0xFB, 0x01, 0xFB, 0xFB, 0x0C, 0xFF });
                PumpMessagesFor(TimeSpan.FromMilliseconds(100));
                Assert.Equal(3, posted.Count);
                string malformedInterpretation = Convert.ToString(
                    rawGrid.Rows[8].Cells["interpretationColumn"].Value) ?? string.Empty;
                Assert.Contains("MALFORMED LIVE FRAME CANDIDATE", malformedInterpretation, StringComparison.Ordinal);
                Assert.Contains("COMPLETE LIVE FRAME", malformedInterpretation, StringComparison.Ordinal);

                Assert.Equal(
                    new[]
                    {
                        "FB FB 0C",
                        "FF",
                        "FB FB 0C",
                        "FF D2 07",
                        "FB FB 0C 00 0C 00 0C FF",
                        "FB FB 00 FF",
                        "D2 28",
                        "FB FB 0C FF",
                        "FB FB 01 FB FB 0C FF"
                    },
                    rawGrid.Rows.Cast<DataGridViewRow>()
                        .Select(row => Convert.ToString(row.Cells["bytesColumn"].Value))
                        .ToArray());
                stage = "disposing MainForm";
            }
            finally
            {
                if (File.Exists(profilePath)) File.Delete(profilePath);
                if (File.Exists(unverifiedProfilePath)) File.Delete(unverifiedProfilePath);
                if (Directory.Exists(folder)) Directory.Delete(folder);
            }
        }, () => stage);
    }

    [Fact]
    public void MainFormRoutesAllSignalsFromOneSplitFrameAndPublishesOnlyPerSignalChanges()
    {
        string stage = "starting STA thread";
        RunInStaThread(() =>
        {
            string folder = Path.Combine(Path.GetTempPath(), $"otmr-realtime-multi-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            string profilePath = Path.Combine(folder, "runtime-three-verified-signals.json");
            try
            {
                stage = "creating three-signal verified profile";
                RcmPinProfile throttle1 = RcmVerifiedLiveDecoderTests.EventPin(
                    "A", "Throttle 1", removed: 0x00, applied: 0x0C);
                RcmPinProfile throttle2 = RcmVerifiedLiveDecoderTests.EventPin(
                    "B", "Throttle 2", removed: 0x01, applied: 0x0D);
                RcmPinProfile forward = RcmVerifiedLiveDecoderTests.EventPin(
                    "D", "Forward", removed: 0x03, applied: 0x0F);
                throttle1.CcfReference!.LogicalChannel = 0;
                throttle1.DecoderVerification.ExpectedCcf.LogicalChannel = 0;
                throttle2.CcfReference!.LogicalChannel = 1;
                throttle2.DecoderVerification.ExpectedCcf.LogicalChannel = 1;
                forward.CcfReference!.LogicalChannel = 3;
                forward.DecoderVerification.ExpectedCcf.LogicalChannel = 3;
                foreach (RcmPinProfile pin in new[] { throttle1, throttle2, forward })
                    MakePersistableVerified(pin);
                RcmProfileJson.SaveAsync(
                    profilePath,
                    RcmVerifiedLiveDecoderTests.Profile(throttle1, throttle2, forward),
                    DateTimeOffset.UtcNow).GetAwaiter().GetResult();

                var posted = new ConcurrentQueue<(Uri Uri, OtmrRealtimeUpdateRequest Update)>();
                using var http = new HttpClient(new DelegateHandler(async (request, cancellationToken) =>
                {
                    OtmrRealtimeUpdateRequest update = JsonSerializer.Deserialize<OtmrRealtimeUpdateRequest>(
                        await request.Content!.ReadAsStringAsync(cancellationToken),
                        OtmrApiV1Json.Options)!;
                    posted.Enqueue((request.RequestUri!, update));
                    return JsonResponse(new OtmrRealtimeUpdateAcknowledgement(
                        OtmrApiContract.Version,
                        update.VehicleIdentifier,
                        true,
                        update.TimestampUtc,
                        update.Signals.Count));
                }));
                var publisher = new OtmrRealtimePublisher(http);
                using var form = new MainForm();
                InvokeLoadCcf(form, FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf"));
                OtmrBenchControl bench = Find<OtmrBenchControl>(form, "otmrBenchControl");
                OtmrLiveControl live = Find<OtmrLiveControl>(form, "otmrLiveControl");
                OtmrRcmLiveControl rcmLive = Find<OtmrRcmLiveControl>(form, "otmrRcmLiveControl");
                OtmrServerSyncControl serverSync = Find<OtmrServerSyncControl>(form, "otmrServerSyncControl");
                Task loadProfile = bench.LoadRcmProfileAsync(profilePath);
                WaitUntilWithMessagePump(() => loadProfile.IsCompleted);
                loadProfile.GetAwaiter().GetResult();
                serverSync.ConfigureRealtimePublisherForTests(publisher, Configuration(enabled: true));
                OtmrLiveService liveService = GetPrivateField<OtmrLiveService>(live, "_liveService");
                InvokePrivate(liveService, "SetState", OtmrLiveState.LiveReady);
                WaitUntilWithMessagePump(() => posted.Count == 1);
                string sourceConnectionId = posted.Single().Update.SourceConnectionId!;

                var decodedEvents = new List<VerifiedLiveStateDecodedEventArgs>();
                rcmLive.VerifiedLiveStateDecoded += (_, e) => decodedEvents.Add(e);
                DataGridView rawGrid = Find<DataGridView>(live, "captureGrid");

                stage = "injecting initial three-signal frame in three chunks";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFB, 0xFB, 0x0C });
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0x01 });
                Assert.Empty(decodedEvents);
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0x0F, 0xFF, 0xD2, 0x02 });
                WaitUntilWithMessagePump(() => posted.Count == 2);

                VerifiedLiveStateDecodedEventArgs initial = Assert.Single(decodedEvents);
                Assert.Equal(3, initial.Signals.Count);
                Assert.Equal(1, GetPrivateLong(form, "_realtimeFramesReceived"));
                Assert.Equal(1, GetPrivateLong(form, "_realtimeFramesDecoded"));
                Assert.Equal(
                    new[] { "A:Active:12", "B:Inactive:1", "D:Active:15" },
                    initial.Signals.Select(signal =>
                        $"{signal.Pin}:{signal.State}:{signal.RawObservedValue}").ToArray());

                DataGridView decodedGrid = Find<DataGridView>(rcmLive, "decodedSignalsGrid");
                Assert.Equal(3, decodedGrid.Rows.Count);
                Assert.Equal(
                    new[] { "ACTIVE", "INACTIVE", "ACTIVE" },
                    decodedGrid.Rows.Cast<DataGridViewRow>()
                        .Select(row => Convert.ToString(row.Cells["decodedStateColumn"].Value))
                        .ToArray());

                OtmrRealtimeUpdateRequest initialPost = posted.Last().Update;
                Assert.False(initialPost.IsSessionStart);
                Assert.Equal(sourceConnectionId, initialPost.SourceConnectionId);
                Assert.Equal(3, initialPost.Signals.Count);
                Assert.Equal(initial.Signals.Select(signal => signal.PinId).Order(),
                    initialPost.Signals.Select(signal => signal.SignalId).Order());
                Assert.Equal(
                    new[] { "A:ACTIVE:12", "B:INACTIVE:1", "D:ACTIVE:15" },
                    initialPost.Signals.Select(signal =>
                        $"{signal.Pin}:{signal.State}:{signal.RawValue}").ToArray());

                DataGridViewRow terminatingRow = rawGrid.Rows[2];
                Assert.Equal("0F FF D2 02", Convert.ToString(terminatingRow.Cells["bytesColumn"].Value));
                string initialInterpretation = Convert.ToString(
                    terminatingRow.Cells["interpretationColumn"].Value) ?? string.Empty;
                Assert.StartsWith("VERIFIED:", initialInterpretation, StringComparison.Ordinal);
                Assert.Contains("J1-A Throttle 1=ACTIVE [12]", initialInterpretation, StringComparison.Ordinal);
                Assert.Contains("J1-B Throttle 2=INACTIVE [1]", initialInterpretation, StringComparison.Ordinal);
                Assert.Contains("J1-D Forward=ACTIVE [15]", initialInterpretation, StringComparison.Ordinal);
                Assert.Contains("Trailing: D2 02 (UNKNOWN)", initialInterpretation, StringComparison.Ordinal);
                Assert.DoesNotContain("Unmapped payload bytes", initialInterpretation, StringComparison.Ordinal);

                stage = "injecting mixed unchanged and changed frame";
                InjectTransportBytesThroughRealLiveService(
                    live,
                    new byte[] { 0xFB, 0xFB, 0x0C, 0x0D, 0x03, 0xFF });
                WaitUntilWithMessagePump(() => posted.Count == 3);
                Assert.Equal(2, decodedEvents.Count);
                VerifiedLiveStateDecodedEventArgs mixed = decodedEvents[1];
                Assert.Equal(3, mixed.Signals.Count);

                string mixedInterpretation = Convert.ToString(
                    rawGrid.Rows[3].Cells["interpretationColumn"].Value) ?? string.Empty;
                Assert.Contains("J1-A Throttle 1=ACTIVE [12] (UNCHANGED)", mixedInterpretation,
                    StringComparison.Ordinal);
                Assert.Contains("J1-B Throttle 2=ACTIVE [13]", mixedInterpretation,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("J1-B Throttle 2=ACTIVE [13] (UNCHANGED)", mixedInterpretation,
                    StringComparison.Ordinal);
                Assert.Contains("J1-D Forward=INACTIVE [3]", mixedInterpretation,
                    StringComparison.Ordinal);

                OtmrRealtimeUpdateRequest mixedPost = posted.Last().Update;
                Assert.Equal(sourceConnectionId, mixedPost.SourceConnectionId);
                Assert.Equal(2, mixedPost.Signals.Count);
                Assert.Equal(
                    new[] { "B:ACTIVE:13", "D:INACTIVE:3" },
                    mixedPost.Signals.Select(signal =>
                        $"{signal.Pin}:{signal.State}:{signal.RawValue}").ToArray());

                stage = "injecting one-signal frame after multi-signal frame";
                InjectTransportBytesThroughRealLiveService(
                    live,
                    new byte[] { 0xFB, 0xFB, 0x00, 0xFF });
                WaitUntilWithMessagePump(() => posted.Count == 4);
                Assert.Equal(3, decodedEvents.Count);
                Assert.Single(decodedEvents[2].Signals);
                Assert.Equal(3, decodedGrid.Rows.Count);
                Assert.Equal(
                    new[] { "J1-A:INACTIVE", "J1-B:ACTIVE", "J1-D:INACTIVE" },
                    decodedGrid.Rows.Cast<DataGridViewRow>()
                        .Select(row =>
                            $"{row.Cells["decodedPhysicalColumn"].Value}:" +
                            $"{row.Cells["decodedStateColumn"].Value}")
                        .ToArray());
                OtmrRealtimeSignalUpdate lastUpdate = Assert.Single(posted.Last().Update.Signals);
                Assert.Equal("A", lastUpdate.Pin);
                Assert.Equal(OtmrRealtimeContract.Inactive, lastUpdate.State);
                Label sessionStatus = Find<Label>(rcmLive, "decodedSignalsStatusLabel");
                Assert.Contains("Observed this session: 3", sessionStatus.Text, StringComparison.Ordinal);
                Assert.Contains("Latest frame decoded: 1", sessionStatus.Text, StringComparison.Ordinal);
                Assert.Equal(3, GetPrivateLong(form, "_realtimeFramesReceived"));
                Assert.Equal(3, GetPrivateLong(form, "_realtimeFramesDecoded"));

                stage = "starting a new real live source connection";
                InvokePrivate(liveService, "SetState", OtmrLiveState.WaitingForLiveFrames);
                Application.DoEvents();
                Assert.Empty(decodedGrid.Rows.Cast<DataGridViewRow>());
                Assert.Contains("Observed this session: 0", sessionStatus.Text, StringComparison.Ordinal);
                InvokePrivate(liveService, "SetState", OtmrLiveState.LiveReady);
                WaitUntilWithMessagePump(() => posted.Count == 5);
                OtmrRealtimeUpdateRequest connectionBStart = posted.Last().Update;
                Assert.True(connectionBStart.IsSessionStart);
                Assert.NotEqual(sourceConnectionId, connectionBStart.SourceConnectionId);
                Assert.Empty(connectionBStart.Signals);
                Assert.Empty(decodedGrid.Rows.Cast<DataGridViewRow>());

                stage = "observing only Throttle 2 in the new connection";
                InjectTransportBytesThroughRealLiveService(
                    live,
                    new byte[] { 0xFB, 0xFB, 0x01, 0xFF });
                WaitUntilWithMessagePump(() => posted.Count == 6);
                DataGridViewRow onlyConnectionBRow = Assert.Single(
                    decodedGrid.Rows.Cast<DataGridViewRow>());
                Assert.Equal("J1-B", onlyConnectionBRow.Cells["decodedPhysicalColumn"].Value);
                Assert.Equal("INACTIVE", onlyConnectionBRow.Cells["decodedStateColumn"].Value);
                Assert.Equal(connectionBStart.SourceConnectionId, posted.Last().Update.SourceConnectionId);
                Assert.Equal(4, GetPrivateLong(form, "_realtimeFramesReceived"));
                Assert.Equal(4, GetPrivateLong(form, "_realtimeFramesDecoded"));
            }
            finally
            {
                if (File.Exists(profilePath)) File.Delete(profilePath);
                if (Directory.Exists(folder)) Directory.Delete(folder);
            }
        }, () => stage);
    }

    [Fact]
    public async Task BackpressureCoalescesPendingFramesPerSignalWithoutDroppingSiblingChanges()
    {
        RcmPinProfile throttle1 = RcmVerifiedLiveDecoderTests.EventPin(
            "A", "Throttle 1", removed: 0x00, applied: 0x0C);
        RcmPinProfile throttle2 = RcmVerifiedLiveDecoderTests.EventPin(
            "B", "Throttle 2", removed: 0x01, applied: 0x0D);
        RcmPinProfile forward = RcmVerifiedLiveDecoderTests.EventPin(
            "D", "Forward", removed: 0x03, applied: 0x0F);
        RcmProfile profile = RcmVerifiedLiveDecoderTests.Profile(throttle1, throttle2, forward);
        var decoder = new RcmVerifiedLiveDecoder();
        string connectionId = Guid.NewGuid().ToString("D");
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new ConcurrentQueue<OtmrRealtimeUpdateRequest>();
        int callCount = 0;
        var client = new DelegateRealtimeClient(async (update, cancellationToken) =>
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
            sent.Enqueue(update);
            return new OtmrRealtimeUpdateAcknowledgement(
                OtmrApiContract.Version,
                update.VehicleIdentifier,
                true,
                update.TimestampUtc,
                update.Signals.Count);
        });
        using var http = new HttpClient(new DelegateHandler((_, _) =>
            throw new InvalidOperationException("The injected realtime client should be used.")));
        await using var publisher = new OtmrRealtimePublisher(http, _ => client);
        publisher.Configure(Configuration(enabled: true));

        OtmrRealtimeDecodedState State(params byte[] records)
        {
            byte[] frameBytes = new byte[records.Length + 3];
            frameBytes[0] = 0xFB;
            frameBytes[1] = 0xFB;
            records.CopyTo(frameBytes, 2);
            frameBytes[^1] = 0xFF;
            OtmrLiveFrame frame = Assert.Single(new OtmrLiveFrameAssembler().Append(frameBytes));
            return new OtmrRealtimeDecodedState(
                "987654",
                DateTimeOffset.UtcNow,
                connectionId,
                "three-signals.json",
                new string('E', 64),
                decoder.Decode(frame, profile));
        }

        Assert.True(publisher.TryPublish(State(0x00)));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // These two calls occur while the first HTTP request is blocked. The
        // second pending frame must augment, not evict, B and D from the first.
        Assert.True(publisher.TryPublish(State(0x0D, 0x03)));
        Assert.True(publisher.TryPublish(State(0x0C)));
        releaseFirst.TrySetResult();
        await WaitUntilAsync(() => sent.Count == 2);

        OtmrRealtimeUpdateRequest coalesced = sent.Last();
        Assert.False(coalesced.IsSessionStart);
        Assert.Equal(connectionId, coalesced.SourceConnectionId);
        Assert.Equal(3, coalesced.Signals.Count);
        Assert.Equal(
            new[] { "A:ACTIVE:12", "B:ACTIVE:13", "D:INACTIVE:3" },
            coalesced.Signals
                .OrderBy(signal => signal.Pin)
                .Select(signal => $"{signal.Pin}:{signal.State}:{signal.RawValue}")
                .ToArray());
    }

    [Fact]
    public async Task GenuineVerifiedChangesPublishOnceAndUnchangedFramesAreDeduplicated()
    {
        var sent = new ConcurrentQueue<OtmrRealtimeUpdateRequest>();
        await using var publisher = Publisher(sent);
        publisher.Configure(Configuration(enabled: true));

        Assert.True(publisher.TryPublish(Decoded("987654", value: 1)));
        await WaitUntilAsync(() => sent.Count == 1);
        OtmrRealtimeUpdateRequest first = Assert.Single(sent);
        OtmrRealtimeSignalUpdate firstSignal = Assert.Single(first.Signals);
        Assert.Equal("987654", first.VehicleIdentifier);
        Assert.NotEqual("171804", first.VehicleIdentifier);
        Assert.True(first.IsSessionStart);
        Assert.Equal("connection-test", first.SourceConnectionId);
        Assert.Equal(OtmrRealtimeContract.Active, firstSignal.State);
        Assert.Equal(1, firstSignal.RawValue);
        Assert.Equal(OtmrRealtimeContract.Verified, firstSignal.Verification);

        Assert.True(publisher.TryPublish(Decoded("987654", value: 1)));
        await WaitUntilAsync(() => publisher.Status.LastResult.StartsWith("UNCHANGED", StringComparison.Ordinal));
        Assert.Single(sent);

        Assert.True(publisher.TryPublish(Decoded("987654", value: 0)));
        await WaitUntilAsync(() => sent.Count == 2);
        OtmrRealtimeUpdateRequest changedRequest = sent.Last();
        Assert.False(changedRequest.IsSessionStart);
        Assert.Equal(first.SourceConnectionId, changedRequest.SourceConnectionId);
        OtmrRealtimeSignalUpdate changed = Assert.Single(changedRequest.Signals);
        Assert.Equal(OtmrRealtimeContract.Inactive, changed.State);
        Assert.Equal(0, changed.RawValue);
    }

    [Fact]
    public async Task IdenticalSignalInNewConnectionIsSentBecauseDeduplicationIsConnectionScoped()
    {
        var sent = new ConcurrentQueue<OtmrRealtimeUpdateRequest>();
        await using var publisher = Publisher(sent);
        publisher.Configure(Configuration(enabled: true));
        string connectionA = Guid.NewGuid().ToString("D");
        string connectionB = Guid.NewGuid().ToString("D");

        Assert.True(publisher.TryStartSession(new(
            "987654", DateTimeOffset.UtcNow, connectionA)));
        await WaitUntilAsync(() => sent.Count == 1);
        Assert.True(publisher.TryPublish(
            Decoded("987654", value: 1) with { SourceConnectionId = connectionA }));
        await WaitUntilAsync(() => sent.Count == 2);

        Assert.True(publisher.TryStartSession(new(
            "987654", DateTimeOffset.UtcNow.AddSeconds(1), connectionB)));
        await WaitUntilAsync(() => sent.Count == 3);
        Assert.True(publisher.TryPublish(
            Decoded("987654", value: 1) with { SourceConnectionId = connectionB }));
        await WaitUntilAsync(() => sent.Count == 4);

        OtmrRealtimeUpdateRequest[] requests = sent.ToArray();
        Assert.True(requests[0].IsSessionStart);
        Assert.Empty(requests[0].Signals);
        Assert.Equal(connectionA, requests[0].SourceConnectionId);
        Assert.False(requests[1].IsSessionStart);
        Assert.Equal(connectionA, requests[1].SourceConnectionId);
        Assert.True(requests[2].IsSessionStart);
        Assert.Empty(requests[2].Signals);
        Assert.Equal(connectionB, requests[2].SourceConnectionId);
        Assert.False(requests[3].IsSessionStart);
        Assert.Equal(connectionB, requests[3].SourceConnectionId);
        Assert.Equal(OtmrRealtimeContract.Active, Assert.Single(requests[3].Signals).State);
    }

    [Fact]
    public async Task CandidateConflictUnknownAndFabricatedValuesAreNeverPublished()
    {
        RcmPinProfile verified = RcmVerifiedLiveDecoderTests.VerifiedPin("A", 3, 0, 0, 1);
        RcmPinProfile candidate = RcmVerifiedLiveDecoderTests.VerifiedPin("B", 4, 0, 0, 1);
        candidate.DecoderVerification.Status = RcmVerificationStates.CandidateFound;
        RcmPinProfile conflict = RcmVerifiedLiveDecoderTests.VerifiedPin("C", 5, 0, 0, 1);
        conflict.DecoderVerification.Status = RcmVerificationStates.Conflict;
        RcmProfile profile = RcmVerifiedLiveDecoderTests.Profile(verified, candidate, conflict);
        OtmrLiveFrame frame = Assert.Single(new OtmrLiveFrameAssembler().Append(
            new byte[] { 0xFB, 0xFB, 0x38, 0x01, 0x01, 0x01, 0xFF }));
        IReadOnlyList<RcmVerifiedLiveSignal> decoded = new RcmVerifiedLiveDecoder().Decode(frame, profile);
        RcmVerifiedLiveSignal genuine = Assert.Single(decoded);

        var unsafeSignals = decoded.Concat(new[]
        {
            new RcmVerifiedLiveSignal
            {
                PinId = Guid.NewGuid(), Connector = "J9", Pin = "X", Function = "Candidate",
                State = RcmDecodedElectricalState.Active, RawObservedValue = 255,
                VerificationStatus = RcmVerificationStates.CandidateFound
            },
            new RcmVerifiedLiveSignal
            {
                PinId = Guid.NewGuid(), Connector = "J9", Pin = "Y", Function = "Unknown",
                State = RcmDecodedElectricalState.Unknown, RawObservedValue = 123,
                VerificationStatus = RcmVerificationStates.Verified
            }
        }).ToArray();

        var sent = new ConcurrentQueue<OtmrRealtimeUpdateRequest>();
        await using var publisher = Publisher(sent);
        publisher.Configure(Configuration(enabled: true));
        Assert.True(publisher.TryPublish(new(
            "555001", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"),
            "verified.json", new string('A', 64), unsafeSignals)));
        await WaitUntilAsync(() => sent.Count == 1);

        OtmrRealtimeSignalUpdate update = Assert.Single(Assert.Single(sent).Signals);
        Assert.Equal(genuine.PinId, update.SignalId);
        Assert.Equal(genuine.RawObservedValue, update.RawValue);
        Assert.Equal(OtmrRealtimeContract.Verified, update.Verification);
    }

    [Fact]
    public async Task HttpFailureIsIsolatedAndGenuineDecodingContinues()
    {
        var client = new DelegateRealtimeClient((_, _) =>
            Task.FromException<OtmrRealtimeUpdateAcknowledgement>(new HttpRequestException("server unavailable")));
        using var http = new HttpClient(new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        await using var publisher = new OtmrRealtimePublisher(http, _ => client);
        publisher.Configure(Configuration(enabled: true));

        Assert.True(publisher.TryPublish(Decoded("600100", value: 1)));
        await WaitUntilAsync(() => publisher.Status.LastResult == "SEND FAILED");
        Assert.Contains("server unavailable", publisher.Status.LastError, StringComparison.Ordinal);

        OtmrRealtimeDecodedState laterFrame = Decoded("600100", value: 0);
        RcmVerifiedLiveSignal decodedAfterFailure = Assert.Single(laterFrame.Signals);
        Assert.Equal(RcmDecodedElectricalState.Inactive, decodedAfterFailure.State);
        Assert.True(publisher.TryPublish(laterFrame));
    }

    [Fact]
    public async Task HttpClientReusesSavedBearerTokenAndSerializesExactDecodedValue()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var http = new HttpClient(new DelegateHandler(async (request, cancellationToken) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            OtmrRealtimeUpdateRequest update = JsonSerializer.Deserialize<OtmrRealtimeUpdateRequest>(body, OtmrApiV1Json.Options)!;
            return JsonResponse(new OtmrRealtimeUpdateAcknowledgement(
                OtmrApiContract.Version, update.VehicleIdentifier, true, update.TimestampUtc, update.Signals.Count));
        }));
        var settings = new OtmrSyncUserSettings
        {
            ServerUrl = "https://otmr.example.test/",
            ApiToken = "same-secure-token",
            RealtimePublishingEnabled = true
        };
        await using var publisher = new OtmrRealtimePublisher(http);
        publisher.Configure(settings.ToRealtimeConfiguration());

        Assert.True(publisher.TryPublish(Decoded("700321", value: 1)));
        await WaitUntilAsync(() => publisher.Status.LastResult.StartsWith("SENT", StringComparison.Ordinal));

        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("same-secure-token", captured.Headers.Authorization.Parameter);
        Assert.Contains("/api/v1/otmr/vehicles/700321/live", captured.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(body!);
        Assert.Equal("700321", json.RootElement.GetProperty("vehicleIdentifier").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("signals")[0].GetProperty("rawValue").GetInt32());
        Assert.False(body!.Contains("RCM JSON", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RealtimeUsesProductionHttpsAndOnlyTheExactOptedInHttpTestServer()
    {
        Assert.Throws<InvalidOperationException>(() => new OtmrRealtimePublisherConfiguration
        {
            Enabled = true, ServerUrl = "http://example.com/", ApiToken = "token"
        }.CreateClientOptions());

        OtmrSyncOptions exact = new OtmrRealtimePublisherConfiguration
        {
            Enabled = true,
            ServerUrl = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer,
            ApiToken = "token",
            AllowInsecureKnownTestServer = true
        }.CreateClientOptions();
        Assert.True(exact.AllowInsecureKnownTestServer);

        Assert.Throws<InvalidOperationException>(() => new OtmrRealtimePublisherConfiguration
        {
            Enabled = true,
            ServerUrl = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer + "other/",
            ApiToken = "token",
            AllowInsecureKnownTestServer = true
        }.CreateClientOptions());
    }

    private static OtmrRealtimePublisher Publisher(ConcurrentQueue<OtmrRealtimeUpdateRequest> sent)
    {
        var client = new DelegateRealtimeClient((update, _) =>
        {
            sent.Enqueue(update);
            return Task.FromResult(new OtmrRealtimeUpdateAcknowledgement(
                OtmrApiContract.Version, update.VehicleIdentifier, true,
                update.TimestampUtc, update.Signals.Count));
        });
        var http = new HttpClient(new DelegateHandler((_, _) =>
            throw new InvalidOperationException("The injected realtime client should be used.")));
        return new OtmrRealtimePublisher(http, _ => client);
    }

    private static OtmrRealtimePublisherConfiguration Configuration(bool enabled) => new()
    {
        Enabled = enabled,
        ServerUrl = "https://otmr.example.test/",
        ApiToken = "publisher-test-token",
        RequestTimeout = TimeSpan.FromSeconds(2)
    };

    private static OtmrRealtimeDecodedState Decoded(string vehicleIdentifier, byte value)
    {
        RcmPinProfile pin = RcmVerifiedLiveDecoderTests.VerifiedPin("A", 3, 0, 0, 1);
        pin.Id = Guid.Parse("A5F083B8-A93F-4D2B-874B-2CA0C8E04391");
        RcmProfile profile = RcmVerifiedLiveDecoderTests.Profile(pin);
        OtmrLiveFrame frame = Assert.Single(new OtmrLiveFrameAssembler().Append(
            new byte[] { 0xFB, 0xFB, 0x38, value, 0xFF }));
        IReadOnlyList<RcmVerifiedLiveSignal> signals = new RcmVerifiedLiveDecoder().Decode(frame, profile);
        return new(vehicleIdentifier, DateTimeOffset.UtcNow, "connection-test",
            "verified-profile.json", new string('B', 64), signals);
    }

    private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, OtmrApiV1Json.Options))
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition(), "Timed out waiting for the realtime publisher worker.");
    }

    private static void InjectTransportBytesThroughRealLiveService(OtmrLiveControl live, byte[] bytes)
    {
        object service = live.GetType().GetField("_liveService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(live)!;
        MethodInfo receive = service.GetType().GetMethod(
            "Transport_BytesReceived", BindingFlags.Instance | BindingFlags.NonPublic)!;
        receive.Invoke(service, new object?[] { null, new OtmrBytesReceivedEventArgs(bytes) });
    }

    private static void InvokeLoadCcf(MainForm form, string path) =>
        typeof(MainForm).GetMethod("LoadCcf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { path });

    private static long GetPrivateLong(object target, string name) =>
        Assert.IsType<long>(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target));

    private static T GetPrivateField<T>(object target, string name) where T : class =>
        Assert.IsType<T>(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target));

    private static void InvokePrivate(object target, string name, params object?[] arguments) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, arguments);

    private static T Find<T>(Control root, string name) where T : Control =>
        root.Controls.Find(name, true).OfType<T>().SingleOrDefault()
        ?? throw new Xunit.Sdk.XunitException($"Control '{name}' was not found.");

    private static string FindFromRoot(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new Xunit.Sdk.XunitException($"Fixture not found: {Path.Combine(parts)}");
    }

    private static void WaitUntilWithMessagePump(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.True(condition(), "Timed out waiting for the MainForm realtime route.");
    }

    private static void PumpMessagesFor(TimeSpan duration)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(duration);
        while (DateTimeOffset.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static void MakePersistableVerified(RcmPinProfile pin)
    {
        RcmObservedTransition observed = pin.DecoderVerification.ObservedMapping!;
        pin.VerificationRuns.Clear();
        for (int runNumber = 1; runNumber <= 3; runNumber++)
        {
            var run = new RcmPhysicalVerificationRun
            {
                RunNumber = runNumber,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(runNumber),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(runNumber).AddSeconds(6),
                Connector = pin.Connector,
                Pin = pin.Pin,
                Function = pin.Function,
                ExpectedCcf = new RcmExpectedMappingSnapshot
                {
                    LogicalCard = pin.CcfReference!.LogicalCard,
                    LogicalChannel = pin.CcfReference.LogicalChannel,
                    RecordA = pin.CcfReference.RecordA,
                    RecordB = pin.CcfReference.RecordB
                },
                CandidateTransitions = new List<RcmObservedTransition>
                {
                    new()
                    {
                        RawPosition = observed.RawPosition,
                        Bit = observed.Bit,
                        RemovedValue = observed.RemovedValue,
                        AppliedValue = observed.AppliedValue,
                        TransitionPolarity = observed.TransitionPolarity
                    }
                }
            };
            pin.VerificationRuns.Add(run);
        }
        pin.DecoderVerification.RequiredRunCount = 3;
        pin.DecoderVerification.SuccessfulRepetitionCount = 3;
        pin.DecoderVerification.QualifyingRunIds = pin.VerificationRuns.Select(run => run.RunId).ToList();
        pin.Comparison.DecoderVerified = true;
    }

    private static void RunInStaThread(Action action, Func<string>? describeStage = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)),
            $"The realtime UI source test timed out while {describeStage?.Invoke() ?? "running"}.");
        if (failure is not null)
            throw new AggregateException(failure);
    }

    private sealed class DelegateRealtimeClient(
        Func<OtmrRealtimeUpdateRequest, CancellationToken, Task<OtmrRealtimeUpdateAcknowledgement>> callback)
        : IOtmrRealtimeClient
    {
        public Task<OtmrRealtimeUpdateAcknowledgement> PublishAsync(
            OtmrRealtimeUpdateRequest update,
            CancellationToken cancellationToken = default) => callback(update, cancellationToken);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => callback(request, cancellationToken);
    }
}
