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
        await Task.Delay(50);

        Assert.False(queued);
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
                RcmPinProfile pin = RcmVerifiedLiveDecoderTests.VerifiedPin("A", 3, bit: null, removed: 0, applied: 12);
                MakePersistableVerified(pin);
                RcmProfile profile = RcmVerifiedLiveDecoderTests.Profile(pin);
                RcmProfileJson.SaveAsync(profilePath, profile, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
                RcmPinProfile unverifiedPin = RcmVerifiedLiveDecoderTests.VerifiedPin(
                    "A", 3, bit: null, removed: 0, applied: 12);
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

                int decodedNotifications = 0;
                VerifiedLiveStateDecodedEventArgs? decoded = null;
                rcmLive.VerifiedLiveStateDecoded += (_, e) =>
                {
                    decodedNotifications++;
                    decoded = e;
                };

                DataGridView rawGrid = Find<DataGridView>(live, "captureGrid");
                stage = "injecting partial frame";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFB, 0xFB, 0x38, 0x0C });
                Assert.Empty(posted);
                Assert.Equal(0, decodedNotifications);
                DataGridViewRow partialRow = Assert.Single(rawGrid.Rows.Cast<DataGridViewRow>());
                Assert.Equal("FB FB 38 0C", Convert.ToString(partialRow.Cells["bytesColumn"].Value));
                Assert.Equal(string.Empty, Convert.ToString(partialRow.Cells["interpretationColumn"].Value));

                stage = "injecting terminating chunk";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFF });
                stage = "waiting for first POST";
                WaitUntilWithMessagePump(() => posted.Count == 1);

                Assert.Equal(1, decodedNotifications);
                RcmVerifiedLiveSignal decodedSignal = Assert.Single(Assert.IsType<VerifiedLiveStateDecodedEventArgs>(decoded).Signals);
                Assert.Equal(RcmDecodedElectricalState.Active, decodedSignal.State);
                Assert.Equal(RcmVerificationStates.Verified, decodedSignal.VerificationStatus);
                (Uri uri, OtmrRealtimeUpdateRequest update) = Assert.Single(posted);
                Assert.EndsWith("/api/v1/otmr/vehicles/171804/live", uri.AbsolutePath, StringComparison.Ordinal);
                Assert.Equal("171804", update.VehicleIdentifier);
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
                Assert.Equal("VERIFIED: J1-A Throttle 1=ACTIVE [12]", interpretation);
                Assert.Contains("J1-A", interpretation, StringComparison.Ordinal);
                Assert.Contains("Throttle 1", interpretation, StringComparison.Ordinal);
                Assert.Contains("ACTIVE", interpretation, StringComparison.Ordinal);
                Assert.Contains("[12]", interpretation, StringComparison.Ordinal);

                stage = "injecting repeated frame";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFB, 0xFB, 0x38, 0x0C, 0xFF });
                stage = "waiting for deduplication";
                WaitUntilWithMessagePump(() =>
                    serverSync.RealtimePublisherStatus.LastResult.StartsWith("UNCHANGED", StringComparison.Ordinal));
                Assert.Equal(2, decodedNotifications);
                Assert.Single(posted);
                Assert.Equal(2, GetPrivateLong(form, "_realtimeFramesReceived"));
                Assert.Equal(2, GetPrivateLong(form, "_realtimeFramesDecoded"));
                Assert.Equal(string.Empty, Convert.ToString(
                    rawGrid.Rows[2].Cells["interpretationColumn"].Value));

                stage = "loading unverified profile";
                Task loadUnverifiedProfile = bench.LoadRcmProfileAsync(unverifiedProfilePath);
                WaitUntilWithMessagePump(() => loadUnverifiedProfile.IsCompleted);
                loadUnverifiedProfile.GetAwaiter().GetResult();
                stage = "injecting frame with unverified mapping";
                InjectTransportBytesThroughRealLiveService(live, new byte[] { 0xFB, 0xFB, 0x38, 0x0C, 0xFF });
                Application.DoEvents();
                Assert.Single(posted);
                Assert.Empty(rcmLive.GetDecodedSignalSnapshot());
                Assert.Equal(string.Empty, Convert.ToString(
                    rawGrid.Rows[3].Cells["interpretationColumn"].Value));
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
        Assert.Equal(OtmrRealtimeContract.Active, firstSignal.State);
        Assert.Equal(1, firstSignal.RawValue);
        Assert.Equal(OtmrRealtimeContract.Verified, firstSignal.Verification);

        Assert.True(publisher.TryPublish(Decoded("987654", value: 1)));
        await WaitUntilAsync(() => publisher.Status.LastResult.StartsWith("UNCHANGED", StringComparison.Ordinal));
        Assert.Single(sent);

        Assert.True(publisher.TryPublish(Decoded("987654", value: 0)));
        await WaitUntilAsync(() => sent.Count == 2);
        OtmrRealtimeSignalUpdate changed = Assert.Single(sent.Last().Signals);
        Assert.Equal(OtmrRealtimeContract.Inactive, changed.State);
        Assert.Equal(0, changed.RawValue);
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
            "555001", DateTimeOffset.UtcNow, null, "verified.json", new string('A', 64), unsafeSignals)));
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
