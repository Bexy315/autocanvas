using System.Globalization;
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

        return UseCase.Success(await ReportsAsync(runId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The reports of a run with their totals, for whoever may see the run (checked by the caller).</summary>
    internal async Task<RunReportsView> ReportsAsync(Guid runId, CancellationToken cancellationToken)
    {
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
        return new RunReportsView(
            tests.Count == 0 ? null : tests.Aggregate((sum, next) => sum.Add(next)),
            coverage.Count == 0 ? null : coverage.Aggregate((sum, next) => sum.Add(next)),
            [.. ordered.Select(ReportSummaryView.Of)]);
    }

    /// <summary>A report of the run with its totals and, for coverage, its packages.</summary>
    public async Task<UseCaseResult<ReportDetailView>> GetAsync(Actor actor, Guid runId, Guid reportId, CancellationToken cancellationToken)
    {
        var found = await ReportAsync(actor, runId, reportId, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } error)
        {
            return UseCase.Failure<ReportDetailView>(error);
        }

        var packages = await database.ReportCoveragePackages.AsNoTracking()
            .Where(p => p.ReportId == reportId)
            .OrderBy(p => p.Ordinal)
            .Select(p => new CoveragePackageView(p.Ordinal, p.Name, new CoverageTotalsView(p.LinesCovered, p.LinesValid, p.BranchesCovered, p.BranchesValid)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new ReportDetailView(ReportSummaryView.Of(found.Value!), packages));
    }

    /// <summary>The tests of a test report in report order, optionally only those with one outcome.</summary>
    /// <param name="actor">Who asks; needs Viewer.</param>
    /// <param name="runId">The run.</param>
    /// <param name="reportId">The report.</param>
    /// <param name="outcome"><c>passed</c>, <c>failed</c> or <c>skipped</c>, ignoring case; all tests when <see langword="null"/>.</param>
    /// <param name="page">Which page.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    public async Task<UseCaseResult<Page<TestCaseView>>> TestsAsync(Actor actor, Guid runId, Guid reportId, string? outcome, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        TestCaseOutcome? only = null;
        if (outcome is not null)
        {
            if (!Enum.TryParse<TestCaseOutcome>(outcome, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return UseCase.Failure<Page<TestCaseView>>(UseCaseError.Invalid("outcome", "outcome must be passed, failed or skipped."));
            }

            only = parsed;
        }

        var (limit, after, invalid) = Paging(page);
        if (invalid is not null)
        {
            return UseCase.Failure<Page<TestCaseView>>(invalid);
        }

        if ((await ReportAsync(actor, runId, reportId, cancellationToken).ConfigureAwait(false)).Error is { } error)
        {
            return UseCase.Failure<Page<TestCaseView>>(error);
        }

        var rows = await database.ReportTestCases.AsNoTracking()
            .Where(c => c.ReportId == reportId && c.Ordinal > after && (only == null || c.Outcome == only))
            .OrderBy(c => c.Ordinal)
            .Take(limit + 1)
            .Select(c => new TestCaseView(c.Ordinal, c.Group, c.Suite, c.Name, c.FullName, c.Outcome, c.DurationMs, c.Message, c.StackTrace, c.Output))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(PageOf(rows, limit, r => r.Index));
    }

    /// <summary>The classes of a coverage report in report order, optionally of one package.</summary>
    /// <param name="actor">Who asks; needs Viewer.</param>
    /// <param name="runId">The run.</param>
    /// <param name="reportId">The report.</param>
    /// <param name="package">The <see cref="CoveragePackageView.Index"/> of a package; every class when <see langword="null"/>.</param>
    /// <param name="page">Which page.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    public async Task<UseCaseResult<Page<CoverageClassView>>> ClassesAsync(Actor actor, Guid runId, Guid reportId, int? package, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var (limit, after, invalid) = Paging(page);
        if (invalid is not null)
        {
            return UseCase.Failure<Page<CoverageClassView>>(invalid);
        }

        if ((await ReportAsync(actor, runId, reportId, cancellationToken).ConfigureAwait(false)).Error is { } error)
        {
            return UseCase.Failure<Page<CoverageClassView>>(error);
        }

        var rows = await database.ReportCoverageClasses.AsNoTracking()
            .Where(c => c.ReportId == reportId && c.Ordinal > after && (package == null || c.PackageOrdinal == package))
            .OrderBy(c => c.Ordinal)
            .Take(limit + 1)
            .Select(c => new CoverageClassView(c.Ordinal, c.PackageOrdinal, c.Name, c.File, new CoverageTotalsView(c.LinesCovered, c.LinesValid, c.BranchesCovered, c.BranchesValid), c.Uncovered))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(PageOf(rows, limit, r => r.Index));
    }

    /// <summary>Page size and the position to continue after; pages of a report are keyed by position.</summary>
    private static (int Limit, int After, UseCaseError? Error) Paging(PageRequest page)
    {
        var limit = page.ResolveLimit();
        if (limit.Error is { } badLimit)
        {
            return (0, 0, badLimit);
        }

        if (page.Cursor is null)
        {
            return (limit.Value, -1, null);
        }

        return int.TryParse(page.Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var after)
            ? (limit.Value, after, null)
            : (0, 0, UseCaseError.Invalid("cursor", "cursor is not a value from a previous page."));
    }

    private static Page<T> PageOf<T>(List<T> rows, int limit, Func<T, int> position) =>
        new([.. rows.Take(limit)], rows.Count > limit ? position(rows[limit - 1]).ToString(CultureInfo.InvariantCulture) : null);

    private async Task<UseCaseResult<Report>> ReportAsync(Actor actor, Guid runId, Guid reportId, CancellationToken cancellationToken)
    {
        if (await access.ViewAsync(actor, runId, cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return UseCase.Failure<Report>(denied);
        }

        var report = await database.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId && r.RunId == runId, cancellationToken).ConfigureAwait(false);
        return report is null ? UseCase.Failure<Report>(UseCaseError.NotFound("The run has no such report.")) : UseCase.Success(report);
    }
}
