using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoCanvas.Forge.GitHub;

/// <summary>
/// The JSON Web Token a GitHub App authenticates as itself with (RS256, signed with its private
/// key), exchanged for installation tokens. It is valid for nine minutes; GitHub allows ten, and its
/// start lies a minute back against clock drift.
/// </summary>
public static class GitHubJwt
{
    /// <summary>How long a token is valid from <c>now</c>.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(9);

    /// <summary>Creates a token for the app; throws <see cref="ForgeException"/> when the key is not an RSA private key in PEM.</summary>
    public static string Create(string appId, string privateKeyPem, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(privateKeyPem);
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(privateKeyPem);
        }
        catch (ArgumentException exception)
        {
            throw new ForgeException("The private key is not an RSA private key in PEM form (-----BEGIN RSA PRIVATE KEY----- …).", exception);
        }

        if (rsa.KeySize < 2048)
        {
            throw new ForgeException("The private key has fewer than 2048 bits.");
        }

        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>(StringComparer.Ordinal) { ["alg"] = "RS256", ["typ"] = "JWT" }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["iat"] = now.AddSeconds(-60).ToUnixTimeSeconds(),
            ["exp"] = now.Add(Lifetime).ToUnixTimeSeconds(),
            ["iss"] = appId,
        }));
        var signed = $"{header}.{payload}";
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signed}.{Encode(signature)}";
    }

    private static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);
}
