using AutoCanvas.Forge;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Variables;

namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// The credential a repository is reached with: the stored token or password of a Generic Git
/// repository, decrypted, or an installation token of the GitHub App for a GitHub repository.
/// </summary>
public sealed class RepositoryCredentials(SecretCipher cipher, IInstallationTokens installations)
{
    /// <summary>The user name sent with the credential.</summary>
    public static string? UserNameOf(Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return repository.Kind == RepositoryKind.GitHub ? RepositoryAccess.TokenUserName : repository.UserName;
    }

    /// <summary>
    /// The token or password; <see langword="null"/> for none. Throws <see cref="ForgeException"/>
    /// when a GitHub repository's token cannot be had.
    /// </summary>
    public async Task<string?> SecretAsync(Repository repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (repository.Kind == RepositoryKind.GitHub)
        {
            return repository.InstallationId is { } installation
                ? await installations.TokenAsync(installation, cancellationToken).ConfigureAwait(false)
                : throw new ForgeException($"The repository '{repository.Name}' has no installation of the GitHub App.");
        }

        return repository.CredentialCiphertext is { } ciphertext ? cipher.Decrypt(RepositoryDiscovery.CredentialScope, repository.Id.ToString(), ciphertext) : null;
    }

    /// <summary>Every credential the repository may have been reached with lately, to mask in logs; nothing is fetched.</summary>
    public IEnumerable<string> Known(Repository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (repository.Kind == RepositoryKind.GitHub)
        {
            return repository.InstallationId is { } installation ? installations.Issued(installation) : [];
        }

        return repository.CredentialCiphertext is { } ciphertext && cipher.Decrypt(RepositoryDiscovery.CredentialScope, repository.Id.ToString(), ciphertext) is { } secret ? [secret] : [];
    }
}
