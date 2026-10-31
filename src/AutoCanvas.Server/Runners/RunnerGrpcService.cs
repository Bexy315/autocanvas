using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Server.Application.Reports;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Runners;

/// <summary>
/// <c>RunnerService</c> of the runner protocol. Every call is authenticated by
/// <see cref="RunnerAuthInterceptor"/>; each control stream is served by its own
/// <see cref="RunnerSession"/>, each report upload by <see cref="ReportUpload"/>.
/// </summary>
internal sealed class RunnerGrpcService(RunnerSessions sessions, ReportIngestion ingestion, IOptions<ReportOptions> options) : RunnerService.RunnerServiceBase
{
    public override Task Connect(IAsyncStreamReader<ConnectRequest> requestStream, IServerStreamWriter<ConnectResponse> responseStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var runnerId = context.GetHttpContext().User.GetRunnerId()
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "The call carries no runner credential."));
        return sessions.RunAsync(runnerId, requestStream, responseStream, context.CancellationToken);
    }

    public override Task<UploadReportResponse> UploadReport(IAsyncStreamReader<UploadReportRequest> requestStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var runnerId = context.GetHttpContext().User.GetRunnerId()
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "The call carries no runner credential."));
        return ReportUpload.ReceiveAsync(runnerId, requestStream, ingestion, options.Value, context.CancellationToken);
    }
}
