using AutoCanvas.Server.Application.Reports;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Runs;

/// <summary>
/// The test and coverage reports runners uploaded for a run's jobs, under <c>/api/v1/runs/{id}</c>.
/// Viewers of the run's folder read them (ADR-0012).
/// </summary>
internal static class ReportEndpoints
{
    public static void MapReportEndpoints(this RouteGroupBuilder api)
    {
        var runs = api.MapGroup("/runs").WithTags("Reports").RequireAuthorization();

        runs.MapGet("/{id:guid}/reports", ListAsync)
            .WithName("ListRunReports")
            .Produces<RunReportsView>()
            .WithSummary("List a run's test and coverage reports")
            .WithDescription("Every report file of every job attempt, with its totals or why it could not be read, and totals over each job's latest attempt that uploaded reports. Needs Viewer.")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(Guid id, HttpContext context, RunReportService reports, CancellationToken cancellationToken) =>
        ApiResults.Ok(await reports.ListAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));
}
