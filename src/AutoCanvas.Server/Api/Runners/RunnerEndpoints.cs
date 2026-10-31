using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Runners;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Runners;

/// <summary>
/// <c>/api/v1/runners</c> and <c>/api/v1/runner-registration-tokens</c>: registering, pausing and
/// removing runners. Admins only. Runners themselves never use the HTTP API; they talk gRPC on the
/// runner port (ADR-0017).
/// </summary>
internal static class RunnerEndpoints
{
    public static void MapRunnerEndpoints(this RouteGroupBuilder api)
    {
        var tokens = api.MapGroup("/runner-registration-tokens").WithTags("Runners").RequireAuthorization(AutoCanvasPolicies.Admin);

        tokens.MapPost("/", CreateTokenAsync)
            .WithName("CreateRunnerRegistrationToken")
            .Produces<CreatedRegistrationToken>(StatusCodes.Status201Created)
            .WithSummary("Create a runner registration token")
            .WithDescription("A one-time token for a runner's register command. The token is in this response only; the server keeps its hash.")
            .ProducesValidationProblem();

        tokens.MapGet("/", ListTokensAsync)
            .WithName("ListRunnerRegistrationTokens")
            .WithSummary("List runner registration tokens")
            .WithDescription("Newest first, used and expired ones included, without their secrets.");

        tokens.MapDelete("/{id:guid}", DeleteTokenAsync)
            .WithName("DeleteRunnerRegistrationToken")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Delete a runner registration token")
            .WithDescription("An unused token can no longer be used. A runner that registered with it stays registered.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var runners = api.MapGroup("/runners").WithTags("Runners").RequireAuthorization(AutoCanvasPolicies.Admin);

        runners.MapGet("/", ListAsync)
            .WithName("ListRunners")
            .Produces<Page<RunnerView>>()
            .WithSummary("List runners")
            .WithDescription("Ordered by name, one page at a time, with what each announced and whether it is online.")
            .ProducesValidationProblem();

        runners.MapGet("/{id:guid}", GetAsync)
            .WithName("GetRunner")
            .Produces<RunnerView>()
            .WithSummary("Get a runner")
            .WithDescription("The ETag header is the runner's version, for If-Match.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        runners.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdateRunner")
            .Produces<RunnerView>()
            .WithSummary("Rename, pause or resume a runner")
            .WithDescription("A paused runner stays connected and finishes its jobs but is offered no new ones. Send If-Match with the ETag to avoid overwriting someone else's change (412).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        runners.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteRunner")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Delete a runner")
            .WithDescription("Revokes its credential and tells a connected runner not to reconnect. Jobs it still held are offered again or fail, as their retry settings say.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
    }

    private static async Task<IResult> CreateTokenAsync(CreateRegistrationTokenRequest request, RunnerRegistrationService registrations, CancellationToken cancellationToken) =>
        ApiResults.Created(
            await registrations.CreateTokenAsync(request.Description, request.ExpiresAt, cancellationToken).ConfigureAwait(false),
            token => $"/api/v1/runner-registration-tokens/{token.Details.Id}");

    private static Task<IReadOnlyList<RegistrationTokenView>> ListTokensAsync(RunnerRegistrationService registrations, CancellationToken cancellationToken) =>
        registrations.ListTokensAsync(cancellationToken);

    private static async Task<IResult> DeleteTokenAsync(Guid id, RunnerRegistrationService registrations, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await registrations.DeleteTokenAsync(id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ListAsync(int? limit, string? cursor, RunnerService runners, CancellationToken cancellationToken) =>
        ApiResults.Ok(await runners.ListAsync(new PageRequest(limit, cursor), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, RunnerService runners, CancellationToken cancellationToken)
    {
        var result = await runners.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ApiResults.Ok(result).WithETag(context, result, r => r.Version);
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateRunnerRequest request, HttpContext context, RunnerService runners, CancellationToken cancellationToken)
    {
        var result = await runners.UpdateAsync(id, new RunnerChanges(request.Name, request.IsPaused), EntityTags.ExpectedVersion(context.Request), cancellationToken).ConfigureAwait(false);
        return ApiResults.Ok(result).WithETag(context, result, r => r.Version);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, RunnerService runners, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await runners.DeleteAsync(id, EntityTags.ExpectedVersion(context.Request), cancellationToken).ConfigureAwait(false));
}
