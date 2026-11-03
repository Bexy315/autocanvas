using System.CommandLine;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Database;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Variables;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Hosting;

/// <summary>
/// The server executable's commands. Without a command it runs the server; arguments it does not
/// know (such as <c>--urls</c> or <c>--Database:Provider=Postgres</c>) go to the host's
/// configuration, for the server and for every command.
/// </summary>
internal static class ServerCommandLine
{
    public static Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? error = null, TextReader? input = null)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        input ??= Console.In;

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
        migrate.SetAction((parse, cancellationToken) => RunInScopeAsync(
            [.. parse.UnmatchedTokens], error, migrate: false, (services, ct) => MigrateAsync(services, output, ct), cancellationToken));
        root.Subcommands.Add(migrate);
        root.Subcommands.Add(AdminCommand(output, error, input));

        // Exceptions pass through: tests host the server by intercepting its start with one.
        return root.Parse(args).InvokeAsync(new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
            Output = output,
            Error = error,
        });
    }

    private static Command AdminCommand(TextWriter output, TextWriter error, TextReader input)
    {
        var userName = new Argument<string>("user-name") { Description = "The name the admin signs in with." };
        var displayName = new Option<string?>("--display-name") { Description = "The name shown to other people." };
        var passwordStdin = new Option<bool>("--password-stdin")
        {
            Description = "Read the password from the first line of standard input instead of generating a one-time password.",
        };
        var create = new Command("create", "Create an admin account. Without --password-stdin, prints a one-time password that must be changed at the first sign-in.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        create.Arguments.Add(userName);
        create.Options.Add(displayName);
        create.Options.Add(passwordStdin);
        create.SetAction((parse, cancellationToken) => RunInScopeAsync(
            [.. parse.UnmatchedTokens],
            error,
            migrate: true,
            async (services, ct) =>
            {
                var typed = parse.GetValue(passwordStdin);
                var password = typed ? await input.ReadLineAsync(ct).ConfigureAwait(false) ?? string.Empty : PasswordGenerator.Create();
                var result = await services.GetRequiredService<UserService>().CreateAsync(
                    new NewUser(parse.GetValue(userName)!, password)
                    {
                        DisplayName = parse.GetValue(displayName),
                        IsAdmin = true,
                        MustChangePassword = !typed,
                    },
                    ct).ConfigureAwait(false);
                return await ReportAsync(result, output, error, $"Created admin '{result.Value?.UserName}'.", typed ? null : password).ConfigureAwait(false);
            },
            cancellationToken));

        return new Command("admin", "Manage accounts and the master key from the command line, e.g. to create the first admin.")
        {
            create,
            ResetPasswordCommand(output, error),
            GenerateMasterKeyCommand(output),
            RotateMasterKeyCommand(output, error, input),
        };
    }

    private static Command ResetPasswordCommand(TextWriter output, TextWriter error)
    {
        var resetUserName = new Argument<string>("user-name") { Description = "The user whose password to reset." };
        var reset = new Command("reset-password", "Give a user a new one-time password, e.g. when the last admin is locked out. Ends their sessions.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        reset.Arguments.Add(resetUserName);
        reset.SetAction((parse, cancellationToken) => RunInScopeAsync(
            [.. parse.UnmatchedTokens],
            error,
            migrate: true,
            async (services, ct) =>
            {
                var users = services.GetRequiredService<UserService>();
                var user = await users.FindByUserNameAsync(parse.GetValue(resetUserName)!, ct).ConfigureAwait(false);
                if (user.Error is not null)
                {
                    return await ReportAsync(user, output, error, string.Empty, null).ConfigureAwait(false);
                }

                var password = PasswordGenerator.Create();
                var result = await users.ResetPasswordAsync(user.Value!.Id, password, ct).ConfigureAwait(false);
                return await ReportAsync(result, output, error, $"Reset the password of '{user.Value.UserName}'.", password).ConfigureAwait(false);
            },
            cancellationToken));

        return reset;
    }

    private static Command GenerateMasterKeyCommand(TextWriter output)
    {
        var generate = new Command("generate-master-key", "Print a new random master key for AUTOCANVAS_MASTER_KEY. Back it up: without it, secrets cannot be recovered.");
        generate.SetAction(async (_, cancellationToken) =>
        {
            await output.WriteLineAsync(MasterKey.Generate().Text.AsMemory(), cancellationToken).ConfigureAwait(false);
            return 0;
        });
        return generate;
    }

    private static Command RotateMasterKeyCommand(TextWriter output, TextWriter error, TextReader input)
    {
        var keyFile = new Option<FileInfo?>("--new-key-file") { Description = "A file holding the new master key." };
        var keyStdin = new Option<bool>("--new-key-stdin") { Description = "Read the new master key from the first line of standard input." };
        var rotate = new Command(
            "rotate-master-key",
            "Re-encrypt every secret under a new master key. Stop the server first; the current key comes from its usual settings. Afterwards, start the server with the new key.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        rotate.Options.Add(keyFile);
        rotate.Options.Add(keyStdin);
        rotate.SetAction((parse, cancellationToken) => RunInScopeAsync(
            [.. parse.UnmatchedTokens],
            error,
            migrate: true,
            async (services, ct) =>
            {
                var file = parse.GetValue(keyFile);
                var fromStdin = parse.GetValue(keyStdin);
                if ((file is null) == !fromStdin)
                {
                    await error.WriteLineAsync("Give the new key with either --new-key-file or --new-key-stdin.").ConfigureAwait(false);
                    return 1;
                }

                var text = fromStdin ? await input.ReadLineAsync(ct).ConfigureAwait(false) : await File.ReadAllTextAsync(file!.FullName, ct).ConfigureAwait(false);
                if (MasterKey.Parse(text, out var problem) is not { } newKey)
                {
                    await error.WriteLineAsync($"The new key cannot be used: {problem}").ConfigureAwait(false);
                    return 1;
                }

                var result = await services.GetRequiredService<MasterKeyGuard>().RotateAsync(newKey, ct).ConfigureAwait(false);
                return await ReportAsync(
                    result,
                    output,
                    error,
                    $"Re-encrypted {result.Value} secret(s) with master key {newKey.Id}. Start the server with the new key; the old one no longer works.",
                    null).ConfigureAwait(false);
            },
            cancellationToken));
        return rotate;
    }

    private static async Task<int> ReportAsync<T>(UseCaseResult<T> result, TextWriter output, TextWriter error, string success, string? oneTimePassword)
    {
        if (result.Error is { } failure)
        {
            await error.WriteLineAsync(failure.Message).ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync(success).ConfigureAwait(false);
        if (oneTimePassword is not null)
        {
            await output.WriteLineAsync($"One-time password: {oneTimePassword}").ConfigureAwait(false);
            await output.WriteLineAsync("It must be changed at the first sign-in. It is shown only once.").ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task RunServerAsync(string[] args, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddAutoCanvasServer(runsServer: true);

        var app = builder.Build();
        await using (app.ConfigureAwait(false))
        {
            app.MapAutoCanvasServer();
            await app.RunAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> MigrateAsync(IServiceProvider services, TextWriter output, CancellationToken cancellationToken)
    {
        var applied = await services.GetRequiredService<DatabaseMaintenance>().MigrateAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(applied.Count == 0
            ? "The database is up to date."
            : $"Applied {applied.Count} migration(s): {string.Join(", ", applied)}").ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Builds the host with the server's configuration without starting it, and runs a command in a
    /// service scope. With <paramref name="migrate"/>, the schema is brought up to date first unless
    /// <c>Database:MigrateOnStart</c> is off, as starting the server would.
    /// </summary>
    private static async Task<int> RunInScopeAsync(
        string[] args,
        TextWriter error,
        bool migrate,
        Func<IServiceProvider, CancellationToken, Task<int>> command,
        CancellationToken cancellationToken)
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
                    var database = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                    if (migrate && database.MigrateOnStart)
                    {
                        await scope.ServiceProvider.GetRequiredService<DatabaseMaintenance>().MigrateAsync(cancellationToken).ConfigureAwait(false);
                    }

                    return await command(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
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
