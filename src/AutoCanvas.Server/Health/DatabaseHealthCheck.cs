using AutoCanvas.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AutoCanvas.Server.Health;

/// <summary>
/// Ready when the database answers and has every migration applied. Descriptions stay generic:
/// errors from the driver can contain host names or user names.
/// </summary>
internal sealed class DatabaseHealthCheck(AutoCanvasDbContext database, DatabaseMigrator migrator) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await database.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return HealthCheckResult.Unhealthy("The database cannot be reached.");
            }

            var pending = await migrator.GetPendingAsync(cancellationToken).ConfigureAwait(false);
            return pending.Count == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"{pending.Count} database migration(s) are not applied; run the migrate command.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database cannot be reached.");
        }
    }
}
