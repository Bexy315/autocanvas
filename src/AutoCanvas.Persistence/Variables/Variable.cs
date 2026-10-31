using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Persistence.Variables;

/// <summary>
/// A plain configuration value of the instance or a folder, readable in automations as
/// <c>${{ vars.NAME }}</c>. Variables of one automation live in its file instead.
/// </summary>
public sealed class Variable
{
    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Largest value, in characters.</summary>
    public const int MaxValueLength = 64 * 1024;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Where it is defined; see <see cref="ValueScopes"/>.</summary>
    public required string Scope { get; init; }

    /// <summary>The folder, for folder variables; deleting the folder deletes them.</summary>
    public Guid? FolderId { get; init; }

    /// <summary>The name as written; expressions read it regardless of case.</summary>
    public required string Name { get; set; }

    /// <summary>Upper-case form of <see cref="Name"/>, unique per scope.</summary>
    public required string NormalizedName { get; init; }

    /// <summary>The value.</summary>
    public required string Value { get; set; }

    /// <summary>When it was first set.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it was last set.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who set it last, as their user name then.</summary>
    public required string UpdatedByName { get; set; }

    /// <summary>Navigation to the folder.</summary>
    public Folder? Folder { get; init; }
}
