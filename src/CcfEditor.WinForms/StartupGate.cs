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
        "https://raw.githubusercontent.com/paulopp1234/CL380_App_Control/main/status.txt";

    internal const string AppControlPrefix = "OTMR CCF EDYTOR - ";
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
                    $"HTTP {(int)response.StatusCode}",
                    "Remote startup authorisation could not be read.");
            }

            string controlText = response.Content
                .ReadAsStringAsync(timeoutSource.Token)
                .GetAwaiter()
                .GetResult();

            if (string.IsNullOrWhiteSpace(controlText))
            {
                return StartupGateResult.Denied(
                    "EMPTY",
                    "Remote startup authorisation was empty.");
            }

            string[] matchingLines = controlText
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => line.StartsWith(AppControlPrefix, StringComparison.Ordinal))
                .ToArray();

            if (matchingLines.Length == 0)
            {
                return StartupGateResult.Denied(
                    "MISSING",
                    $"Required control line '{AppControlPrefix}<status>' was not found.");
            }

            if (matchingLines.Length != 1)
            {
                return StartupGateResult.Denied(
                    "DUPLICATE",
                    $"Expected exactly one '{AppControlPrefix}<status>' control line.");
            }

            string status = matchingLines[0][AppControlPrefix.Length..];

            if (string.Equals(status, AllowedStatus, StringComparison.Ordinal))
                return StartupGateResult.Allowed(status);

            if (string.Equals(status, BlockedStatus, StringComparison.Ordinal))
            {
                return StartupGateResult.Denied(
                    status,
                    "OTMR CCF Editor startup is remotely disabled.");
            }

            return StartupGateResult.Denied(
                status.Length == 0 ? "<empty>" : status,
                $"Unknown OTMR CCF Editor startup status. Expected '{AllowedStatus}' or '{BlockedStatus}'.");
        }
        catch (OperationCanceledException)
        {
            return StartupGateResult.Denied(
                "TIMEOUT",
                "Remote startup authorisation check timed out.");
        }
        catch (Exception ex)
        {
            return StartupGateResult.Denied(
                "UNAVAILABLE",
                $"Remote startup authorisation check failed: {ex.Message}");
        }
    }
}
