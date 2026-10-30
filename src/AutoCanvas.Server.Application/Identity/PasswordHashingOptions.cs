namespace AutoCanvas.Server.Application.Identity;

/// <summary>
/// Cost of new password hashes. Not a configuration setting: lowering it weakens every new hash.
/// Tests register a cheaper instance to stay fast.
/// </summary>
public sealed class PasswordHashingOptions
{
    /// <summary>OWASP's recommendation for PBKDF2-HMAC-SHA256 (2023).</summary>
    public const int RecommendedIterations = 600_000;

    /// <summary>PBKDF2 iterations for new hashes; older hashes with fewer are upgraded on sign-in.</summary>
    public int Iterations { get; init; } = RecommendedIterations;
}
