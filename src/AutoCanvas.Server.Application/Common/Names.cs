namespace AutoCanvas.Server.Application.Common;

/// <summary>The rule for names of things in the folder tree (folders, automations).</summary>
internal static class Names
{
    /// <summary>
    /// Why the name is not acceptable, or <see langword="null"/>: it needs 1 to
    /// <paramref name="maxLength"/> characters after trimming, no slash (paths use it) and no
    /// control characters.
    /// </summary>
    public static UseCaseError? Check(string field, string? name, int maxLength, string what) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > maxLength || name.Any(char.IsControl) || name.Contains('/', StringComparison.Ordinal)
            ? UseCaseError.Invalid(field, $"The {what} name must have 1 to {maxLength} characters, no slash and no control characters.")
            : null;
}
