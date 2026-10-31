using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Variables;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Variables;

/// <summary>
/// Variables of the instance and of folders (ADR-0013). Variables of one automation are part of its
/// file (<c>variables:</c>) and are versioned with it, so they are not managed here.
/// </summary>
public sealed class VariableService(AutoCanvasDbContext database, IAuditLog audit, TimeProvider time)
{
    private readonly ScopeAccess access = new(database);

    /// <summary>The variables of a scope by name; values only for callers with the Operator role.</summary>
    public async Task<UseCaseResult<IReadOnlyList<VariableView>>> ListAsync(Actor actor, ConfigurationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Unsupported(scope) is { } unsupported)
        {
            return UseCase.Failure<IReadOnlyList<VariableView>>(unsupported);
        }

        var place = await access.RequireAsync(actor, scope, FolderRole.Viewer, cancellationToken).ConfigureAwait(false);
        if (place.Error is { } denied)
        {
            return UseCase.Failure<IReadOnlyList<VariableView>>(denied);
        }

        var showValues = place.Value!.Role >= FolderRole.Operator;
        var rows = await database.Variables.AsNoTracking()
            .Where(v => v.Scope == place.Value.Key)
            .OrderBy(v => v.NormalizedName)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success<IReadOnlyList<VariableView>>([.. rows.Select(v => View(v, showValues))]);
    }

    /// <summary>Creates or replaces a variable (Editor; admins for the instance).</summary>
    public async Task<UseCaseResult<Stored<VariableView>>> SetAsync(Actor actor, ConfigurationScope scope, string name, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if ((Unsupported(scope) ?? ValueNames.Check(name) ?? ValueNames.CheckValue(value, required: false)) is { } invalid)
        {
            return UseCase.Failure<Stored<VariableView>>(invalid);
        }

        var place = await access.RequireAsync(actor, scope, FolderRole.Editor, cancellationToken).ConfigureAwait(false);
        if (place.Error is { } denied)
        {
            return UseCase.Failure<Stored<VariableView>>(denied);
        }

        var now = time.GetUtcNow();
        var author = await AuthorAsync(actor, cancellationToken).ConfigureAwait(false);
        var normalized = IdentityNames.Normalize(name);
        var variable = await database.Variables.FirstOrDefaultAsync(v => v.Scope == place.Value!.Key && v.NormalizedName == normalized, cancellationToken).ConfigureAwait(false);
        var created = variable is null;
        if (variable is null)
        {
            variable = new Variable
            {
                Id = Guid.CreateVersion7(now),
                Scope = place.Value!.Key,
                FolderId = place.Value.FolderId,
                Name = name,
                NormalizedName = normalized,
                Value = value,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByName = author,
            };
            database.Variables.Add(variable);
        }
        else
        {
            variable.Name = name;
            variable.Value = value;
            variable.UpdatedAt = now;
            variable.UpdatedByName = author;
        }

        audit.Record(Entry(AuditActions.VariableSet, place.Value!, name));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new Stored<VariableView>(View(variable, showValue: true), created));
    }

    /// <summary>Deletes a variable (Editor; admins for the instance).</summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(Actor actor, ConfigurationScope scope, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Unsupported(scope) is { } unsupported)
        {
            return UseCase.Failure<Done>(unsupported);
        }

        var place = await access.RequireAsync(actor, scope, FolderRole.Editor, cancellationToken).ConfigureAwait(false);
        if (place.Error is { } denied)
        {
            return UseCase.Failure<Done>(denied);
        }

        var normalized = IdentityNames.Normalize(name ?? string.Empty);
        var variable = await database.Variables.FirstOrDefaultAsync(v => v.Scope == place.Value!.Key && v.NormalizedName == normalized, cancellationToken).ConfigureAwait(false);
        if (variable is null)
        {
            return UseCase.Failure<Done>(UseCaseError.NotFound($"There is no variable '{name}' here."));
        }

        database.Variables.Remove(variable);
        audit.Record(Entry(AuditActions.VariableDeleted, place.Value!, variable.Name));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }

    private static UseCaseError? Unsupported(ConfigurationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.Kind == ConfigurationScopeKind.Automation
            ? UseCaseError.Invalid("scope", "Variables of one automation belong in its file, under variables:, so they are versioned with it.")
            : null;
    }

    private static VariableView View(Variable variable, bool showValue) =>
        new(variable.Name, showValue ? variable.Value : null, variable.UpdatedAt, variable.UpdatedByName);

    private static AuditEntry Entry(string action, ResolvedScope place, string name) => new(action)
    {
        TargetType = "variable",
        TargetId = $"{place.Key}/{name}",
        FolderId = place.AuditFolderId,
        Details = AuditEntry.DetailsOf(("scope", place.Key), ("name", name)),
    };

    private async Task<string> AuthorAsync(Actor actor, CancellationToken cancellationToken) =>
        await database.Users.Where(u => u.Id == actor.UserId).Select(u => u.UserName).FirstAsync(cancellationToken).ConfigureAwait(false);
}
