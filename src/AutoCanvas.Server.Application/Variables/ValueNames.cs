using AutoCanvas.Persistence.Variables;
using AutoCanvas.Server.Application.Common;

namespace AutoCanvas.Server.Application.Variables;

/// <summary>Rules for names and values of variables and secrets.</summary>
internal static class ValueNames
{
    /// <summary>
    /// Why the name cannot be used in <c>${{ vars.NAME }}</c> or <c>${{ secrets.NAME }}</c>, or
    /// <see langword="null"/>: a letter or underscore, then letters, digits and underscores.
    /// </summary>
    public static UseCaseError? Check(string? name) =>
        name is { Length: > 0 and <= Variable.MaxNameLength } && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? null
            : UseCaseError.Invalid("name", $"Names have 1 to {Variable.MaxNameLength} characters: a letter or underscore, then letters, digits and underscores.");

    public static UseCaseError? CheckValue(string? value, bool required) =>
        value is null || (required && value.Length == 0)
            ? UseCaseError.Invalid("value", "The value must not be empty.")
            : value.Length > Variable.MaxValueLength
                ? UseCaseError.Invalid("value", $"The value must have at most {Variable.MaxValueLength} characters.")
                : null;
}
