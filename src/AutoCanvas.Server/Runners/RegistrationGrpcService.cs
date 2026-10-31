using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Server.Application.Runners;
using Grpc.Core;

namespace AutoCanvas.Server.Runners;

/// <summary>
/// <c>RegistrationService</c> of the runner protocol: the one call a runner makes without a
/// credential. The registration token in the request is what authenticates it.
/// </summary>
internal sealed class RegistrationGrpcService(RunnerRegistrationService registrations) : RegistrationService.RegistrationServiceBase
{
    public override async Task<RegisterResponse> Register(RegisterRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var capabilities = ProtocolMapping.ToCapabilities(request.Capabilities);
        if (capabilities.Error is { } invalid)
        {
            throw ProtocolMapping.ToRpcException(invalid);
        }

        var registered = await registrations.RegisterAsync(request.RegistrationToken, request.Name, capabilities.Value!, context.CancellationToken).ConfigureAwait(false);
        return registered.Error is { } error
            ? throw ProtocolMapping.ToRpcException(error)
            : new RegisterResponse { RunnerId = registered.Value!.RunnerId.ToString(), Credential = registered.Value.Credential };
    }
}
