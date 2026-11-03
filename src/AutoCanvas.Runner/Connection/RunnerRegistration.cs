using AutoCanvas.Protocol.Runner.V1;
using Grpc.Core;

namespace AutoCanvas.Runner.Connection;

/// <summary>
/// Exchanges a registration token for the runner's credential and stores it, for the
/// <c>register</c> command and for a runner that registers itself from a token file (ADR-0020).
/// </summary>
internal static class RunnerRegistration
{
    /// <summary>The name a runner registers with when none is configured.</summary>
    public static string NameOf(RunnerOptions options) =>
        string.IsNullOrWhiteSpace(options.Name) ? Environment.MachineName : options.Name.Trim();

    /// <summary>Registers and writes the credential file; throws <see cref="RpcException"/> when the server refuses or cannot be reached.</summary>
    public static async Task<RunnerCredential> RegisterAsync(ServerChannel server, RunnerOptions options, string token, string name, CancellationToken cancellationToken)
    {
        var registered = await server.Registration.RegisterAsync(
            new RegisterRequest { RegistrationToken = token, Name = name, Capabilities = RunnerCapabilities.Of(options) },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var credential = new RunnerCredential(Guid.Parse(registered.RunnerId), registered.Credential);
        await credential.WriteAsync(options.CredentialFile, cancellationToken).ConfigureAwait(false);
        return credential;
    }

    /// <summary>Whether the server could not be reached, so trying again later may work.</summary>
    public static bool IsTransient(RpcException exception) =>
        exception.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.ResourceExhausted;
}
