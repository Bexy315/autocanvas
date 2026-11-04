namespace AutoCanvas.Persistence.Forges;

/// <summary>
/// The instance's GitHub App (ADR-0022), at most one row. Its private key and secrets are encrypted
/// with the master key and never returned by the API.
/// </summary>
public sealed class GitHubApp
{
    /// <summary>The only row's id.</summary>
    public const int SingletonId = 1;

    /// <summary>Column size of names and logins.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Column size of addresses.</summary>
    public const int MaxAddressLength = 500;

    /// <summary>Column size of an encrypted secret (a private key is the longest).</summary>
    public const int MaxSecretLength = 8000;

    /// <summary>Always <see cref="SingletonId"/>.</summary>
    public int Id { get; init; } = SingletonId;

    /// <summary>GitHub's id of the app.</summary>
    public required long AppId { get; set; }

    /// <summary>Its name in addresses.</summary>
    public required string Slug { get; set; }

    /// <summary>Its name.</summary>
    public required string Name { get; set; }

    /// <summary>Its OAuth client id, when known.</summary>
    public string? ClientId { get; set; }

    /// <summary>The account or organization that owns it.</summary>
    public required string OwnerLogin { get; set; }

    /// <summary>Its page on GitHub.</summary>
    public required Uri HtmlUrl { get; set; }

    /// <summary>GitHub's web address, <c>https://github.com</c> or a GitHub Enterprise Server's.</summary>
    public required Uri WebAddress { get; set; }

    /// <summary>GitHub's API address, <c>https://api.github.com</c> or <c>…/api/v3</c> of an Enterprise Server.</summary>
    public required Uri ApiAddress { get; set; }

    /// <summary>The private key in PEM, encrypted.</summary>
    public required string PrivateKeyCiphertext { get; set; }

    /// <summary>The webhook secret, encrypted.</summary>
    public required string WebhookSecretCiphertext { get; set; }

    /// <summary>The OAuth client secret, encrypted; only an app created from a manifest has one.</summary>
    public string? ClientSecretCiphertext { get; set; }

    /// <summary>When it was set up.</summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>Who set it up, kept when the user is deleted.</summary>
    public required string CreatedByName { get; set; }
}
