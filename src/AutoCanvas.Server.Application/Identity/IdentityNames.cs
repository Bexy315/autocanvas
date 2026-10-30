using System.Text.RegularExpressions;
using AutoCanvas.Persistence.Identity;
using Group = AutoCanvas.Persistence.Identity.Group;
using AutoCanvas.Server.Application.Common;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>Rules for user and group names, and the normalized form used for uniqueness.</summary>
public static partial class IdentityNames
{
    /// <summary>The form names are compared and stored in for uniqueness: trimmed, upper case.</summary>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToUpperInvariant();
    }

    /// <summary>Why a user name is not acceptable, or <see langword="null"/>.</summary>
    public static UseCaseError? CheckUserName(string? userName) =>
        userName is not null && UserNamePattern().IsMatch(userName)
            ? null
            : UseCaseError.Invalid(
                "userName",
                $"The user name must have 1 to {User.MaxUserNameLength} characters (letters, digits, dot, underscore or hyphen) and start with a letter or digit.");

    /// <summary>Why a group name is not acceptable, or <see langword="null"/>.</summary>
    public static UseCaseError? CheckGroupName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > Group.MaxNameLength || name.Any(char.IsControl)
            ? UseCaseError.Invalid("name", $"The group name must have 1 to {Group.MaxNameLength} characters and no control characters.")
            : null;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 100)]
    private static partial Regex UserNamePattern();
}
