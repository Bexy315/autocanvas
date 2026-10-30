using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Persistence.Postgres;

/// <summary>The AutoCanvas model on PostgreSQL; owns the PostgreSQL migrations.</summary>
/// <param name="options">Options built by <see cref="PostgresPersistence.UseAutoCanvasPostgres"/>.</param>
public sealed class PostgresAutoCanvasDbContext(DbContextOptions<PostgresAutoCanvasDbContext> options)
    : AutoCanvasDbContext(options)
{
    /// <summary>
    /// Looks the table up in the catalog: Npgsql's own check queries the table and logs the
    /// failure as an error when it is missing.
    /// </summary>
    public override async Task<bool> MigrationHistoryExistsAsync(CancellationToken cancellationToken) =>
        await Database.SqlQueryRaw<bool>(
                "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_tables WHERE schemaname = current_schema() AND tablename = '__EFMigrationsHistory') AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);
}
