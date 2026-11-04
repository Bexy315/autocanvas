namespace AutoCanvas.Server.Application.Repositories;

/// <summary>A Generic Git repository to connect in a folder.</summary>
/// <param name="Address">Its clone address, <c>http</c> or <c>https</c>.</param>
public sealed record NewRepository(string Address)
{
    /// <summary>Its name in the folder; the last part of the address when unset.</summary>
    public string? Name { get; init; }

    /// <summary>The branch to read automations from; the repository's default when unset.</summary>
    public string? DefaultBranch { get; init; }

    /// <summary>The user name sent with the credential; <c>x-access-token</c> when only a token is given.</summary>
    public string? UserName { get; init; }

    /// <summary>A token or password for a private repository; stored encrypted, never shown again.</summary>
    public string? Secret { get; init; }
}
