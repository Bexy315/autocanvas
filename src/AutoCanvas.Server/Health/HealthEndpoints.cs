using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AutoCanvas.Server.Health;

/// <summary>
/// Liveness and readiness probes. Liveness says the process runs; readiness says it can serve
/// requests (checks tagged <see cref="ReadyTag"/>, such as the database).
/// </summary>
internal static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    public static IEndpointRouteBuilder MapAutoCanvasHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteAsync,
        }).AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteAsync,
        }).AllowAnonymous();

        return endpoints;
    }

    // Status per check only: descriptions and exceptions may contain connection details.
    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new HealthResponse(
            report.Status.ToString(),
            report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString(), StringComparer.Ordinal));
        return JsonSerializer.SerializeAsync(context.Response.Body, body, JsonSerializerOptions.Web, context.RequestAborted);
    }

    private sealed record HealthResponse(string Status, IReadOnlyDictionary<string, string> Checks);
}
