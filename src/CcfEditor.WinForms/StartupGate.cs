using System.Net.Http.Headers;

namespace CcfEditor.WinForms;

internal readonly record struct StartupGateResult(bool IsAllowed, string Status, string Detail)
{
    public static StartupGateResult Allowed(string status) => new(true, status, "Startup authorised.");
    public static StartupGateResult Denied(string status, string detail) => new(false, status, detail);
}

internal static class StartupGate
{
    internal const string ControlUrl =
        "https://api.github.com/repos/paulopp1234/OTMR-CCF-EDYTOR/contents/OTMR_RCM?ref=main";

    internal const string AllowedStatus = "ALLOW_START";
    internal const string BlockedStatus = "DO_NOT_START";

    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    internal static StartupGateResult Check()
    {
        using var client = new HttpClient
        {
            // StartupGate owns timeout enforcement so the same path is testable
            // with an injected HttpClient and a short test timeout.
            Timeout = Timeout.InfiniteTimeSpan
        };

        return Check(client, RequestTimeout);
    }

    internal static StartupGateResult Check(HttpClient client, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(client);

        try
        {
            if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            using var request = new HttpRequestMessage(HttpMethod.Get, ControlUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
                MaxAge = TimeSpan.Zero
            };
            request.Headers.Pragma.ParseAdd("no-cache");
            request.Headers.UserAgent.ParseAdd("OTMR-CcfEditor/0.3");

            using var timeoutSource = new CancellationTokenSource(timeout);

            using HttpResponseMessage response = client
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token)
                .GetAwaiter()
                .GetResult();

            if (!response.IsSuccessStatusCode)
            {
                return StartupGateResult.Denied(
                    "UNAVAILABLE",
                    $"The OTMR_RCM startup control file could not be validated. HTTP {(int)response.StatusCode}.");
            }

            string controlText = response.Content
                .ReadAsStringAsync(timeoutSource.Token)
                .GetAwaiter()
                .GetResult();

            string status = controlText.Trim();

            if (string.Equals(status, AllowedStatus, StringComparison.Ordinal))
                return StartupGateResult.Allowed(status);

            if (string.Equals(status, BlockedStatus, StringComparison.Ordinal))
            {
                return StartupGateResult.Denied(
                    status,
                    "OTMR RCM startup is remotely disabled.");
            }

            return StartupGateResult.Denied(
                "INVALID",
                "OTMR_RCM contains an unsupported status value.");
        }
        catch (OperationCanceledException)
        {
            return StartupGateResult.Denied(
                "UNAVAILABLE",
                "The OTMR_RCM startup control file could not be validated. The request timed out.");
        }
        catch (Exception ex)
        {
            return StartupGateResult.Denied(
                "UNAVAILABLE",
                $"The OTMR_RCM startup control file could not be validated. {ex.Message}");
        }
    }
}
