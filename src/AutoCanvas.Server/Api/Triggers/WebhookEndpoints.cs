using AutoCanvas.Server.Application.Triggers;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Triggers;

/// <summary>
/// Webhooks: <c>/api/v1/automations/{id}/webhook</c> to manage an automation's address (Operators
/// create and delete it, Viewers see it), and <c>POST /api/v1/hooks/{token}</c>, which other
/// systems call without signing in.
/// </summary>
internal static class WebhookEndpoints
{
    /// <summary>The rate limiting policy of webhook calls, partitioned by address.</summary>
    public const string RateLimit = "webhook";

    /// <summary>Headers that carry credentials and never reach the run.</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Cookie", WebhookService.SignatureHeader,
    };

    public static void MapWebhookEndpoints(this RouteGroupBuilder api)
    {
        var manage = api.MapGroup("/automations/{automationId:guid}/webhook").WithTags("Triggers").RequireAuthorization();

        manage.MapGet("/", GetAsync)
            .WithName("GetAutomationWebhook")
            .Produces<WebhookView>()
            .WithSummary("Get an automation's webhook address")
            .WithDescription("Whether it has one and calls need a signature, without the token. Needs Viewer.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        manage.MapPost("/", CreateAsync)
            .WithName("CreateAutomationWebhook")
            .Produces<CreatedWebhookView>(StatusCodes.Status201Created)
            .WithSummary("Create an automation's webhook address")
            .WithDescription("Replaces any earlier address. The token, and with `requireSignature` the signing secret, are shown only in this response. Calls start runs only while the current version has `triggers: webhook: true`. Needs Operator.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        manage.MapDelete("/", DeleteAsync)
            .WithName("DeleteAutomationWebhook")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Delete an automation's webhook address")
            .WithDescription("Calls of it fail from then on. Needs Operator.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapPost("/hooks/{token}", CallAsync)
            .WithTags("Triggers")
            .WithName("CallWebhook")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimit)
            .Produces<WebhookRunView>(StatusCodes.Status202Accepted)
            .WithSummary("Start a run through a webhook address")
            .WithDescription($"Needs no sign-in: the token in the path is the credential. The body (at most 1 MiB), the headers and the query become `trigger.body`, `trigger.headers` and `trigger.query`; a JSON body can be read into, as `trigger.body.NAME`. A signed address needs `{WebhookService.SignatureHeader}: sha256=<hex HMAC-SHA256 of the body>`.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> GetAsync(Guid automationId, HttpContext context, WebhookService webhooks, CancellationToken cancellationToken) =>
        ApiResults.Ok(await webhooks.GetAsync(context.User.ToActor(), automationId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateAsync(Guid automationId, CreateWebhookRequest? request, HttpContext context, WebhookService webhooks, CancellationToken cancellationToken) =>
        ApiResults.Created(
            await webhooks.CreateAsync(context.User.ToActor(), automationId, request?.RequireSignature ?? false, cancellationToken).ConfigureAwait(false),
            _ => $"/api/v1/automations/{automationId}/webhook");

    private static async Task<IResult> DeleteAsync(Guid automationId, HttpContext context, WebhookService webhooks, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await webhooks.DeleteAsync(context.User.ToActor(), automationId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CallAsync(string token, HttpContext context, WebhookService webhooks, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var body = await ReadAsync(request.Body, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                type: ProblemTypes.TooLarge,
                title: "Too large",
                detail: $"The body of a webhook call may have at most {WebhookService.MaxBodyBytes / 1024 / 1024} MiB.");
        }

        var call = new WebhookCall(
            body,
            request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true,
            request.Headers[WebhookService.SignatureHeader].FirstOrDefault(),
            request.Headers.Where(h => !Hidden.Contains(h.Key)).ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal));
        var started = await webhooks.TriggerAsync(token, call, cancellationToken).ConfigureAwait(false);
        return started.Error is { } error ? ApiResults.Problem(error) : TypedResults.Accepted($"/api/v1/runs/{started.Value!.RunId}", started.Value);
    }

    /// <summary>The body, or <see langword="null"/> when it is larger than a call may be.</summary>
    private static async Task<byte[]?> ReadAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > WebhookService.MaxBodyBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }
}
