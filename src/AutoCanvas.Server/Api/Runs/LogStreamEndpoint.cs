using System.Globalization;
using System.Text.Json;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Authentication;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Api.Runs;

/// <summary>
/// A job attempt's log as Server-Sent Events (ADR-0015): what is stored is sent at once, new chunks
/// as they arrive — woken by <see cref="LogSignal"/> on this server and by a look every
/// <see cref="PollInterval"/> for chunks stored by another one — and an <c>end</c> event once the
/// attempt has finished and everything was sent.
/// </summary>
internal static class LogStreamEndpoint
{
    /// <summary>How often the stream looks for chunks without a signal.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>How long the stream may be silent before it sends a comment to keep proxies from closing it.</summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

    public static async Task<IResult> FollowAsync(
        Guid id,
        Guid attemptId,
        long? after,
        HttpContext context,
        RunLogService logs,
        LogSignal signal,
        IOptions<JsonOptions> json,
        IHostApplicationLifetime lifetime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(json);
        var actor = context.User.ToActor();
        if (long.TryParse(context.Request.Headers["Last-Event-ID"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastEvent))
        {
            after = lastEvent;
        }

        // The first page decides between a stream and a problem (no access, unknown attempt).
        var page = await logs.ReadAsync(actor, id, attemptId, after, RunLogService.MaxChunks, cancellationToken).ConfigureAwait(false);
        if (page.Error is { } error)
        {
            return ApiResults.Problem(error);
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.ApplicationStopping);
        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        try
        {
            await StreamAsync(response, page.Value!, next => logs.ReadAsync(actor, id, attemptId, next, RunLogService.MaxChunks, stop.Token), signal, json.Value.SerializerOptions, stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The reader left or the server stops; the reader reconnects with Last-Event-ID.
        }

        return Results.Empty;
    }

    private static async Task StreamAsync(HttpResponse response, LogPage first, Func<long, Task<Application.Common.UseCaseResult<LogPage>>> read, LogSignal signal, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var current = first;
        var quietSince = DateTimeOffset.UtcNow;
        while (true)
        {
            foreach (var chunk in current.Chunks)
            {
                await response.WriteAsync($"id: {chunk.Sequence.ToString(CultureInfo.InvariantCulture)}\nevent: chunk\ndata: {JsonSerializer.Serialize(chunk, options)}\n\n", cancellationToken).ConfigureAwait(false);
            }

            if (current.IsComplete)
            {
                await response.WriteAsync($"event: end\ndata: {JsonSerializer.Serialize(new { current.IsTruncated, current.Missing }, options)}\n\n", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (current.Chunks.Count > 0)
            {
                quietSince = DateTimeOffset.UtcNow;
            }
            else if (DateTimeOffset.UtcNow - quietSince >= KeepAlive)
            {
                await response.WriteAsync(": keep-alive\n\n", cancellationToken).ConfigureAwait(false);
                quietSince = DateTimeOffset.UtcNow;
            }

            await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (current.Chunks.Count < RunLogService.MaxChunks)
            {
                await signal.WaitAsync(PollInterval, cancellationToken).ConfigureAwait(false);
            }

            var next = await read(current.Next).ConfigureAwait(false);
            if (next.Value is null)
            {
                // The run was deleted or access withdrawn while following it.
                return;
            }

            current = next.Value;
        }
    }
}
