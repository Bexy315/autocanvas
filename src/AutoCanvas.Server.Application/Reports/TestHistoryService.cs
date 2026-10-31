using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Reports;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Reports;

/// <summary>
/// The history of a test across an automation's runs, followed by its full name (<c>Suite.Name</c>).
/// Needs Viewer on the automation's folder.
/// </summary>
public sealed class TestHistoryService(AutoCanvasDbContext database)
{
    /// <summary>Results returned when the caller does not say.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The most results one request returns.</summary>
    public const int MaxLimit = 200;

    private readonly FolderAccess access = new(database);

    /// <summary>The newest results of a test in the automation's runs.</summary>
    public async Task<UseCaseResult<TestHistoryView>> HistoryAsync(Actor actor, Guid automationId, string? fullName, int? limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrEmpty(fullName) || fullName.Length > ReportTestCase.MaxFullNameLength)
        {
            return Refused(UseCaseError.Invalid("name", $"name must be the full name of a test (Suite.Name), at most {ReportTestCase.MaxFullNameLength} characters."));
        }

        if (limit is < 1 or > MaxLimit)
        {
            return Refused(UseCaseError.Invalid("limit", $"limit must be between 1 and {MaxLimit}."));
        }

        var folder = await database.Automations.AsNoTracking().Where(a => a.Id == automationId).Select(a => (Guid?)a.FolderId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var allowed = folder is null ? null : await access.RequireAsync(actor, folder.Value, FolderRole.Viewer, track: false, cancellationToken).ConfigureAwait(false);
        if (allowed is null || allowed.Error is { Kind: UseCaseErrorKind.NotFound })
        {
            return Refused(UseCaseError.NotFound("The automation does not exist, or you may not see it."));
        }

        if (allowed.Error is { } denied)
        {
            return Refused(denied);
        }

        var hash = ReportRows.Hash(fullName);
        var rows = await database.ReportTestCases.AsNoTracking()
            .Where(c => c.NameHash == hash && c.FullName == fullName)
            .Join(database.Reports.Where(r => r.AutomationId == automationId), c => c.ReportId, r => r.Id, (c, r) => new { Case = c, Report = r })
            .Join(database.JobAttempts, x => x.Report.AttemptId, a => a.Id, (x, a) => new { x.Case, x.Report, Attempt = a.Number })
            .Join(database.Runs, x => x.Report.RunId, r => r.Id, (x, r) => new { x.Case, x.Report, x.Attempt, Run = r.Number })
            .OrderByDescending(x => x.Run).ThenByDescending(x => x.Attempt).ThenBy(x => x.Report.Name).ThenBy(x => x.Case.Ordinal)
            .Take(limit ?? DefaultLimit)
            .Select(x => new TestHistoryEntry(
                x.Report.RunId, x.Run, x.Report.JobKey, x.Report.AttemptId, x.Attempt, x.Report.Id, x.Case.Outcome, x.Case.DurationMs, x.Case.Message, x.Report.ReceivedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new TestHistoryView(
            fullName,
            rows.Count(r => r.Outcome == TestCaseOutcome.Passed),
            rows.Count(r => r.Outcome == TestCaseOutcome.Failed),
            rows.Count(r => r.Outcome == TestCaseOutcome.Skipped),
            rows));
    }

    private static UseCaseResult<TestHistoryView> Refused(UseCaseError error) => UseCase.Failure<TestHistoryView>(error);
}
