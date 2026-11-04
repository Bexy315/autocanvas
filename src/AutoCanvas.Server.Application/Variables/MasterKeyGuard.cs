using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Variables;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Variables;

/// <summary>
/// Makes sure the configured master key is the one the secrets were encrypted with, and changes it
/// (ADR-0013). Rotation runs from the command line while the server is stopped.
/// </summary>
public sealed class MasterKeyGuard(AutoCanvasDbContext database, SecretCipher cipher, IAuditLog audit)
{
    private const string CheckScope = "master-key-check";
    private const string CheckName = "CHECK";
    private const string CheckText = "AutoCanvas master key check";

    /// <summary>
    /// Succeeds when the key matches, and on a new database, where it records the key. Fails with a
    /// message for operators when the database was set up with another key.
    /// </summary>
    public async Task<UseCaseResult<Done>> VerifyAsync(CancellationToken cancellationToken)
    {
        var check = await database.MasterKeyChecks.FirstOrDefaultAsync(c => c.Id == MasterKeyCheck.SingletonId, cancellationToken).ConfigureAwait(false);
        if (check is null)
        {
            database.MasterKeyChecks.Add(new MasterKeyCheck { Id = MasterKeyCheck.SingletonId, KeyId = cipher.Key.Id, Ciphertext = Encrypt(cipher) });
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Done.Result;
        }

        return string.Equals(cipher.Decrypt(CheckScope, CheckName, check.Ciphertext), CheckText, StringComparison.Ordinal)
            ? Done.Result
            : UseCase.Failure<Done>(UseCaseError.Conflict(
                $"The configured master key ({cipher.Key.Id}) is not the one the secrets in this database are encrypted with ({check.KeyId}). " +
                "Configure that key; to change keys, use 'admin rotate-master-key'."));
    }

    /// <summary>
    /// Re-encrypts every secret under <paramref name="newKey"/> in one transaction and returns how
    /// many there were. Changes nothing if any secret does not decrypt with the current key.
    /// </summary>
    public async Task<UseCaseResult<int>> RotateAsync(MasterKey newKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newKey);
        if (string.Equals(newKey.Id, cipher.Key.Id, StringComparison.Ordinal))
        {
            return UseCase.Failure<int>(UseCaseError.Invalid("newKey", "The new master key is the current one."));
        }

        var verified = await VerifyAsync(cancellationToken).ConfigureAwait(false);
        if (verified.Error is { } wrongKey)
        {
            return UseCase.Failure<int>(wrongKey);
        }

        var next = new SecretCipher(newKey);
        var secrets = await database.Secrets.ToListAsync(cancellationToken).ConfigureAwait(false);
        var broken = new List<string>();
        foreach (var secret in secrets)
        {
            if (cipher.Decrypt(secret.Scope, secret.NormalizedName, secret.Ciphertext) is { } value)
            {
                secret.Ciphertext = next.Encrypt(secret.Scope, secret.NormalizedName, value);
                secret.KeyId = newKey.Id;
            }
            else
            {
                broken.Add($"{secret.Scope}/{secret.Name}");
            }
        }

        // Repository credentials (ADR-0021) are secrets too.
        var repositories = await database.Repositories.Where(r => r.CredentialCiphertext != null).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var repository in repositories)
        {
            if (cipher.Decrypt(Repositories.RepositoryDiscovery.CredentialScope, repository.Id.ToString(), repository.CredentialCiphertext!) is { } value)
            {
                repository.CredentialCiphertext = next.Encrypt(Repositories.RepositoryDiscovery.CredentialScope, repository.Id.ToString(), value);
                repository.CredentialKeyId = newKey.Id;
            }
            else
            {
                broken.Add($"repository/{repository.Name}");
            }
        }

        // Webhook secrets, of automations and repositories, as well.
        var hooks = await RotateWebhooksAsync(next, broken, cancellationToken).ConfigureAwait(false);
        if (broken.Count > 0)
        {
            return UseCase.Failure<int>(UseCaseError.Conflict($"Nothing was changed: these secrets do not decrypt with the current key: {string.Join(", ", broken)}."));
        }

        var check = await database.MasterKeyChecks.FirstAsync(c => c.Id == MasterKeyCheck.SingletonId, cancellationToken).ConfigureAwait(false);
        check.KeyId = newKey.Id;
        check.Ciphertext = Encrypt(next);
        audit.Record(new AuditEntry(AuditActions.MasterKeyRotated)
        {
            Details = AuditEntry.DetailsOf(("fromKeyId", cipher.Key.Id), ("toKeyId", newKey.Id), ("secrets", secrets.Count), ("repositories", repositories.Count), ("webhooks", hooks)),
        });
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(secrets.Count);
    }

    /// <summary>Re-encrypts the webhook secrets; adds the ones that do not decrypt to <paramref name="broken"/>.</summary>
    private async Task<int> RotateWebhooksAsync(SecretCipher next, List<string> broken, CancellationToken cancellationToken)
    {
        var automations = await database.AutomationWebhooks.Where(w => w.SigningSecret != null).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var webhook in automations)
        {
            var scope = Triggers.WebhookService.CipherScope;
            if (cipher.Decrypt(scope, webhook.AutomationId.ToString(), webhook.SigningSecret!) is { } value)
            {
                database.Entry(webhook).Property(w => w.SigningSecret).CurrentValue = next.Encrypt(scope, webhook.AutomationId.ToString(), value);
            }
            else
            {
                broken.Add($"webhook/{webhook.Hint}");
            }
        }

        var repositories = await database.RepositoryWebhooks.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var webhook in repositories)
        {
            var scope = Repositories.RepositoryWebhookService.SecretScope;
            if (cipher.Decrypt(scope, webhook.RepositoryId.ToString(), webhook.SecretCiphertext) is { } value)
            {
                webhook.SecretCiphertext = next.Encrypt(scope, webhook.RepositoryId.ToString(), value);
            }
            else
            {
                broken.Add($"webhook/{webhook.Hint}");
            }
        }

        return automations.Count + repositories.Count;
    }

    private static string Encrypt(SecretCipher with) => with.Encrypt(CheckScope, CheckName, CheckText);
}
