namespace AutoCanvas.Server.Application.Setup;

/// <summary>The first admin of a setup.</summary>
public sealed record SetupAdmin(string UserName, string Password)
{
    /// <summary>The name shown to other people; defaults to the user name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Contact address; information only.</summary>
    public string? Email { get; init; }
}
