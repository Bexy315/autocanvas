using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoCanvas.Persistence.Sqlite;

/// <summary>The AutoCanvas model on SQLite; owns the SQLite migrations.</summary>
/// <param name="options">Options built by <see cref="SqlitePersistence.UseAutoCanvasSqlite"/>.</param>
public sealed class SqliteAutoCanvasDbContext(DbContextOptions<SqliteAutoCanvasDbContext> options)
    : AutoCanvasDbContext(options)
{
    /// <summary>
    /// Switches the database to write-ahead logging, so readers do not block the writer. The mode
    /// is stored in the database file, so setting it once after migrating is enough.
    /// </summary>
    public override async Task AfterMigrateAsync(CancellationToken cancellationToken) =>
        await Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // SQLite has no date type; stored as text, DateTimeOffset values cannot be ordered or
        // compared in SQL. As a number they can, and all values are UTC (ADR-0014).
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}
