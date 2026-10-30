using System.Security.Cryptography;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>One-time passwords for new accounts and resets, copied or read aloud by an admin.</summary>
public static class PasswordGenerator
{
    // No characters that are easily confused (0/O, 1/l/I).
    private const string Alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>A random 20-character password (about 117 bits).</summary>
    public static string Create() => RandomNumberGenerator.GetString(Alphabet, 20);
}
