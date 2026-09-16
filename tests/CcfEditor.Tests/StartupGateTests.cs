using System.Net;
using CcfEditor.WinForms;

namespace CcfEditor.Tests;

public sealed class StartupGateTests
{
    [Theory]
    [InlineData("ALLOW_START")]
    [InlineData("ALLOW_START\n")]
    [InlineData(" \t\r\nALLOW_START\r\n\t ")]
    public void DedicatedAllowValue_AllowsStartupAndRequestsDedicatedFile(string content)
    {
        HttpRequestMessage? capturedRequest = null;
        using var client = Client((request, _) =>
        {
            capturedRequest = request;
            return Response(content);
        });

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.True(result.IsAllowed);
        Assert.Equal(StartupGate.AllowedStatus, result.Status);
        Assert.NotNull(capturedRequest);
        AssertFreshControlRequest(capturedRequest);
    }

    [Theory]
    [InlineData("DO_NOT_START")]
    [InlineData(" \t\r\nDO_NOT_START\r\n\t ")]
    public void DedicatedBlockValue_DeniesStartup(string content)
    {
        using var client = Client((_, _) => Response(content));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal(StartupGate.BlockedStatus, result.Status);
        Assert.Equal("OTMR RCM startup is remotely disabled.", result.Detail);
    }

    [Fact]
    public void EveryCheckDownloadsStatusAndDoesNotCacheAllow()
    {
        int requestCount = 0;
        var requests = new List<HttpRequestMessage>();
        using var client = Client((request, _) =>
        {
            requests.Add(request);
            return Response(Interlocked.Increment(ref requestCount) == 1
                ? "ALLOW_START"
                : "DO_NOT_START");
        });

        StartupGateResult first = StartupGate.Check(client, TimeSpan.FromSeconds(1));
        StartupGateResult second = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.True(first.IsAllowed);
        Assert.False(second.IsAllowed);
        Assert.Equal(StartupGate.BlockedStatus, second.Status);
        Assert.Equal(2, requestCount);
        Assert.NotSame(requests[0], requests[1]);
        Assert.All(requests, AssertFreshControlRequest);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "DO_NOT_START", "DO_NOT_START")]
    [InlineData(HttpStatusCode.OK, "", "INVALID")]
    [InlineData(HttpStatusCode.OK, "UNKNOWN", "INVALID")]
    [InlineData(HttpStatusCode.NotFound, "", "UNAVAILABLE")]
    public void NewLaunchWithNewClient_RequestsAgainAndDoesNotReusePreviousAllow(
        HttpStatusCode nextStatusCode, string nextContent, string expectedStatus)
    {
        var requests = new List<HttpRequestMessage>();
        HttpResponseMessage Respond(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            return requests.Count == 1
                ? Response("ALLOW_START")
                : new HttpResponseMessage(nextStatusCode) { Content = new StringContent(nextContent) };
        }

        StartupGateResult first;
        using (var firstLaunchClient = Client(Respond))
            first = StartupGate.Check(firstLaunchClient, TimeSpan.FromSeconds(1));

        using var nextLaunchClient = Client(Respond);
        StartupGateResult next = StartupGate.Check(nextLaunchClient, TimeSpan.FromSeconds(1));

        Assert.True(first.IsAllowed);
        Assert.False(next.IsAllowed);
        Assert.Equal(expectedStatus, next.Status);
        Assert.Equal(2, requests.Count);
        Assert.NotSame(requests[0], requests[1]);
        Assert.All(requests, AssertFreshControlRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t")]
    public void EmptyFile_DeniesStartupAsInvalid(string content)
    {
        using var client = Client((_, _) => Response(content));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal("INVALID", result.Status);
        Assert.Equal("OTMR_RCM contains an unsupported status value.", result.Detail);
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("allow_start")]
    [InlineData("do_not_start")]
    [InlineData("ALLOW START")]
    [InlineData("ALLOW_START extra")]
    [InlineData("\"ALLOW_START\"")]
    [InlineData("OTMR_RCM = ALLOW_START")]
    [InlineData("ALLOW_START\nDO_NOT_START")]
    [InlineData("ALLOW_START\r\nALLOW_START")]
    [InlineData("ALLOW_START\n# comment")]
    [InlineData("ALLOW_START\0")]
    public void MalformedOrUnknownStatus_DeniesStartupAsInvalid(string content)
    {
        using var client = Client((_, _) => Response(content));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal("INVALID", result.Status);
        Assert.Equal("OTMR_RCM contains an unsupported status value.", result.Detail);
    }

    [Theory]
    [InlineData("CL380 - ALLOW_START")]
    [InlineData("OTMR CCF EDYTOR - ALLOW_START")]
    [InlineData("OTMR CCF EDYTOR - DO_NOT_START")]
    [InlineData("CL380 - DO_NOT_START\nOTMR CCF EDYTOR - ALLOW_START\n")]
    [InlineData("CL380 - ALLOW_START\nOTMR CCF EDYTOR - DO_NOT_START\n")]
    [InlineData("OTMR CCF EDYTOR - ALLOW_START\nOTMR CCF EDYTOR - DO_NOT_START")]
    [InlineData("ALLOW_START\nOTMR CCF EDYTOR - ALLOW_START")]
    public void OldSharedStatusFileControlLines_AreRejectedAsInvalid(string content)
    {
        using var client = Client((_, _) => Response(content));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal("INVALID", result.Status);
        Assert.Equal("OTMR_RCM contains an unsupported status value.", result.Detail);
    }

    [Fact]
    public void NoContentHttpResponse_DeniesStartupAsInvalid()
    {
        using var client = Client((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal("INVALID", result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void NonSuccessHttpResponse_DeniesStartup(HttpStatusCode statusCode)
    {
        var requests = new List<HttpRequestMessage>();
        using var client = Client((request, _) =>
        {
            requests.Add(request);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("ALLOW_START")
            };
        });

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal("UNAVAILABLE", result.Status);
        Assert.Contains("The OTMR_RCM startup control file could not be validated.", result.Detail);
        Assert.Contains($"HTTP {(int)statusCode}", result.Detail);
        AssertFreshControlRequest(Assert.Single(requests));
    }

    [Theory]
    [InlineData(HttpRequestError.Unknown)]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public void NetworkDnsOrTlsFailure_DeniesStartup(HttpRequestError requestError)
    {
        using var client = Client((_, _) => Task.FromException<HttpResponseMessage>(
            new HttpRequestException(requestError, "simulated network failure")));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal("UNAVAILABLE", result.Status);
        Assert.Contains("The OTMR_RCM startup control file could not be validated.", result.Detail);
    }

    [Fact]
    public void Timeout_DeniesStartup()
    {
        CancellationToken requestCancellationToken = default;
        using var client = Client(async (_, cancellationToken) =>
        {
            requestCancellationToken = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Response("ALLOW_START");
        });

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromMilliseconds(25));

        Assert.False(result.IsAllowed);
        Assert.Equal("UNAVAILABLE", result.Status);
        Assert.Contains("The OTMR_RCM startup control file could not be validated.", result.Detail);
        Assert.Contains("timed out", result.Detail);
        Assert.True(requestCancellationToken.IsCancellationRequested);
        Assert.Equal(TimeSpan.FromSeconds(8), StartupGate.RequestTimeout);
    }

    [Fact]
    public void ResponseReadException_DeniesStartup()
    {
        using var client = Client((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingHttpContent()
        });

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal("UNAVAILABLE", result.Status);
        Assert.Contains("The OTMR_RCM startup control file could not be validated.", result.Detail);
    }

    private static void AssertFreshControlRequest(HttpRequestMessage request)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://raw.githubusercontent.com/paulopp1234/OTMR-CCF-EDYTOR/main/OTMR_RCM",
            request.RequestUri!.AbsoluteUri);
        Assert.True(request.Headers.CacheControl!.NoCache);
        Assert.True(request.Headers.CacheControl.NoStore);
        Assert.Equal(TimeSpan.Zero, request.Headers.CacheControl.MaxAge);
        Assert.Contains(request.Headers.Pragma,
            value => string.Equals(value.Name, "no-cache", StringComparison.OrdinalIgnoreCase));
    }

    private static HttpClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) =>
        new(new DelegateHttpHandler(response))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    private static HttpClient Client(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> response) =>
        Client((request, cancellationToken) => Task.FromResult(response(request, cancellationToken)));

    private static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content)
    };

    private sealed class DelegateHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => response(request, cancellationToken);
    }

    private sealed class ThrowingHttpContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new InvalidDataException("simulated response read failure"));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
