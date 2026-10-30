using System.Globalization;
using System.Security.Cryptography;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>
/// Hashes passwords with PBKDF2-HMAC-SHA256 and a random salt. The stored form names algorithm and
/// cost (<c>pbkdf2-sha256$iterations$salt$hash</c>), so the cost can rise and hashes can be
/// upgraded on the next sign-in (ADR-0011).
/// </summary>
public sealed class PasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly PasswordHashingOptions options;
    private readonly Lazy<string> dummyHash;

    /// <summary>Creates a hasher with the given cost.</summary>
    public PasswordHasher(PasswordHashingOptions options)
    {
        this.options = options;
        dummyHash = new Lazy<string>(() => Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltSize))));
    }

    /// <summary>A new hash of the password with a fresh salt.</summary>
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, options.Iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Create(CultureInfo.InvariantCulture, $"{Algorithm}${options.Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <summary>Checks a password against a stored hash in constant time.</summary>
    public PasswordVerification Verify(string password, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(storedHash);

        var parts = storedHash.Split('$');
        if (parts.Length != 4
            || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < 1
            || !TryFromBase64(parts[2], out var salt)
            || !TryFromBase64(parts[3], out var expected))
        {
            return PasswordVerification.Failed;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerification.Failed;
        }

        return iterations < options.Iterations || expected.Length != HashSize
            ? PasswordVerification.SucceededRehashNeeded
            : PasswordVerification.Succeeded;
    }

    /// <summary>
    /// Spends the same time as checking a real password, for sign-ins with an unknown user name, so
    /// response times do not reveal which names exist.
    /// </summary>
    public void VerifyNothing(string password) => _ = Verify(password, dummyHash.Value);

    private static bool TryFromBase64(string text, out byte[] bytes)
    {
        var buffer = new byte[text.Length];
        if (Convert.TryFromBase64String(text, buffer, out var written) && written > 0)
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = [];
        return false;
    }
}
