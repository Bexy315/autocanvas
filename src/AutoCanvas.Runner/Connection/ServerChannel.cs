using AutoCanvas.Protocol.Runner.V1;
using Grpc.Core;
using Grpc.Net.Client;

namespace AutoCanvas.Runner.Connection;

/// <summary>
/// The gRPC channel to the server's runner endpoint and the clients on it. One channel serves the
/// control stream and report uploads alike.
/// </summary>
internal sealed class ServerChannel(GrpcChannel channel) : IDisposable
{
    public RegistrationService.RegistrationServiceClient Registration { get; } = new(channel);

    public RunnerService.RunnerServiceClient Runner { get; } = new(channel);

    public static ServerChannel For(Uri server) => new(GrpcChannel.ForAddress(server));

    /// <summary>The metadata every <c>RunnerService</c> call carries.</summary>
    public static Metadata Authorization(RunnerCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return new Metadata { { "authorization", $"Bearer {credential.Credential}" } };
    }

    public void Dispose() => channel.Dispose();
}
