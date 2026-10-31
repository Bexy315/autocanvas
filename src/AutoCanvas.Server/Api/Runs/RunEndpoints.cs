using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Runs;

/// <summary>
/// Runs (ADR-0015): <c>/api/v1/automations/{id}/runs</c> to start and list them,
/// <c>/api/v1/runs/{id}</c> for one run. The folder's roles decide access (ADR-0012): Operators
/// start and cancel runs, Viewers read them.
/// </summary>
internal static class RunEndpoints
{
    public static void MapRunEndpoints(this RouteGroupBuilder api)
    {
        var ofAutomation = api.MapGroup("/automations/{automationId:guid}/runs").WithTags("Runs").RequireAuthorization();

        ofAutomation.MapPost("/", StartAsync)
            .WithName("StartRun")
            .Produces<RunView>(StatusCodes.Status201Created)
            .WithSummary("Start a run by hand")
            .WithDescription("Runs the automation's current version. Needs Operator; the automation must allow it (`triggers: manual: true`), and with `concurrency: single` no other run may be active (409).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        ofAutomation.MapGet("/", ListAsync)
            .WithName("ListRuns")
            .Produces<Page<RunSummary>>()
            .WithSummary("List an automation's runs")
            .WithDescription("Newest first, one page at a time. Needs Viewer.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var runs = api.MapGroup("/runs").WithTags("Runs").RequireAuthorization();

        runs.MapGet("/{id:guid}", GetAsync)
            .WithName("GetRun")
            .Produces<RunView>()
            .WithSummary("Get a run")
            .WithDescription("With its jobs in the order of the automation file. Needs Viewer; also works after the automation was deleted.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        runs.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithName("CancelRun")
            .Produces<RunView>()
            .WithSummary("Cancel a run")
            .WithDescription("Stops every unfinished job; `finally` jobs with `on_cancel: true` still run. Needs Operator. Cancelling a finished or already cancelled run changes nothing.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> StartAsync(Guid automationId, HttpContext context, RunService runs, CancellationToken cancellationToken) =>
        ApiResults.Created(await runs.StartAsync(context.User.ToActor(), automationId, cancellationToken).ConfigureAwait(false), run => $"/api/v1/runs/{run.Id}");

    private static async Task<IResult> ListAsync(Guid automationId, int? limit, string? cursor, HttpContext context, RunService runs, CancellationToken cancellationToken) =>
        ApiResults.Ok(await runs.ListAsync(context.User.ToActor(), automationId, new PageRequest(limit, cursor), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, RunService runs, CancellationToken cancellationToken) =>
        ApiResults.Ok(await runs.GetAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CancelAsync(Guid id, HttpContext context, RunService runs, CancellationToken cancellationToken) =>
        ApiResults.Ok(await runs.CancelAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));
}
