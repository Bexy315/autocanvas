using System.Security.Cryptography;
using System.Text;

namespace AutoCanvas.Server.Application.Variables;

/// <summary>
/// Encrypts secret values with AES-256-GCM under the master key (ADR-0013). Each value gets a
/// random nonce, and the associated data binds it to its scope and name: a ciphertext copied to
/// another row, scope or name does not decrypt.
/// </summary>
public sealed class SecretCipher(MasterKey key)
{
    /// <summary>Size of the authentication tag in bytes.</summary>
    public const int TagSize = 16;

    private const int NonceSize = 12;

    /// <summary>The key this cipher encrypts with.</summary>
    public MasterKey Key { get; } = key ?? throw new ArgumentNullException(nameof(key));

    /// <summary>The value's ciphertext as stored: base64 of nonce, tag and encrypted bytes.</summary>
    public string Encrypt(string scope, string normalizedName, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var plain = Encoding.UTF8.GetBytes(value);
        var sealedValue = new byte[NonceSize + TagSize + plain.Length];
        var nonce = sealedValue.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = Key.CreateAes();
        aes.Encrypt(nonce, plain, sealedValue.AsSpan(NonceSize + TagSize), sealedValue.AsSpan(NonceSize, TagSize), AssociatedData(scope, normalizedName));
        return Convert.ToBase64String(sealedValue);
    }

    /// <summary>
    /// The value, or <see langword="null"/> when the ciphertext was not made by this key for this
    /// scope and name (or was changed).
    /// </summary>
    public string? Decrypt(string scope, string normalizedName, string ciphertext)
    {
        byte[] sealedValue;
        try
        {
            sealedValue = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException)
        {
            return null;
        }

        if (sealedValue.Length < NonceSize + TagSize)
        {
            return null;
        }

        var plain = new byte[sealedValue.Length - NonceSize - TagSize];
        try
        {
            using var aes = Key.CreateAes();
            aes.Decrypt(sealedValue.AsSpan(0, NonceSize), sealedValue.AsSpan(NonceSize + TagSize), sealedValue.AsSpan(NonceSize, TagSize), plain, AssociatedData(scope, normalizedName));
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }

        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] AssociatedData(string scope, string normalizedName) =>
        Encoding.UTF8.GetBytes($"autocanvas/secret/v1\n{scope}\n{normalizedName}");
}
