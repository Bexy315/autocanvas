using System.Security.Cryptography;

namespace AutoCanvas.Server.Application.Variables;

/// <summary>
/// The 256-bit key that encrypts every secret (ADR-0013). It lives outside the database; losing it
/// makes the secrets unrecoverable, so AutoCanvas never generates one on its own.
/// </summary>
public sealed class MasterKey
{
    /// <summary>Key size in bytes.</summary>
    public const int Size = 32;

    private readonly byte[] key;

    private MasterKey(byte[] key)
    {
        this.key = key;
        Id = Convert.ToHexStringLower(SHA256.HashData(key))[..8];
    }

    /// <summary>
    /// Identifies the key in the database and in messages without revealing it: the start of its
    /// SHA-256 hash.
    /// </summary>
    public string Id { get; }

    /// <summary>The key in its text form (base64), as configured.</summary>
    public string Text => Convert.ToBase64String(key);

    /// <summary>Reads a key from its text form, or explains why it is not one.</summary>
    public static MasterKey? Parse(string? text, out string? problem)
    {
        problem = null;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text?.Trim() ?? string.Empty);
        }
        catch (FormatException)
        {
            problem = "The master key is not valid base64.";
            return null;
        }

        if (bytes.Length != Size)
        {
            problem = $"The master key must be {Size} bytes (44 characters of base64); this one has {bytes.Length} bytes.";
            return null;
        }

        return new MasterKey(bytes);
    }

    /// <summary>A new random key, for setting up an installation or rotating the key.</summary>
    public static MasterKey Generate() => new(RandomNumberGenerator.GetBytes(Size));

    /// <summary>A cipher with this key; it must be disposed.</summary>
    internal AesGcm CreateAes() => new(key, SecretCipher.TagSize);
}
