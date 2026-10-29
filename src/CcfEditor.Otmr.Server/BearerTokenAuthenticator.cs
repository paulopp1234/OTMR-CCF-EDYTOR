using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CcfEditor.Otmr.Server;

public sealed class BearerTokenAuthenticator(IOptions<OtmrServerOptions> options)
{
    private readonly OtmrServerOptions _options = options.Value;

    public bool IsAuthorized(HttpRequest request)
    {
        string configured = _options.BearerToken;
        if (string.IsNullOrWhiteSpace(configured))
            return false; // Fail closed when the administrator has not supplied a token.

        string header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string supplied = header[prefix.Length..].Trim();
        if (supplied.Length == 0)
            return false;

        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        byte[] suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}
