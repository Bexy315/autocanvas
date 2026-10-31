using AutoCanvas.Persistence.Audit;
using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Identity;
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

    /// <summary>Local accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Groups of users.</summary>
    public DbSet<Group> Groups => Set<Group>();

    /// <summary>Which user is in which group.</summary>
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();

    /// <summary>Signed-in browsers.</summary>
    public DbSet<Session> Sessions => Set<Session>();

    /// <summary>Personal API tokens.</summary>
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    /// <summary>The folder tree.</summary>
    public DbSet<Folder> Folders => Set<Folder>();

    /// <summary>Roles of users and groups on folders.</summary>
    public DbSet<FolderGrant> FolderGrants => Set<FolderGrant>();

    /// <summary>Managed automations.</summary>
    public DbSet<Automation> Automations => Set<Automation>();

    /// <summary>Every saved version of every managed automation.</summary>
    public DbSet<AutomationVersion> AutomationVersions => Set<AutomationVersion>();

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
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new GroupConfiguration());
        modelBuilder.ApplyConfiguration(new GroupMemberConfiguration());
        modelBuilder.ApplyConfiguration(new SessionConfiguration());
        modelBuilder.ApplyConfiguration(new ApiTokenConfiguration());
        modelBuilder.ApplyConfiguration(new FolderConfiguration());
        modelBuilder.ApplyConfiguration(new FolderGrantConfiguration());
        modelBuilder.ApplyConfiguration(new AutomationConfiguration());
        modelBuilder.ApplyConfiguration(new AutomationVersionConfiguration());
    }
}
