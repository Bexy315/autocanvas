using AutoCanvas.Protocol.Runner.V1;
using Grpc.Core;

namespace AutoCanvas.Server.Runners;

/// <summary>
/// <c>RunnerService</c> of the runner protocol. Every call is authenticated by
/// <see cref="RunnerAuthInterceptor"/>; each control stream is served by its own
/// <see cref="RunnerSession"/>.
/// </summary>
internal sealed class RunnerGrpcService(RunnerSessions sessions) : RunnerService.RunnerServiceBase
{
    public override Task Connect(IAsyncStreamReader<ConnectRequest> requestStream, IServerStreamWriter<ConnectResponse> responseStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var runnerId = context.GetHttpContext().User.GetRunnerId()
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "The call carries no runner credential."));
        return sessions.RunAsync(runnerId, requestStream, responseStream, context.CancellationToken);
    }

    // Reports are stored from S2 step 6 on.
    public override Task<UploadReportResponse> UploadReport(IAsyncStreamReader<UploadReportRequest> requestStream, ServerCallContext context) =>
        throw new RpcException(new Status(StatusCode.Unimplemented, "This server does not accept reports yet."));
}
