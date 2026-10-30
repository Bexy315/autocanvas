using AutoCanvas.Server.Application.Common;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>What a password must look like. Length matters most; no composition rules (NIST SP 800-63B).</summary>
public static class PasswordPolicy
{
    /// <summary>Shortest accepted password.</summary>
    public const int MinLength = 12;

    /// <summary>Longest accepted password; bounds the hashing work per request.</summary>
    public const int MaxLength = 256;

    /// <summary>Why the password is not acceptable for this user, or <see langword="null"/>.</summary>
    public static UseCaseError? Check(string field, string? password, string userName) => password switch
    {
        null or { Length: < MinLength } => UseCaseError.Invalid(field, $"The password must have at least {MinLength} characters."),
        { Length: > MaxLength } => UseCaseError.Invalid(field, $"The password must have at most {MaxLength} characters."),
        _ when string.Equals(password, userName, StringComparison.OrdinalIgnoreCase) =>
            UseCaseError.Invalid(field, "The password must not be the user name."),
        _ => null,
    };
}
