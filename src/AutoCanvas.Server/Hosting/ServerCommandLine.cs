using System.CommandLine;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Database;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Hosting;

/// <summary>
/// The server executable's commands. Without a command it runs the server; arguments it does not
/// know (such as <c>--urls</c> or <c>--Database:Provider=Postgres</c>) go to the host's
/// configuration, for the server and for every command.
/// </summary>
internal static class ServerCommandLine
{
    public static Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? error = null)
    {
        output ??= Console.Out;
        error ??= Console.Error;

        var root = new RootCommand("AutoCanvas server. Without a command, it runs the server.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        root.SetAction(async (parse, cancellationToken) =>
        {
            await RunServerAsync([.. parse.UnmatchedTokens], cancellationToken).ConfigureAwait(false);
            return 0;
        });

        var migrate = new Command("migrate", "Apply pending database migrations, then exit.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        migrate.SetAction((parse, cancellationToken) => MigrateAsync([.. parse.UnmatchedTokens], output, error, cancellationToken));
        root.Subcommands.Add(migrate);

        // Exceptions pass through: tests host the server by intercepting its start with one.
        return root.Parse(args).InvokeAsync(new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
            Output = output,
            Error = error,
        });
    }

    private static async Task RunServerAsync(string[] args, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddAutoCanvasServer();

        var app = builder.Build();
        await using (app.ConfigureAwait(false))
        {
            app.MapAutoCanvasServer();
            await app.RunAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> MigrateAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddAutoCanvasServer();
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

        var app = builder.Build();
        await using (app.ConfigureAwait(false))
        {
            var scope = app.Services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                try
                {
                    // Reading the options validates them, as starting the server would.
                    _ = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                    var applied = await scope.ServiceProvider.GetRequiredService<DatabaseMaintenance>()
                        .MigrateAsync(cancellationToken).ConfigureAwait(false);

                    await output.WriteLineAsync(applied.Count == 0
                        ? "The database is up to date."
                        : $"Applied {applied.Count} migration(s): {string.Join(", ", applied)}").ConfigureAwait(false);
                    return 0;
                }
                catch (OptionsValidationException exception)
                {
                    await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                    return 1;
                }
            }
        }
    }
}
