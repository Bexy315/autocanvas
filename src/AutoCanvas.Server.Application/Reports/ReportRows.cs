using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoCanvas.Core.Secrets;
using AutoCanvas.Persistence.Reports;
using AutoCanvas.Reports;

namespace AutoCanvas.Server.Application.Reports;

/// <summary>
/// A read report file as rows: the report with its totals, and its tests or packages and classes.
/// Every text is masked with the run's secrets and cut to its column.
/// </summary>
/// <param name="Report">The report.</param>
/// <param name="Details">Its tests, or its packages followed by its classes.</param>
internal sealed record ReportRows(Report Report, IReadOnlyList<object> Details)
{
    public static ReportRows Tests(ReportReceipt receipt, ReportReadResult<TestReport> read, DateTimeOffset now)
    {
        var redactor = receipt.Redactor!;
        var id = Guid.CreateVersion7();
        var cases = new List<object>();
        if (read.Report is { } tests)
        {
            for (var i = 0; i < tests.Cases.Count; i++)
            {
                var test = tests.Cases[i];
                var suite = Masked(redactor, test.Suite, ReportTestCase.MaxNameLength)!;
                var name = Masked(redactor, test.Name, ReportTestCase.MaxNameLength)!;
                var fullName = suite.Length == 0 ? name : $"{suite}.{name}";
                cases.Add(new ReportTestCase
                {
                    ReportId = id,
                    Ordinal = i,
                    Group = Masked(redactor, test.Group, ReportTestCase.MaxNameLength),
                    Suite = suite,
                    Name = name,
                    FullName = fullName,
                    NameHash = Hash(fullName),
                    Outcome = test.Outcome switch
                    {
                        TestOutcome.Passed => TestCaseOutcome.Passed,
                        TestOutcome.Skipped => TestCaseOutcome.Skipped,
                        _ => TestCaseOutcome.Failed,
                    },
                    DurationMs = (long)test.Duration.TotalMilliseconds,
                    Message = Masked(redactor, test.Message, ReportTestCase.MaxMessageLength),
                    StackTrace = Masked(redactor, test.StackTrace, ReportTestCase.MaxStackTraceLength),
                    Output = Masked(redactor, test.Output, ReportTestCase.MaxMessageLength),
                });
            }
        }

        return new ReportRows(Base(receipt, id, ReportKind.Tests, read, read.Report?.Truncated ?? false, now, read.Report?.Counts, null), cases);
    }

    public static ReportRows Coverage(ReportReceipt receipt, ReportReadResult<CoverageReport> read, DateTimeOffset now)
    {
        var redactor = receipt.Redactor!;
        var id = Guid.CreateVersion7();
        var packages = new List<object>();
        var classes = new List<object>();
        var packageList = read.Report?.Packages ?? [];
        for (var p = 0; p < packageList.Count; p++)
        {
            var package = packageList[p];
            packages.Add(new ReportCoveragePackage
            {
                ReportId = id,
                Ordinal = p,
                Name = Masked(redactor, package.Name, ReportCoveragePackage.MaxNameLength)!,
                LinesCovered = package.Counts.LinesCovered,
                LinesValid = package.Counts.LinesValid,
                BranchesCovered = package.Counts.BranchesCovered,
                BranchesValid = package.Counts.BranchesValid,
            });
            foreach (var type in package.Classes)
            {
                classes.Add(new ReportCoverageClass
                {
                    ReportId = id,
                    Ordinal = classes.Count,
                    PackageOrdinal = p,
                    Name = Masked(redactor, type.Name, ReportCoverageClass.MaxNameLength)!,
                    File = Masked(redactor, type.File, ReportCoverageClass.MaxNameLength)!,
                    LinesCovered = type.Counts.LinesCovered,
                    LinesValid = type.Counts.LinesValid,
                    BranchesCovered = type.Counts.BranchesCovered,
                    BranchesValid = type.Counts.BranchesValid,
                    Uncovered = Cut(string.Join(',', type.Uncovered), ReportCoverageClass.MaxUncoveredLength),
                });
            }
        }

        return new ReportRows(Base(receipt, id, ReportKind.Coverage, read, read.Report?.Truncated ?? false, now, null, read.Report?.Total), [.. packages, .. classes]);
    }

    /// <summary>The format's name as written in automation files.</summary>
    public static string FormatName(ReportFormat format) => format switch
    {
        ReportFormat.JUnit => "junit",
        ReportFormat.Trx => "trx",
        _ => "cobertura",
    };

    /// <summary>Cuts a text to a column, marking the cut with an ellipsis.</summary>
    public static string Cut(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");

    /// <summary>The key a test's history is looked up by.</summary>
    public static string Hash(string fullName) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fullName)));

    private static Report Base<T>(ReportReceipt receipt, Guid id, ReportKind kind, ReportReadResult<T> read, bool truncated, DateTimeOffset now, TestCounts? tests, CoverageCounts? coverage)
        where T : class
    {
        var file = receipt.File!;
        var redactor = receipt.Redactor!;
        return new Report
        {
            Id = id,
            AttemptId = file.AttemptId,
            RunId = receipt.RunId,
            AutomationId = receipt.AutomationId,
            JobKey = receipt.JobKey,
            Name = receipt.Name,
            Format = FormatName(file.Format),
            Kind = kind,
            Path = file.Path,
            Size = file.Size,
            Error = Masked(redactor, read.Error, Report.MaxErrorLength),
            Warnings = Warnings([.. read.Warnings.Select(w => redactor.Redact(w))]),
            Truncated = truncated,
            ReceivedAt = now,
            TestsTotal = tests?.Total,
            TestsPassed = tests?.Passed,
            TestsFailed = tests?.Failed,
            TestsSkipped = tests?.Skipped,
            TestsDurationMs = tests is null ? null : (long)tests.Duration.TotalMilliseconds,
            LinesCovered = coverage?.LinesCovered,
            LinesValid = coverage?.LinesValid,
            BranchesCovered = coverage?.BranchesCovered,
            BranchesValid = coverage?.BranchesValid,
        };
    }

    /// <summary>The warnings as a JSON array that fits its column, dropping the last ones if needed.</summary>
    private static string? Warnings(List<string> warnings)
    {
        while (warnings.Count > 0)
        {
            var json = JsonSerializer.Serialize(warnings);
            if (json.Length <= Report.MaxWarningsLength)
            {
                return json;
            }

            warnings.RemoveAt(warnings.Count - 1);
        }

        return null;
    }

    private static string? Masked(SecretRedactor redactor, string? text, int max) =>
        text is null ? null : Cut(redactor.Redact(text), max);
}
