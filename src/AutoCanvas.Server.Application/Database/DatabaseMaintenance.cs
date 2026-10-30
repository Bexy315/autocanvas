using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Audit;

namespace AutoCanvas.Server.Application.Database;

/// <summary>Brings the database up to date and records that in the audit log.</summary>
/// <param name="database">The scope's context; the audit event is saved on it after migrating.</param>
/// <param name="migrator">Applies the migrations.</param>
/// <param name="audit">Receives <see cref="AuditActions.DatabaseMigrated"/>.</param>
public sealed class DatabaseMaintenance(AutoCanvasDbContext database, DatabaseMigrator migrator, IAuditLog audit)
{
    /// <summary>
    /// Applies pending migrations and returns their names. When something was applied, an audit
    /// event lists the migrations; an up-to-date database writes nothing.
    /// </summary>
    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken)
    {
        var applied = await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
        if (applied.Count > 0)
        {
            audit.Record(new AuditEntry(AuditActions.DatabaseMigrated)
            {
                Details = new Dictionary<string, object?>(StringComparer.Ordinal) { ["migrations"] = applied },
            });
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return applied;
    }
}
