using System.Globalization;
using AutoCanvas.Forge;
using AutoCanvas.Forge.GitHub;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Forges;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// Acting as the instance's GitHub App (ADR-0022): its settings and secrets, a JWT signed with its
/// private key, and installation tokens, cached in <see cref="GitHubTokens"/>.
/// </summary>
public sealed class GitHubAccess(AutoCanvasDbContext database, GitHubClient client, SecretCipher cipher, GitHubTokens tokens, TimeProvider time) : IInstallationTokens
{
    /// <summary>The scope of the app's secrets in the cipher's associated data.</summary>
    public const string SecretScope = "github-app";

    /// <summary>The name the private key is encrypted under.</summary>
    internal const string PrivateKeyName = "private-key";

    /// <summary>The name the webhook secret is encrypted under.</summary>
    internal const string WebhookSecretName = "webhook-secret";

    /// <summary>The name the client secret is encrypted under.</summary>
    internal const string ClientSecretName = "client-secret";

    /// <summary>The REST client.</summary>
    internal GitHubClient Client => client;

    /// <summary>The cache of installation tokens.</summary>
    internal GitHubTokens Tokens => tokens;

    /// <summary>The app, or <see langword="null"/> while none is set up.</summary>
    internal Task<GitHubApp?> AppAsync(CancellationToken cancellationToken) =>
        database.GitHubApps.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    /// <summary>A JWT of the app; throws <see cref="ForgeException"/> when its key does not decrypt.</summary>
    internal string JwtOf(GitHubApp app) =>
        GitHubJwt.Create(app.AppId.ToString(CultureInfo.InvariantCulture), Decrypt(app.PrivateKeyCiphertext, PrivateKeyName), time.GetUtcNow());

    /// <summary>One of the app's secrets, decrypted; throws <see cref="ForgeException"/> when it does not decrypt.</summary>
    internal string Decrypt(string ciphertext, string name) =>
        cipher.Decrypt(SecretScope, name, ciphertext) ?? throw new ForgeException("The secrets of the GitHub App do not decrypt with the master key; enter the app again.");

    /// <summary>One of the app's secrets, encrypted.</summary>
    internal string Encrypt(string value, string name) => cipher.Encrypt(SecretScope, name, value);

    /// <inheritdoc />
    public async Task<string> TokenAsync(long installationId, CancellationToken cancellationToken)
    {
        if (tokens.Valid(installationId, time.GetUtcNow()) is { } valid)
        {
            return valid;
        }

        var app = await AppAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ForgeException("No GitHub App is set up; an admin sets it up under Administration, Forges.");
        var token = await client.CreateInstallationTokenAsync(app.ApiAddress, JwtOf(app), installationId, cancellationToken).ConfigureAwait(false);
        tokens.Store(installationId, token);
        return token.Token;
    }

    /// <inheritdoc />
    public async Task<string> CheckoutTokenAsync(long installationId, long repositoryId, CancellationToken cancellationToken)
    {
        if (tokens.Valid(installationId, time.GetUtcNow(), repositoryId) is { } valid)
        {
            return valid;
        }

        var app = await AppAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ForgeException("No GitHub App is set up; an admin sets it up under Administration, Forges.");
        var token = await client.CreateCheckoutTokenAsync(app.ApiAddress, JwtOf(app), installationId, repositoryId, cancellationToken).ConfigureAwait(false);
        tokens.Store(installationId, token, repositoryId);
        return token.Token;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Issued(long installationId) => tokens.Issued(installationId);
}
