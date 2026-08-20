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

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    internal static StartupGateResult Check()
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = RequestTimeout
            };

            using var request = new HttpRequestMessage(HttpMethod.Get, ControlUrl);
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
                MaxAge = TimeSpan.Zero
            };
            request.Headers.Pragma.ParseAdd("no-cache");
            request.Headers.UserAgent.ParseAdd("OTMR-CcfEditor/0.2");

            using HttpResponseMessage response = client
                .SendAsync(request, HttpCompletionOption.ResponseContentRead)
                .GetAwaiter()
                .GetResult();

            if (!response.IsSuccessStatusCode)
            {
                return StartupGateResult.Denied(
                    $"HTTP {(int)response.StatusCode}",
                    "Remote startup authorisation could not be read.");
            }

            string controlText = response.Content
                .ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();

            string[] matchingLines = controlText
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
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

            string status = matchingLines[0][AppControlPrefix.Length..].Trim();

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
        catch (Exception ex)
        {
            return StartupGateResult.Denied(
                "UNAVAILABLE",
                $"Remote startup authorisation check failed: {ex.Message}");
        }
    }
}
