using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Instance;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Setup;

/// <summary>
/// Opens setup mode on start when there is no account yet, and logs the address with its token
/// (ADR-0020). Runs after the migrations and the bootstrap admin, which may have created one.
/// </summary>
internal sealed partial class SetupModeService(
    IServiceScopeFactory scopes,
    SetupState state,
    IOptions<SetupOptions> options,
    IServer server,
    IHostApplicationLifetime lifetime,
    ILogger<SetupModeService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var services = scope.ServiceProvider;
            if ((await services.GetRequiredService<DatabaseMigrator>().GetPendingAsync(cancellationToken).ConfigureAwait(false)).Count > 0
                || await services.GetRequiredService<AutoCanvasDbContext>().Users.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (!options.Value.Enabled)
            {
                LogNoUsers(logger);
                return;
            }

            state.Open();
            var configured = (await services.GetRequiredService<InstanceSettingsService>().GetAsync(cancellationToken).ConfigureAwait(false)).PublicAddress.Value;

            // The listening addresses are known once the server has started.
            lifetime.ApplicationStarted.Register(() => LogSetupAddress(logger, $"{Base(configured)}/setup?token={state.Token}"));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The public address if known, otherwise the first address the server listens on.</summary>
    private string Base(Uri? configured)
    {
        if (configured is not null)
        {
            return configured.GetLeftPart(UriPartial.Authority);
        }

        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? "http://localhost:5080";
        return address.Replace("://+", "://localhost", StringComparison.Ordinal)
            .Replace("://*", "://localhost", StringComparison.Ordinal)
            .Replace("://[::]", "://localhost", StringComparison.Ordinal)
            .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal)
            .TrimEnd('/');
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AutoCanvas is not set up yet. Open {Address} in a browser to set it up; the address works until the server restarts.")]
    private static partial void LogSetupAddress(ILogger logger, string address);

    [LoggerMessage(Level = LogLevel.Warning, Message = "There is no account yet. Create the first admin with the command 'admin create <user-name>', or set AUTOCANVAS_BOOTSTRAP_ADMIN_USERNAME and AUTOCANVAS_BOOTSTRAP_ADMIN_PASSWORD.")]
    private static partial void LogNoUsers(ILogger logger);
}
