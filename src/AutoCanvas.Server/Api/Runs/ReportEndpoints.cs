using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Reports;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Runs;

/// <summary>
/// The test and coverage reports runners uploaded for a run's jobs, under <c>/api/v1/runs/{id}</c>,
/// and the history of a test under <c>/api/v1/automations/{id}/tests</c>. Viewers of the folder read
/// them (ADR-0012).
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

        runs.MapGet("/{id:guid}/reports/{reportId:guid}", GetAsync)
            .WithName("GetRunReport")
            .Produces<ReportDetailView>()
            .WithSummary("Get a report of a run")
            .WithDescription("Its totals and, for coverage, its packages. Needs Viewer.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        runs.MapGet("/{id:guid}/reports/{reportId:guid}/tests", TestsAsync)
            .WithName("ListReportTests")
            .Produces<Page<TestCaseView>>()
            .WithSummary("List the tests of a test report")
            .WithDescription("In the order of the file, one page at a time; `outcome` (`passed`, `failed`, `skipped`) keeps only those. Needs Viewer.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        runs.MapGet("/{id:guid}/reports/{reportId:guid}/coverage/classes", ClassesAsync)
            .WithName("ListReportCoverageClasses")
            .Produces<Page<CoverageClassView>>()
            .WithSummary("List the classes of a coverage report")
            .WithDescription("In the order of the file, one page at a time, with the lines no test reached; `package` (a package's `index`) keeps only its classes. Needs Viewer.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGroup("/automations/{automationId:guid}/tests").WithTags("Reports").RequireAuthorization()
            .MapGet("/history", HistoryAsync)
            .WithName("GetTestHistory")
            .Produces<TestHistoryView>()
            .WithSummary("Get the history of a test")
            .WithDescription("Its newest results in the automation's runs, by the test's full name (`name`, as `Suite.Name`); at most `limit` (default 50, at most 200). Needs Viewer.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(Guid id, HttpContext context, RunReportService reports, CancellationToken cancellationToken) =>
        ApiResults.Ok(await reports.ListAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetAsync(Guid id, Guid reportId, HttpContext context, RunReportService reports, CancellationToken cancellationToken) =>
        ApiResults.Ok(await reports.GetAsync(context.User.ToActor(), id, reportId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> TestsAsync(Guid id, Guid reportId, string? outcome, int? limit, string? cursor, HttpContext context, RunReportService reports, CancellationToken cancellationToken) =>
        ApiResults.Ok(await reports.TestsAsync(context.User.ToActor(), id, reportId, outcome, new PageRequest(limit, cursor), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ClassesAsync(Guid id, Guid reportId, int? package, int? limit, string? cursor, HttpContext context, RunReportService reports, CancellationToken cancellationToken) =>
        ApiResults.Ok(await reports.ClassesAsync(context.User.ToActor(), id, reportId, package, new PageRequest(limit, cursor), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> HistoryAsync(Guid automationId, string? name, int? limit, HttpContext context, TestHistoryService history, CancellationToken cancellationToken) =>
        ApiResults.Ok(await history.HistoryAsync(context.User.ToActor(), automationId, name, limit, cancellationToken).ConfigureAwait(false));
}
