using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>
/// Random bearer secrets (session cookies, API tokens) and the hash that is stored instead of
/// them. 32 random bytes need no salt or slow hash: they cannot be guessed (ADR-0011).
/// </summary>
public static class SecretTokens
{
    /// <summary>A new random token: <paramref name="prefix"/> plus 32 random bytes, base64url.</summary>
    public static string Create(string prefix = "") => prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>The stored form of a token: SHA-256, lower-case hex.</summary>
    public static string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
