using System.CommandLine;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Connection;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Runner;

/// <summary>
/// The runner's commands: without one it runs; <c>register</c> exchanges a registration token for
/// the runner's credential. Arguments it does not know (such as <c>--Runner:Server=…</c>) go to the
/// configuration.
/// </summary>
internal static class RunnerCommandLine
{
    public static Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? error = null, Action<IServiceCollection>? services = null)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        var root = new RootCommand("AutoCanvas runner. Without a command, it connects to the server and runs jobs.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        root.SetAction(async (parse, cancellationToken) =>
        {
            var builder = RunnerHost.CreateBuilder([.. parse.UnmatchedTokens]);
            services?.Invoke(builder.Services);
            using var host = builder.Build();
            var exitCode = host.Services.GetRequiredService<RunnerExitCode>();
            await host.RunAsync(cancellationToken).ConfigureAwait(false);
            return exitCode.Value;
        });
        root.Subcommands.Add(RegisterCommand(output, error, services));
        return root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = error });
    }

    private static Command RegisterCommand(TextWriter output, TextWriter error, Action<IServiceCollection>? services)
    {
        var token = new Option<string>("--token") { Description = "The registration token an admin created (acrt_…).", Required = true };
        var name = new Option<string>("--name") { Description = "The runner's name, unique on the server.", Required = true };
        var register = new Command("register", "Register the runner with the server and store its credential in the data directory. Run it once per runner.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        register.Options.Add(token);
        register.Options.Add(name);
        register.SetAction(async (parse, cancellationToken) =>
        {
            var builder = RunnerHost.CreateBuilder([.. parse.UnmatchedTokens]);
            services?.Invoke(builder.Services);
            using var host = builder.Build();
            RunnerOptions options;
            try
            {
                options = host.Services.GetRequiredService<IOptions<RunnerOptions>>().Value;
            }
            catch (OptionsValidationException exception)
            {
                await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                return 1;
            }

            return await RegisterAsync(host.Services.GetRequiredService<ServerChannel>(), options, parse.GetValue(token)!, parse.GetValue(name)!, output, error, cancellationToken).ConfigureAwait(false);
        });
        return register;
    }

    private static async Task<int> RegisterAsync(ServerChannel server, RunnerOptions options, string token, string name, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (File.Exists(options.CredentialFile))
        {
            await error.WriteLineAsync($"{options.CredentialFile} exists: this runner is registered already. Delete the file to register it again.").ConfigureAwait(false);
            return 1;
        }

        RegisterResponse registered;
        try
        {
            registered = await server.Registration.RegisterAsync(
                new RegisterRequest { RegistrationToken = token, Name = name, Capabilities = RunnerCapabilities.Of(options) },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            await error.WriteLineAsync($"The server refused the registration: {exception.Status.Detail}").ConfigureAwait(false);
            return 1;
        }

        await new RunnerCredential(Guid.Parse(registered.RunnerId), registered.Credential).WriteAsync(options.CredentialFile, cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Registered runner '{name}' ({registered.RunnerId}). Its credential is in {Path.GetFullPath(options.CredentialFile)}; keep that file private.").ConfigureAwait(false);
        return 0;
    }
}
