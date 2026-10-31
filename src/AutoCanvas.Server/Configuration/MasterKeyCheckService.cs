using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Variables;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// On start, makes sure the master key is the one the secrets were encrypted with, so a wrong key
/// stops the server at once instead of failing runs later. Runs after the migrations.
/// </summary>
internal sealed class MasterKeyCheckService(IServiceScopeFactory scopes) : IHostedService
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

            var result = await scope.ServiceProvider.GetRequiredService<MasterKeyGuard>().VerifyAsync(cancellationToken).ConfigureAwait(false);
            if (result.Error is { } error)
            {
                throw new InvalidOperationException(error.Message);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
