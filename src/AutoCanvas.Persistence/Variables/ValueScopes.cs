namespace AutoCanvas.Persistence.Variables;

/// <summary>
/// Keys of the places where variables and secrets are defined, stored in their <c>scope</c>
/// column. One text column keeps names unique per scope on every database (nullable foreign keys
/// would not), and it is what binds a secret's ciphertext to its place.
/// </summary>
public static class ValueScopes
{
    /// <summary>The whole instance.</summary>
    public const string Instance = "instance";

    /// <summary>Column size of a scope key.</summary>
    public const int MaxLength = 64;

    /// <summary>A folder.</summary>
    public static string Folder(Guid id) => $"folder:{id:D}";

    /// <summary>One automation (secrets only; its variables are in its file).</summary>
    public static string Automation(Guid id) => $"automation:{id:D}";
}
