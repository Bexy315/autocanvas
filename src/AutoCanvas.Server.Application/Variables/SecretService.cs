using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Variables;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Variables;

/// <summary>
/// Secrets of the instance, folders and automations (ADR-0013). Values are encrypted before they
/// reach the database and are never returned, to anyone; only runs (S2) get them.
/// </summary>
public sealed class SecretService(AutoCanvasDbContext database, SecretCipher cipher, IAuditLog audit, TimeProvider time)
{
    private readonly ScopeAccess access = new(database);

    /// <summary>The secrets of a scope by name, without values (Viewer; everyone for the instance).</summary>
    public async Task<UseCaseResult<IReadOnlyList<SecretView>>> ListAsync(Actor actor, ConfigurationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var place = await access.RequireAsync(actor, scope, FolderRole.Viewer, cancellationToken).ConfigureAwait(false);
        if (place.Error is { } denied)
        {
            return UseCase.Failure<IReadOnlyList<SecretView>>(denied);
        }

        var rows = await database.Secrets.AsNoTracking()
            .Where(s => s.Scope == place.Value!.Key)
            .OrderBy(s => s.NormalizedName)
            .Select(s => new SecretView(s.Name, s.UpdatedAt, s.UpdatedByName))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success<IReadOnlyList<SecretView>>(rows);
    }

    /// <summary>Creates or replaces a secret (Editor; admins for the instance).</summary>
    public async Task<UseCaseResult<Stored<SecretView>>> SetAsync(Actor actor, ConfigurationScope scope, string name, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if ((ValueNames.Check(name) ?? ValueNames.CheckValue(value, required: true)) is { } invalid)
        {
            return UseCase.Failure<Stored<SecretView>>(invalid);
        }

        var place = await access.RequireAsync(actor, scope, FolderRole.Editor, cancellationToken).ConfigureAwait(false);
        if (place.Error is { } denied)
        {
            return UseCase.Failure<Stored<SecretView>>(denied);
        }

        var now = time.GetUtcNow();
        var author = await database.Users.Where(u => u.Id == actor.UserId).Select(u => u.UserName).FirstAsync(cancellationToken).ConfigureAwait(false);
        var key = place.Value!.Key;
        var normalized = IdentityNames.Normalize(name);
        var ciphertext = cipher.Encrypt(key, normalized, value);
        var secret = await database.Secrets.FirstOrDefaultAsync(s => s.Scope == key && s.NormalizedName == normalized, cancellationToken).ConfigureAwait(false);
        var created = secret is null;
        if (secret is null)
        {
            secret = new Secret
            {
                Id = Guid.CreateVersion7(now),
                Scope = key,
                FolderId = place.Value.FolderId,
                AutomationId = place.Value.AutomationId,
                Name = name,
                NormalizedName = normalized,
                Ciphertext = ciphertext,
                KeyId = cipher.Key.Id,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByName = author,
            };
            database.Secrets.Add(secret);
        }
        else
        {
            secret.Name = name;
            secret.Ciphertext = ciphertext;
            secret.KeyId = cipher.Key.Id;
            secret.UpdatedAt = now;
            secret.UpdatedByName = author;
        }

        audit.Record(Entry(AuditActions.SecretSet, place.Value, name));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new Stored<SecretView>(new SecretView(secret.Name, secret.UpdatedAt, secret.UpdatedByName), created));
    }

    /// <summary>Deletes a secret (Editor; admins for the instance).</summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(Actor actor, ConfigurationScope scope, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var place = await access.RequireAsync(actor, scope, FolderRole.Editor, cancellationToken).ConfigureAwait(false);
        if (place.Error is { } denied)
        {
            return UseCase.Failure<Done>(denied);
        }

        var normalized = IdentityNames.Normalize(name ?? string.Empty);
        var secret = await database.Secrets.FirstOrDefaultAsync(s => s.Scope == place.Value!.Key && s.NormalizedName == normalized, cancellationToken).ConfigureAwait(false);
        if (secret is null)
        {
            return UseCase.Failure<Done>(UseCaseError.NotFound($"There is no secret '{name}' here."));
        }

        database.Secrets.Remove(secret);
        audit.Record(Entry(AuditActions.SecretDeleted, place.Value!, secret.Name));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }

    private static AuditEntry Entry(string action, ResolvedScope place, string name) => new(action)
    {
        TargetType = "secret",
        TargetId = $"{place.Key}/{name}",
        FolderId = place.AuditFolderId,
        Details = AuditEntry.DetailsOf(("scope", place.Key), ("name", name)),
    };
}
