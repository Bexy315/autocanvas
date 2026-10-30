namespace AutoCanvas.Server.Application.Folders;

/// <summary>Changes to a folder; properties left <see langword="null"/> stay as they are.</summary>
public sealed record FolderChanges
{
    /// <summary>A new name.</summary>
    public string? Name { get; init; }

    /// <summary>A new description; an empty string removes it.</summary>
    public string? Description { get; init; }
}
