using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoCanvas.Persistence;

/// <summary>Brings a database to the current schema, whichever provider it uses.</summary>
/// <param name="context">The context of the configured provider.</param>
public sealed class DatabaseMigrator(AutoCanvasDbContext context)
{
    /// <summary>
    /// Applies every pending migration and returns their names in the order they were applied.
    /// Safe to call when several server instances start at once: EF Core takes a migration lock.
    /// </summary>
    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken)
    {
        var pending = await GetPendingAsync(cancellationToken).ConfigureAwait(false);

        // On a fresh PostgreSQL database EF Core finds out that the history table is missing by
        // querying it, which is logged as an error. Creating it first keeps first starts clean.
        if (!await context.MigrationHistoryExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            await context.GetService<IHistoryRepository>().CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
        }

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await context.AfterMigrateAsync(cancellationToken).ConfigureAwait(false);
        return pending;
    }

    /// <summary>Names of the migrations the database still lacks.</summary>
    public async Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken cancellationToken)
    {
        // Asking EF Core for pending migrations queries the history table, which fails (and is
        // logged as an error) on a database that was never migrated.
        if (!await context.MigrationHistoryExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return [.. context.Database.GetMigrations()];
        }

        return [.. await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)];
    }
}
