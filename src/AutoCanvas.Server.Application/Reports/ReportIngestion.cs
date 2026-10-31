using AutoCanvas.Core.Secrets;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Reports;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Reports;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Reports;

/// <summary>
/// Takes the report files runners upload (<c>UploadReport</c>): only for a job the runner runs and
/// a report the job declares, each file once, read while it arrives and stored normalized with
/// every text masked with the run's secrets. The file itself is not kept.
/// </summary>
public sealed class ReportIngestion(AutoCanvasDbContext database, ConfigurationService configuration, IOptions<ReportOptions> options, TimeProvider time)
{
    private const int BatchSize = 1000;

    /// <summary>
    /// Checks the header of an upload: the runner holds the running attempt, the job declares the
    /// report, and the file is within the limits. A file stored before comes back with its id.
    /// </summary>
    public async Task<UseCaseResult<ReportReceipt>> BeginAsync(Guid runnerId, ReportFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrWhiteSpace(file.Path) || file.Path.Length > Report.MaxPathLength)
        {
            return Refused(UseCaseError.Invalid("path", $"path must be set and at most {Report.MaxPathLength} characters long."));
        }

        var context = await AttemptContext.LoadAsync(database, configuration, time, file.AttemptId, track: false, cancellationToken).ConfigureAwait(false);
        if (context is null || context.Attempt.RunnerId != runnerId)
        {
            return Refused(UseCaseError.NotFound("This runner holds no such job."));
        }

        if (context.Attempt.Status != AttemptStatus.Running)
        {
            return Refused(new UseCaseError(UseCaseErrorKind.PreconditionFailed, "The job is not running; reports are uploaded before JobFinished."));
        }

        var format = ReportRows.FormatName(file.Format);
        if (!context.Definition.Reports.Any(r => string.Equals(r.Format, format, StringComparison.Ordinal) && string.Equals(r.Name ?? r.Format, file.Name, StringComparison.Ordinal)))
        {
            return Refused(UseCaseError.Invalid("name", $"The job declares no {format} report named '{file.Name}'."));
        }

        var name = ReportRows.Cut(file.Name, Report.MaxNameLength);
        if (await StoredAsync(file.AttemptId, name, file.Path, cancellationToken).ConfigureAwait(false) is { } stored)
        {
            return UseCase.Success(new ReportReceipt(stored));
        }

        var limits = options.Value;
        if (file.Size > limits.MaxBytesPerFile)
        {
            return Refused(TooMuch($"The file has {file.Size} bytes; a report may have at most {limits.MaxBytesPerFile}."));
        }

        if (await database.Reports.CountAsync(r => r.AttemptId == file.AttemptId, cancellationToken).ConfigureAwait(false) >= limits.MaxFilesPerJob)
        {
            return Refused(TooMuch($"The job uploaded {limits.MaxFilesPerJob} reports already, as many as a job may."));
        }

        return UseCase.Success(new ReportReceipt(file, name, context.Run.Id, context.Run.AutomationId, context.Job.Key, new SecretRedactor(context.Values.Secrets.Values)));
    }

    /// <summary>
    /// Reads the content of a file <see cref="BeginAsync"/> took and stores the report. A file that is
    /// not a report of its format is stored with the reason, so that the run shows it.
    /// </summary>
    /// <param name="receipt">What <see cref="BeginAsync"/> returned, without a stored id.</param>
    /// <param name="content">The file's content; it is read once and not closed.</param>
    /// <param name="cancellationToken">Cancels reading and storing.</param>
    /// <returns>The id of the stored report.</returns>
    public async Task<Guid> StoreAsync(ReportReceipt receipt, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(content);
        if (receipt.StoredId is { } stored)
        {
            return stored;
        }

        var file = receipt.File!;
        var report = file.Format == ReportFormat.Cobertura
            ? ReportRows.Coverage(receipt, CoverageReportReader.Read(file.Format, content), time.GetUtcNow())
            : ReportRows.Tests(receipt, TestReportReader.Read(file.Format, content), time.GetUtcNow());
        return await SaveAsync(receipt, report, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid> SaveAsync(ReportReceipt receipt, ReportRows rows, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            database.Reports.Add(rows.Report);
            try
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException exception)
            {
                // The runner uploaded the same file twice at the same moment.
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();
                return await StoredAsync(rows.Report.AttemptId, receipt.Name, rows.Report.Path, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("A report could not be stored, and no report of the same file exists.", exception);
            }

            foreach (var batch in rows.Details.Chunk(BatchSize))
            {
                database.AddRange(batch);
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                database.ChangeTracker.Clear();
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return rows.Report.Id;
    }

    private Task<Guid?> StoredAsync(Guid attemptId, string name, string path, CancellationToken cancellationToken) =>
        database.Reports.AsNoTracking()
            .Where(r => r.AttemptId == attemptId && r.Name == name && r.Path == path)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static UseCaseResult<ReportReceipt> Refused(UseCaseError error) => UseCase.Failure<ReportReceipt>(error);

    private static UseCaseError TooMuch(string message) => new(UseCaseErrorKind.TooManyRequests, message);
}
