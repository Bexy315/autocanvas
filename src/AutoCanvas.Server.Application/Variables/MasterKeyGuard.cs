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
                $"The configured master key ({cipher.Key.Id}) is not the one this database's secrets are encrypted with ({check.KeyId}). " +
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

        if (broken.Count > 0)
        {
            return UseCase.Failure<int>(UseCaseError.Conflict($"Nothing was changed: these secrets do not decrypt with the current key: {string.Join(", ", broken)}."));
        }

        var check = await database.MasterKeyChecks.FirstAsync(c => c.Id == MasterKeyCheck.SingletonId, cancellationToken).ConfigureAwait(false);
        check.KeyId = newKey.Id;
        check.Ciphertext = Encrypt(next);
        audit.Record(new AuditEntry(AuditActions.MasterKeyRotated)
        {
            Details = AuditEntry.DetailsOf(("fromKeyId", cipher.Key.Id), ("toKeyId", newKey.Id), ("secrets", secrets.Count)),
        });
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(secrets.Count);
    }

    private static string Encrypt(SecretCipher with) => with.Encrypt(CheckScope, CheckName, CheckText);
}
