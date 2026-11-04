using System.Security.Cryptography;
using System.Text;

namespace AutoCanvas.Server.Application.Triggers;

/// <summary>Checks how webhook callers prove they know the secret, in constant time.</summary>
internal static class WebhookSignatures
{
    /// <summary>The prefix of signatures in <c>sha256=&lt;hex&gt;</c> form.</summary>
    public const string Prefix = "sha256=";

    /// <summary>The hex part of a <c>sha256=&lt;hex&gt;</c> header, or <see langword="null"/> when it has another form.</summary>
    public static string? Prefixed(string? header) =>
        header is not null && header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? header[Prefix.Length..].Trim() : null;

    /// <summary>Whether <paramref name="hex"/> is the HMAC-SHA256 of the body with the secret.</summary>
    public static bool Matches(string? secret, ReadOnlySpan<byte> body, string? hex)
    {
        if (secret is null || hex is null)
        {
            return false;
        }

        byte[] given;
        try
        {
            given = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body), given);
    }

    /// <summary>Whether a secret sent as it is (as GitLab does) is the secret.</summary>
    public static bool SameSecret(string? secret, string? given) =>
        secret is not null && given is not null
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), SHA256.HashData(Encoding.UTF8.GetBytes(given)));
}
