using AutoCanvas.Persistence.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoCanvas.Persistence;

/// <summary>
/// The provider-neutral model of the AutoCanvas database. Application code works with this type;
/// each database provider derives from it to add its conventions and own its migrations
/// (ADR-0010, ADR-0014).
/// </summary>
public abstract class AutoCanvasDbContext : DbContext
{
    /// <summary>Creates the context with options built by a provider.</summary>
    protected AutoCanvasDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>The append-only audit log.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <summary>
    /// Runs provider-specific maintenance after migrations have been applied, such as switching
    /// SQLite to write-ahead logging.
    /// </summary>
    public virtual Task AfterMigrateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Whether the migrations history table exists, without running a query that fails (and is
    /// logged as an error) when it does not.
    /// </summary>
    public virtual Task<bool> MigrationHistoryExistsAsync(CancellationToken cancellationToken) =>
        this.GetService<IHistoryRepository>().ExistsAsync(cancellationToken);

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration());
    }
}
