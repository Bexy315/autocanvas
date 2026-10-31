using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Reports;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Runs;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Reports;

/// <summary>Reading the test and coverage reports of runs. Needs Viewer on the run's folder, like the run.</summary>
public sealed class RunReportService(AutoCanvasDbContext database)
{
    private readonly RunAccess access = new(database);

    /// <summary>The reports of a run, with totals over each job's latest attempt that uploaded any.</summary>
    public async Task<UseCaseResult<RunReportsView>> ListAsync(Actor actor, Guid runId, CancellationToken cancellationToken)
    {
        if (await access.ViewAsync(actor, runId, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return UseCase.Failure<RunReportsView>(denied);
        }

        var reports = await database.Reports.AsNoTracking().Where(r => r.RunId == runId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var attemptIds = reports.Select(r => r.AttemptId).Distinct().ToList();
        var numbers = await database.JobAttempts.AsNoTracking()
            .Where(a => attemptIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Number, cancellationToken).ConfigureAwait(false);
        var ordered = reports.OrderBy(r => r.JobKey, StringComparer.Ordinal).ThenBy(r => numbers[r.AttemptId]).ThenBy(r => r.ReceivedAt).ToList();
        var latest = ordered.GroupBy(r => r.JobKey, StringComparer.Ordinal)
            .SelectMany(job => job.Where(r => numbers[r.AttemptId] == job.Max(j => numbers[j.AttemptId])))
            .Select(ReportSummaryView.Of)
            .ToList();

        var tests = latest.Select(r => r.Tests).OfType<TestTotalsView>().ToList();
        var coverage = latest.Where(r => r.Kind == ReportKind.Coverage).Select(r => r.Coverage).OfType<CoverageTotalsView>().ToList();
        return UseCase.Success(new RunReportsView(
            tests.Count == 0 ? null : tests.Aggregate((sum, next) => sum.Add(next)),
            coverage.Count == 0 ? null : coverage.Aggregate((sum, next) => sum.Add(next)),
            [.. ordered.Select(ReportSummaryView.Of)]));
    }
}
