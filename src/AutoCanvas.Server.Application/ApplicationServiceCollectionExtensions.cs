using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Automations;
using AutoCanvas.Server.Application.Variables;
using AutoCanvas.Server.Application.Database;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Reports;
using AutoCanvas.Server.Application.Runners;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoCanvas.Server.Application;

/// <summary>Registers the server's use cases. A database provider must be registered as well.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the use cases and the audit log. Unless the host registers its own
    /// <see cref="IAuditContext"/>, changes are attributed to the system.
    /// </summary>
    public static IServiceCollection AddAutoCanvasApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAuditContext, SystemAuditContext>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<DatabaseMigrator>();
        services.AddScoped<DatabaseMaintenance>();
        services.TryAddSingleton(new PasswordHashingOptions());
        services.AddSingleton<PasswordHasher>();
        services.AddScoped<UserService>();
        services.AddScoped<DirectoryService>();
        services.AddScoped<GroupService>();
        services.AddOptions<SessionLifetimeOptions>();
        services.AddSingleton<LoginThrottle>();
        services.AddScoped<SessionService>();
        services.AddScoped<AccountService>();
        services.AddScoped<SignInService>();
        services.AddScoped<TokenService>();
        services.AddScoped<FolderService>();
        services.AddScoped<FolderGrantService>();
        services.AddScoped<AutomationService>();

        // The master key itself is registered by the host, which reads it from its configuration.
        services.AddSingleton(provider => new SecretCipher(provider.GetRequiredService<MasterKey>()));
        services.AddScoped<VariableService>();
        services.AddScoped<SecretService>();
        services.AddScoped<ConfigurationService>();
        services.AddScoped<MasterKeyGuard>();

        services.AddOptions<AuditOptions>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<AuditRetention>();

        AddRuns(services);

        services.AddOptions<RunnerOptions>();
        services.AddScoped<RunnerRegistrationService>();
        services.AddScoped<RunnerService>();
        services.AddScoped<RunnerConnectionService>();
        return services;
    }

    /// <summary>Runs, the job queue, logs and reports.</summary>
    private static void AddRuns(IServiceCollection services)
    {
        services.AddScoped<RunService>();
        services.AddOptions<RunOptions>();
        services.AddScoped<RunRetention>();
        services.AddOptions<QueueOptions>();
        services.AddScoped<JobDispatcher>();
        services.AddScoped<JobOfferBuilder>();
        services.AddScoped<JobProgress>();
        services.AddOptions<LogOptions>();
        services.AddSingleton<LogSignal>();
        services.AddScoped<LogIngestion>();
        services.AddScoped<RunLogService>();
        services.AddOptions<ReportOptions>();
        services.AddScoped<ReportIngestion>();
        services.AddScoped<RunReportService>();
        services.AddScoped<TestHistoryService>();
        services.AddScoped<TriggerDispatcher>();
        services.AddScoped<AutomationTriggerService>();
        services.AddScoped<WebhookService>();
    }
}
