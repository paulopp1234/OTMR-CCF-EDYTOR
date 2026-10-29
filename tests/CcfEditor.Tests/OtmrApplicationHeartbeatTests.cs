using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Storage;
using CcfEditor.Otmr.Sync;
using CcfEditor.Otmr.Transport;
using CcfEditor.WinForms;

namespace CcfEditor.Tests;

public sealed class OtmrApplicationHeartbeatTests
{
    [Fact]
    public void WindowsApplicationIdentityIsOneNonPersistedProcessScopedGuid()
    {
        Guid first = OtmrWindowsApplicationIdentity.AppInstanceId;
        Guid second = OtmrWindowsApplicationIdentity.AppInstanceId;

        Assert.NotEqual(Guid.Empty, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ExplicitServerSyncEnableStartsHeartbeatAndKeepsOneProcessIdentityAcrossOtmrSessions()
    {
        var sent = new ConcurrentQueue<OtmrApplicationHeartbeatRequest>();
        var client = new DelegateHeartbeatClient((request, _) =>
        {
            sent.Enqueue(request);
            return Task.FromResult(new OtmrApplicationHeartbeatAcknowledgement(
                OtmrApiContract.Version, request.AppInstanceId, true, DateTimeOffset.UtcNow));
        });
        Guid appInstanceId = Guid.NewGuid();
        using var http = new HttpClient(new RejectNetworkHandler());
        await using var service = new OtmrApplicationHeartbeatService(
            http, appInstanceId, "0.3.0", TimeSpan.FromMilliseconds(20), _ => client);

        service.UpdateContext(new(false, "171804", null));
        await Task.Delay(60);
        Assert.Empty(sent);

        service.Configure(Configuration(enabled: true));
        await WaitUntilAsync(() => sent.Count >= 1);
        OtmrApplicationHeartbeatRequest disconnected = sent.First();
        Assert.Equal(appInstanceId, disconnected.AppInstanceId);
        Assert.False(disconnected.OtmrLiveConnected);
        Assert.Null(disconnected.SourceConnectionId);

        const string connectionA = "otmr-connection-a";
        service.UpdateContext(new(true, "171804", connectionA));
        await WaitUntilAsync(() => sent.Any(x => x.SourceConnectionId == connectionA));
        OtmrApplicationHeartbeatRequest inA = sent.Last(x => x.SourceConnectionId == connectionA);
        Assert.Equal(appInstanceId, inA.AppInstanceId);

        const string connectionB = "otmr-connection-b";
        service.UpdateContext(new(true, "171804", connectionB));
        await WaitUntilAsync(() => sent.Any(x => x.SourceConnectionId == connectionB));
        OtmrApplicationHeartbeatRequest inB = sent.Last(x => x.SourceConnectionId == connectionB);
        Assert.Equal(appInstanceId, inB.AppInstanceId);
        Assert.NotEqual(inA.SourceConnectionId, inB.SourceConnectionId);
    }

    [Fact]
    public async Task SeparateApplicationProcessesUseDifferentIds()
    {
        Guid appA = Guid.NewGuid();
        Guid appB = Guid.NewGuid();
        using var httpA = new HttpClient(new RejectNetworkHandler());
        using var httpB = new HttpClient(new RejectNetworkHandler());
        await using var serviceA = new OtmrApplicationHeartbeatService(httpA, appA, "0.3.0");
        await using var serviceB = new OtmrApplicationHeartbeatService(httpB, appB, "0.3.0");

        Assert.NotEqual(serviceA.AppInstanceId, serviceB.AppInstanceId);
    }

    [Fact]
    public async Task HttpHeartbeatUsesBearerHeaderButNeverPayloadOrDiagnosticText()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        Guid appInstanceId = Guid.NewGuid();
        const string token = "heartbeat-secret-token";
        using var http = new HttpClient(new DelegateHandler(async (request, cancellationToken) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(new OtmrApplicationHeartbeatAcknowledgement(
                OtmrApiContract.Version, appInstanceId, true, DateTimeOffset.UtcNow));
        }));
        var options = new OtmrSyncOptions
        {
            Enabled = true,
            BaseUrl = new Uri("https://otmr.example.test/"),
            ApiToken = token
        };
        var client = new HttpOtmrApplicationHeartbeatClient(http, options);
        var heartbeat = new OtmrApplicationHeartbeatRequest(
            OtmrApiContract.Version, appInstanceId, "0.3.0", DateTimeOffset.UtcNow,
            true, "171804", "source-a");

        await client.SendAsync(heartbeat);

        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal(token, captured.Headers.Authorization.Parameter);
        Assert.Equal(OtmrApplicationHeartbeatContract.Route, captured.RequestUri!.AbsolutePath);
        Assert.DoesNotContain(token, body!, StringComparison.Ordinal);
        Assert.DoesNotContain(token, Configuration(true, token).ToString(), StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(body!);
        Assert.Equal(appInstanceId, json.RootElement.GetProperty("appInstanceId").GetGuid());
        Assert.Equal("0.3.0", json.RootElement.GetProperty("applicationVersion").GetString());
    }

    [Fact]
    public void HeartbeatUsesExistingProductionHttpsAndExactTestHttpPolicy()
    {
        Assert.Throws<InvalidOperationException>(() => new OtmrApplicationHeartbeatConfiguration
        {
            Enabled = true, ServerUrl = "http://example.com/", ApiToken = "token"
        }.CreateClientOptions());

        OtmrSyncOptions exact = new OtmrApplicationHeartbeatConfiguration
        {
            Enabled = true,
            ServerUrl = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer,
            ApiToken = "token",
            AllowInsecureKnownTestServer = true
        }.CreateClientOptions();
        Assert.True(exact.AllowInsecureKnownTestServer);

        Assert.Throws<InvalidOperationException>(() => new OtmrApplicationHeartbeatConfiguration
        {
            Enabled = true,
            ServerUrl = OtmrSyncOptions.KnownInsecureDigitalOceanTestServer + "other/",
            ApiToken = "token",
            AllowInsecureKnownTestServer = true
        }.CreateClientOptions());
    }

    [Fact]
    public async Task HeartbeatFailureRemainsIsolatedFromSerialFrameReception()
    {
        string folder = Path.Combine(Path.GetTempPath(), "otmr-heartbeat-isolation-" + Guid.NewGuid().ToString("N"));
        string database = Path.Combine(folder, "OTMR_RCM.db");
        Directory.CreateDirectory(folder);
        try
        {
        var failingClient = new DelegateHeartbeatClient((_, _) =>
            Task.FromException<OtmrApplicationHeartbeatAcknowledgement>(
                new HttpRequestException("server unavailable")));
        using var http = new HttpClient(new RejectNetworkHandler());
        await using var heartbeat = new OtmrApplicationHeartbeatService(
            http, Guid.NewGuid(), "0.3.0", TimeSpan.FromMilliseconds(15), _ => failingClient);
        heartbeat.Configure(Configuration(enabled: true));

        using var transport = new FakeTransport();
        using var live = new OtmrLiveService(transport);
        await using var store = new SqliteOtmrRecordingStore(database);
        await store.InitializeAsync();
        Guid recordingSessionId = await store.StartSessionAsync(new OtmrRecordingSessionContext
        {
            SoftwareVersion = "0.3.0",
            ComPort = "COM1"
        });
        live.SetRecordingStore(store);
        int completeFrames = 0;
        live.FrameReceived += (_, _) => completeFrames++;
        await live.ConnectAsync(OtmrSerialSettings.Class171Bench("COM1"));
        transport.EmitRx(new byte[] { 0xFB, 0xFB, 0x0C, 0xFF });

        await WaitUntilAsync(() => heartbeat.Status.Failures >= 1);
        Assert.True(live.IsConnected);
        Assert.Equal(1, completeFrames);
        Assert.Equal("HEARTBEAT FAILED", heartbeat.Status.LastResult);
        Assert.Contains("server unavailable", heartbeat.Status.LastError, StringComparison.Ordinal);
        await store.StopSessionAsync(DateTimeOffset.UtcNow);
        OtmrSessionUploadPackage recording = await store.BuildUploadPackageAsync(recordingSessionId);
        Assert.Single(recording.LiveFrames);
        Assert.Contains(recording.RawEntries, entry => entry.Data.SequenceEqual(
            new byte[] { 0xFB, 0xFB, 0x0C, 0xFF }));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static OtmrApplicationHeartbeatConfiguration Configuration(
        bool enabled,
        string token = "heartbeat-test-token") => new()
    {
        Enabled = enabled,
        ServerUrl = "https://otmr.example.test/",
        ApiToken = token,
        RequestTimeout = TimeSpan.FromSeconds(1)
    };

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Timed out waiting for heartbeat test condition.");
            await Task.Delay(10);
        }
    }

    private sealed class DelegateHeartbeatClient(
        Func<OtmrApplicationHeartbeatRequest, CancellationToken,
            Task<OtmrApplicationHeartbeatAcknowledgement>> send) : IOtmrApplicationHeartbeatClient
    {
        public Task<OtmrApplicationHeartbeatAcknowledgement> SendAsync(
            OtmrApplicationHeartbeatRequest heartbeat,
            CancellationToken cancellationToken = default) => send(heartbeat, cancellationToken);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new InvalidOperationException("Unexpected real HTTP call."));
    }

    private sealed class FakeTransport : IOtmrTransport
    {
        public bool IsConnected { get; private set; }
        public event EventHandler<OtmrBytesReceivedEventArgs>? BytesReceived;
        public event EventHandler<OtmrBytesTransmittedEventArgs>? BytesTransmitted;
        public event EventHandler<OtmrTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

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

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            BytesTransmitted?.Invoke(this, new(data.ToArray()));
            return Task.CompletedTask;
        }

        public void EmitRx(byte[] data) => BytesReceived?.Invoke(this, new(data));
        public void Dispose() { }
    }

    private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, OtmrApiV1Json.Options))
    };
}
