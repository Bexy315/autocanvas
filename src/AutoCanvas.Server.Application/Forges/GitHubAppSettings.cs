namespace AutoCanvas.Server.Application.Forges;

/// <summary>An existing GitHub App, entered by hand.</summary>
/// <param name="AppId">GitHub's id of the app.</param>
/// <param name="PrivateKey">One of its private keys, in PEM.</param>
/// <param name="WebhookSecret">The secret of its webhook.</param>
public sealed record GitHubAppSettings(long AppId, string PrivateKey, string WebhookSecret)
{
    /// <summary>The web address of a GitHub Enterprise Server; <c>https://github.com</c> when unset.</summary>
    public string? WebAddress { get; init; }

    /// <summary>The API address, when it is not the usual one for <see cref="WebAddress"/>.</summary>
    public string? ApiAddress { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"GitHubAppSettings {{ AppId = {AppId}, WebAddress = {WebAddress} }}";
}
