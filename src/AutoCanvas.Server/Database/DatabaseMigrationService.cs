using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Database;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Database;

/// <summary>
/// Applies pending migrations while the host starts, before the server accepts requests, unless
/// <see cref="DatabaseOptions.MigrateOnStart"/> is off.
/// </summary>
internal sealed partial class DatabaseMigrationService(
    IServiceScopeFactory scopes,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseMigrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Provider == DatabaseProvider.Sqlite)
        {
            LogSqliteInUse(logger);
        }

        if (!options.Value.MigrateOnStart)
        {
            return;
        }

        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var applied = await scope.ServiceProvider.GetRequiredService<DatabaseMaintenance>()
                .MigrateAsync(cancellationToken).ConfigureAwait(false);
            if (applied.Count > 0)
            {
                LogMigrated(logger, applied.Count, applied);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Using SQLite. It suits a single server; use PostgreSQL for production and for several server instances.")]
    private static partial void LogSqliteInUse(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} database migration(s): {Migrations}")]
    private static partial void LogMigrated(ILogger logger, int count, IReadOnlyList<string> migrations);
}
