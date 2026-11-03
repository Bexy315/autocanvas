using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Variables;
using AutoCanvas.Server.Application.Variables;
using AutoCanvas.Server.Hosting;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// On start, makes sure the master key is the one the secrets were encrypted with, so a wrong key
/// stops the server at once instead of failing runs later. Runs after the migrations.
/// </summary>
internal sealed class MasterKeyCheckService(IServiceScopeFactory scopes, MasterKeySource source) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            // Without the migrations there is no table to check; the server reports that elsewhere.
            if ((await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().GetPendingAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return;
            }

            // A key generated on this start belongs to a fresh installation only (ADR-0020): a
            // database that has stored secrets needs the key it was set up with, not a new one.
            if (source is { GeneratedNow: true, DefaultFile: { } file }
                && await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().MasterKeyChecks.FirstOrDefaultAsync(c => c.Id == MasterKeyCheck.SingletonId, cancellationToken).ConfigureAwait(false) is { } check)
            {
                File.Delete(file);
                throw new InvalidOperationException(
                    $"No master key is configured, but this database's secrets are encrypted with the key {check.KeyId}. " +
                    "Configure that key with AUTOCANVAS_MASTER_KEY or AUTOCANVAS_MASTER_KEY_FILE.");
            }

            var result = await scope.ServiceProvider.GetRequiredService<MasterKeyGuard>().VerifyAsync(cancellationToken).ConfigureAwait(false);
            if (result.Error is { } error)
            {
                throw new InvalidOperationException(error.Message);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
