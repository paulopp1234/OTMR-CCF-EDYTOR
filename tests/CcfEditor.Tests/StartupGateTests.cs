using System.Net;
using System.Net.Http.Headers;
using CcfEditor.WinForms;

namespace CcfEditor.Tests;

public sealed class StartupGateTests
{
    [Fact]
    public void ExactAllowLine_AllowsStartupAndIgnoresCl380Line()
    {
        HttpRequestMessage? capturedRequest = null;
        using var client = Client((request, _) =>
        {
            capturedRequest = request;
            return Response(
                "CL380 - DO_NOT_START\n" +
                "OTMR CCF EDYTOR - ALLOW_START\n");
        });

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.True(result.IsAllowed);
        Assert.Equal(StartupGate.AllowedStatus, result.Status);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest.Method);
        Assert.Equal(StartupGate.ControlUrl, capturedRequest.RequestUri!.AbsoluteUri);
        Assert.True(capturedRequest.Headers.CacheControl!.NoCache);
        Assert.True(capturedRequest.Headers.CacheControl.NoStore);
        Assert.Contains(capturedRequest.Headers.Pragma,
            value => string.Equals(value.Name, "no-cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExactBlockLine_DeniesStartup()
    {
        using var client = Client((_, _) => Response("OTMR CCF EDYTOR - DO_NOT_START"));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal(StartupGate.BlockedStatus, result.Status);
    }

    [Fact]
    public void EveryCheckDownloadsStatusAndDoesNotCacheAllow()
    {
        int requestCount = 0;
        using var client = Client((_, _) => Response(
            Interlocked.Increment(ref requestCount) == 1
                ? "OTMR CCF EDYTOR - ALLOW_START"
                : "OTMR CCF EDYTOR - DO_NOT_START"));

        StartupGateResult first = StartupGate.Check(client, TimeSpan.FromSeconds(1));
        StartupGateResult second = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.True(first.IsAllowed);
        Assert.False(second.IsAllowed);
        Assert.Equal(2, requestCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t")]
    [InlineData("CL380 - ALLOW_START")]
    [InlineData("OTMR CCF EDYTOR - ")]
    [InlineData("OTMR CCF EDYTOR - UNKNOWN")]
    [InlineData("OTMR CCF EDYTOR - allow_start")]
    [InlineData("OTMR CCF EDYTOR - ALLOW_START ")]
    [InlineData(" OTMR CCF EDYTOR - ALLOW_START")]
    [InlineData("OTMR CCF EDYTOR- ALLOW_START")]
    public void EmptyMissingOrMalformedControl_DeniesStartup(string content)
    {
        using var client = Client((_, _) => Response(content));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void MoreThanOneOtmrControlLine_DeniesStartup()
    {
        using var client = Client((_, _) => Response(
            "OTMR CCF EDYTOR - ALLOW_START\n" +
            "OTMR CCF EDYTOR - DO_NOT_START"));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal("DUPLICATE", result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void NonSuccessHttpResponse_DeniesStartup(HttpStatusCode statusCode)
    {
        using var client = Client((_, _) => new HttpResponseMessage(statusCode));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal($"HTTP {(int)statusCode}", result.Status);
    }

    [Fact]
    public void NetworkDnsOrTlsFailure_DeniesStartup()
    {
        using var client = Client((_, _) => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("unavailable")));

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromSeconds(1));

        Assert.False(result.IsAllowed);
        Assert.Equal("UNAVAILABLE", result.Status);
    }

    [Fact]
    public void Timeout_DeniesStartup()
    {
        using var client = Client(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Response("OTMR CCF EDYTOR - ALLOW_START");
        });

        StartupGateResult result = StartupGate.Check(client, TimeSpan.FromMilliseconds(25));

        Assert.False(result.IsAllowed);
        Assert.Equal("TIMEOUT", result.Status);
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
