using AutoCanvas.Server.Application.Runners;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace AutoCanvas.Server.Runners;

/// <summary>
/// Authenticates every <c>RunnerService</c> call by the runner credential in
/// <c>authorization: Bearer acr_…</c> metadata (runner protocol v1). An unknown credential — also
/// one of a deleted runner — is refused with <see cref="StatusCode.Unauthenticated"/>, which tells
/// the runner to stop. User sessions and API tokens are never accepted here.
/// </summary>
internal sealed class RunnerAuthInterceptor : Interceptor
{
    private const string BearerPrefix = "Bearer ";

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        await AuthenticateAsync(context).ConfigureAwait(false);
        return await continuation(requestStream, context).ConfigureAwait(false);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        await AuthenticateAsync(context).ConfigureAwait(false);
        await continuation(requestStream, responseStream, context).ConfigureAwait(false);
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        await AuthenticateAsync(context).ConfigureAwait(false);
        return await continuation(request, context).ConfigureAwait(false);
    }

    private static async Task AuthenticateAsync(ServerCallContext context)
    {
        var http = context.GetHttpContext();
        var authorization = context.RequestHeaders.GetValue("authorization") ?? string.Empty;
        var runner = authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? await http.RequestServices.GetRequiredService<RunnerConnectionService>()
                .AuthenticateAsync(authorization[BearerPrefix.Length..].Trim(), context.CancellationToken).ConfigureAwait(false)
            : null;
        if (runner is null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "The runner credential is unknown or was revoked."));
        }

        // The audit log reads the actor from the request's user.
        http.User = RunnerClaims.Create(runner);
    }
}
