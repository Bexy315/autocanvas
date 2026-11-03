using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Identity;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Identity;

/// <summary>
/// On start, creates the admin from <see cref="BootstrapOptions"/> while no account exists. Runs
/// after the migrations (hosted services start in registration order); without an account, the
/// setup mode follows (ADR-0020).
/// </summary>
internal sealed partial class BootstrapAdminService(
    IServiceScopeFactory scopes,
    IOptions<BootstrapOptions> options,
    ILogger<BootstrapAdminService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var pending = await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().GetPendingAsync(cancellationToken).ConfigureAwait(false);
            if (pending.Count > 0)
            {
                LogSkippedBeforeMigration(logger);
                return;
            }

            var settings = options.Value;
            if (settings is { AdminUserName: { Length: > 0 } userName, AdminPassword: { Length: > 0 } password })
            {
                var result = await scope.ServiceProvider.GetRequiredService<UserService>()
                    .CreateFirstAdminAsync(userName, password, cancellationToken).ConfigureAwait(false);
                if (result?.Error is { } error)
                {
                    // A configuration mistake: stop instead of starting a server nobody can sign in to.
                    throw new InvalidOperationException($"The bootstrap admin from Bootstrap:AdminUserName could not be created: {error.Message}");
                }

                if (result is not null)
                {
                    LogCreated(logger, userName);
                }

                return;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the first admin '{UserName}' from the bootstrap settings; the password must be changed at the first sign-in.")]
    private static partial void LogCreated(ILogger logger, string userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The database schema is not up to date, so no bootstrap admin was created. Run the migrate command.")]
    private static partial void LogSkippedBeforeMigration(ILogger logger);
}
