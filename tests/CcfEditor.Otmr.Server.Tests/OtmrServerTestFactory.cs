using CcfEditor.Otmr.Server;
using CcfEditor.Otmr.Sync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CcfEditor.Otmr.Server.Tests;

internal sealed class OtmrServerTestFactory(bool failBeforeReceipt = false) : WebApplicationFactory<Program>
{
    public const string Token = "integration-test-token-not-a-secret";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "otmr-server-tests", Guid.NewGuid().ToString("N"));
    public string DatabasePath => Path.Combine(_directory, "OTMR_RCM.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // The Windows Event Log provider is unavailable in restricted test
        // environments; server behaviour is asserted through HTTP/SQLite.
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OtmrServer:DatabasePath"] = DatabasePath,
                ["OtmrServer:BearerToken"] = Token,
                ["OtmrServer:ListenUrl"] = "http://127.0.0.1:0",
                ["OtmrServer:MaximumUploadBodyBytes"] = "10485760",
                ["OtmrServer:MaximumQueryResultCount"] = "100",
                ["OtmrServer:MaximumSessionResultCount"] = "20",
                ["OtmrServer:MaximumRecordRangeDays"] = "31",
                ["OtmrServer:MaximumRealtimeSignalUpdates"] = "100",
                ["OtmrServer:LiveStaleAfterSeconds"] = "30",
                ["OtmrServer:WindowsAppOfflineAfterSeconds"] = "30"
            });
        });
        if (failBeforeReceipt)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOtmrUploadPersistenceHook>();
                services.AddSingleton<IOtmrUploadPersistenceHook, ThrowingPersistenceHook>();
            });
        }
    }

    public HttpClient CreateAuthenticatedClient()
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        return client;
    }

    private sealed class ThrowingPersistenceHook : IOtmrUploadPersistenceHook
    {
        public Task BeforeReceiptAsync(OtmrApiV1UploadRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Injected persistence failure.");
    }
}
