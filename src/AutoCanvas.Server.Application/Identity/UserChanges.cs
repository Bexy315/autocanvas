namespace AutoCanvas.Server.Application.Identity;

/// <summary>Changes to a user; properties left <see langword="null"/> stay as they are.</summary>
public sealed record UserChanges
{
    /// <summary>A new display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>A new contact address; an empty string removes it.</summary>
    public string? Email { get; init; }

    /// <summary>Grant or take away instance administration.</summary>
    public bool? IsAdmin { get; init; }

    /// <summary>Block or unblock the user. Blocking ends their sessions.</summary>
    public bool? IsDisabled { get; init; }
}
