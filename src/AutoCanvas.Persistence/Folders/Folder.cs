namespace AutoCanvas.Persistence.Folders;

/// <summary>A node of the folder tree that permissions are granted on (ADR-0012).</summary>
public sealed class Folder
{
    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Column size of <see cref="Description"/>.</summary>
    public const int MaxDescriptionLength = 500;

    /// <summary>The deepest a folder can be nested, counting top-level folders as 1.</summary>
    public const int MaxDepth = 16;

    /// <summary>Column size of <see cref="Path"/>: one id and a slash per level.</summary>
    public const int MaxPathLength = 1 + (MaxDepth * 37);

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The folder above; <see langword="null"/> at the top level.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// <see cref="ParentId"/>, or <see cref="Guid.Empty"/> at the top level. Uniqueness of names
    /// among siblings needs a column without nulls on every provider.
    /// </summary>
    public required Guid ParentKey { get; set; }

    /// <summary>The folder's name; unique among its siblings regardless of case.</summary>
    public required string Name { get; set; }

    /// <summary>Upper-case form of <see cref="Name"/>.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>
    /// Ids from the top level down to this folder, each followed by a slash (<c>/a/b/</c>). Finds a
    /// folder's ancestors without recursion and its subtree with one prefix comparison.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>What the folder is for.</summary>
    public string? Description { get; set; }

    /// <summary>Whether grants from above stop applying here (ADR-0012).</summary>
    public bool IsRestricted { get; set; }

    /// <summary>When the folder was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Raised with every change; the API's ETag, and a guard against lost updates.</summary>
    public int Version { get; set; }

    /// <summary>Navigation to the folder above.</summary>
    public Folder? Parent { get; init; }
}
