using AutoCanvas.Persistence.Folders;

namespace AutoCanvas.Persistence.Automations;

/// <summary>
/// A managed automation (ADR-0003): it lives in a folder, and the database holds its versions. Its
/// content is the YAML of <see cref="CurrentVersion"/>; earlier versions stay readable.
/// </summary>
public sealed class Automation
{
    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Column size of <see cref="Title"/>.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Column size of <see cref="Description"/>.</summary>
    public const int MaxDescriptionLength = 500;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The folder the automation lives in; its roles decide who may use it.</summary>
    public required Guid FolderId { get; set; }

    /// <summary>The automation's key, like a file name: unique in its folder regardless of case.</summary>
    public required string Name { get; set; }

    /// <summary>Upper-case form of <see cref="Name"/>.</summary>
    public required string NormalizedName { get; set; }

    /// <summary><c>name:</c> of the current version, or <see cref="Name"/> when it has none; for lists.</summary>
    public required string Title { get; set; }

    /// <summary><c>description:</c> of the current version; for lists.</summary>
    public string? Description { get; set; }

    /// <summary>Number of the newest version; also the ETag and a guard against lost updates.</summary>
    public int CurrentVersion { get; set; }

    /// <summary>When the automation was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When anything about it last changed.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Navigation to the folder.</summary>
    public Folder? Folder { get; init; }
}
