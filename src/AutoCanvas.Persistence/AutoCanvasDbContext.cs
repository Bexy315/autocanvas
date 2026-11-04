using AutoCanvas.Persistence.Audit;
using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Identity;
using AutoCanvas.Persistence.Instance;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Persistence.Reports;
using AutoCanvas.Persistence.Runners;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Persistence.Triggers;
using AutoCanvas.Persistence.Variables;
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

    /// <summary>Variables of the instance and of folders.</summary>
    public DbSet<Variable> Variables => Set<Variable>();

    /// <summary>Encrypted secrets of the instance, folders and automations.</summary>
    public DbSet<Secret> Secrets => Set<Secret>();

    /// <summary>The check that the configured master key is the one secrets were encrypted with.</summary>
    public DbSet<MasterKeyCheck> MasterKeyChecks => Set<MasterKeyCheck>();

    /// <summary>Runs of automations.</summary>
    public DbSet<Run> Runs => Set<Run>();

    /// <summary>The jobs of runs.</summary>
    public DbSet<RunJob> RunJobs => Set<RunJob>();

    /// <summary>Every try to execute a job; queued ones are the job queue.</summary>
    public DbSet<JobAttempt> JobAttempts => Set<JobAttempt>();

    /// <summary>The steps of job attempts, as far as they got.</summary>
    public DbSet<JobStep> JobSteps => Set<JobStep>();

    /// <summary>Log lines of job attempts, in the chunks runners sent them in.</summary>
    public DbSet<LogChunk> LogChunks => Set<LogChunk>();

    /// <summary>Report files of job attempts, read and stored normalized.</summary>
    public DbSet<Report> Reports => Set<Report>();

    /// <summary>The tests of test reports.</summary>
    public DbSet<ReportTestCase> ReportTestCases => Set<ReportTestCase>();

    /// <summary>The packages of coverage reports.</summary>
    public DbSet<ReportCoveragePackage> ReportCoveragePackages => Set<ReportCoveragePackage>();

    /// <summary>The classes of coverage reports.</summary>
    public DbSet<ReportCoverageClass> ReportCoverageClasses => Set<ReportCoverageClass>();

    /// <summary>The schedule entries of automations, with when each fires next.</summary>
    public DbSet<AutomationSchedule> AutomationSchedules => Set<AutomationSchedule>();

    /// <summary>The automations that start when another one finishes.</summary>
    public DbSet<CompletionTrigger> CompletionTriggers => Set<CompletionTrigger>();

    /// <summary>The webhook addresses of automations.</summary>
    public DbSet<AutomationWebhook> AutomationWebhooks => Set<AutomationWebhook>();

    /// <summary>Registered runners.</summary>
    public DbSet<Runner> Runners => Set<Runner>();

    /// <summary>One-time tokens runners register with.</summary>
    public DbSet<RunnerRegistrationToken> RunnerRegistrationTokens => Set<RunnerRegistrationToken>();

    /// <summary>The installation's settings changed in the web UI; at most one row.</summary>
    public DbSet<InstanceSettings> InstanceSettings => Set<InstanceSettings>();

    /// <summary>Repositories connected in folders (ADR-0021).</summary>
    public DbSet<Repository> Repositories => Set<Repository>();

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
        modelBuilder.ApplyConfiguration(new VariableConfiguration());
        modelBuilder.ApplyConfiguration(new SecretConfiguration());
        modelBuilder.ApplyConfiguration(new MasterKeyCheckConfiguration());
        modelBuilder.ApplyConfiguration(new RunConfiguration());
        modelBuilder.ApplyConfiguration(new RunJobConfiguration());
        modelBuilder.ApplyConfiguration(new JobAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new JobStepConfiguration());
        modelBuilder.ApplyConfiguration(new LogChunkConfiguration());
        modelBuilder.ApplyConfiguration(new ReportConfiguration());
        modelBuilder.ApplyConfiguration(new ReportTestCaseConfiguration());
        modelBuilder.ApplyConfiguration(new ReportCoveragePackageConfiguration());
        modelBuilder.ApplyConfiguration(new ReportCoverageClassConfiguration());
        modelBuilder.ApplyConfiguration(new AutomationScheduleConfiguration());
        modelBuilder.ApplyConfiguration(new CompletionTriggerConfiguration());
        modelBuilder.ApplyConfiguration(new AutomationWebhookConfiguration());
        modelBuilder.ApplyConfiguration(new RunnerConfiguration());
        modelBuilder.ApplyConfiguration(new RunnerRegistrationTokenConfiguration());
        modelBuilder.ApplyConfiguration(new InstanceSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new RepositoryConfiguration());
    }
}
