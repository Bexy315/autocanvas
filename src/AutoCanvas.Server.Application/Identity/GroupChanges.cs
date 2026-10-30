namespace AutoCanvas.Server.Application.Identity;

/// <summary>Changes to a group; properties left <see langword="null"/> stay as they are.</summary>
public sealed record GroupChanges
{
    /// <summary>A new name.</summary>
    public string? Name { get; init; }

    /// <summary>A new description; an empty string removes it.</summary>
    public string? Description { get; init; }
}
