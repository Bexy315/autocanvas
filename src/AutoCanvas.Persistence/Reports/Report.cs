namespace AutoCanvas.Persistence.Reports;

/// <summary>
/// One report file a runner uploaded for a job attempt, read and stored normalized: the totals here,
/// the tests in <see cref="ReportTestCase"/>, the coverage in <see cref="ReportCoveragePackage"/> and
/// <see cref="ReportCoverageClass"/>. The file itself is not kept.
/// </summary>
public sealed class Report
{
    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Column size of <see cref="Path"/>.</summary>
    public const int MaxPathLength = 1000;

    /// <summary>Column size of <see cref="Error"/>.</summary>
    public const int MaxErrorLength = 1000;

    /// <summary>Column size of <see cref="Warnings"/>.</summary>
    public const int MaxWarningsLength = 4000;

    /// <summary>Time-ordered identifier (UUID version 7); the protocol's <c>report_id</c>.</summary>
    public required Guid Id { get; init; }

    /// <summary>The attempt that uploaded it.</summary>
    public required Guid AttemptId { get; init; }

    /// <summary>The attempt's run, copied for reading a run's reports at once.</summary>
    public required Guid RunId { get; init; }

    /// <summary>The run's automation, copied for the history of a test.</summary>
    public Guid? AutomationId { get; init; }

    /// <summary>The attempt's job.</summary>
    public required string JobKey { get; init; }

    /// <summary>The label of the report in the automation (its <c>name</c>, or the format).</summary>
    public required string Name { get; init; }

    /// <summary>The format as written in the automation: <c>junit</c>, <c>trx</c> or <c>cobertura</c>.</summary>
    public required string Format { get; init; }

    /// <summary>Test results or coverage, decided by the format.</summary>
    public required ReportKind Kind { get; init; }

    /// <summary>Path of the file relative to the workspace, as the runner found it.</summary>
    public required string Path { get; init; }

    /// <summary>Bytes received.</summary>
    public required long Size { get; init; }

    /// <summary>Why the file could not be read; then there are no totals and no details.</summary>
    public string? Error { get; init; }

    /// <summary>What was read, but not completely, as a JSON array.</summary>
    public string? Warnings { get; init; }

    /// <summary>Details were cut at the limits; the totals still count everything.</summary>
    public bool Truncated { get; init; }

    /// <summary>When the server stored it.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>All tests, for test results.</summary>
    public int? TestsTotal { get; init; }

    /// <summary>Tests that passed.</summary>
    public int? TestsPassed { get; init; }

    /// <summary>Tests that failed.</summary>
    public int? TestsFailed { get; init; }

    /// <summary>Tests that did not run.</summary>
    public int? TestsSkipped { get; init; }

    /// <summary>Sum of the tests' durations in milliseconds.</summary>
    public long? TestsDurationMs { get; init; }

    /// <summary>Lines run at least once, for coverage.</summary>
    public int? LinesCovered { get; init; }

    /// <summary>Lines that can be run.</summary>
    public int? LinesValid { get; init; }

    /// <summary>Branches taken at least once.</summary>
    public int? BranchesCovered { get; init; }

    /// <summary>Branches that exist.</summary>
    public int? BranchesValid { get; init; }

    /// <summary>Navigation to the attempt.</summary>
    public Runs.JobAttempt? Attempt { get; init; }
}
