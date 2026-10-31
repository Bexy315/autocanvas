namespace AutoCanvas.Persistence.Reports;

/// <summary>One test of a test report, in the order of the file.</summary>
public sealed class ReportTestCase
{
    /// <summary>Column size of <see cref="Group"/>, <see cref="Suite"/> and <see cref="Name"/>.</summary>
    public const int MaxNameLength = 1000;

    /// <summary>Column size of <see cref="FullName"/>: suite, dot and name.</summary>
    public const int MaxFullNameLength = (2 * MaxNameLength) + 1;

    /// <summary>Column size of <see cref="Message"/> and <see cref="Output"/>.</summary>
    public const int MaxMessageLength = 4000;

    /// <summary>Column size of <see cref="StackTrace"/>.</summary>
    public const int MaxStackTraceLength = 8000;

    /// <summary>The report.</summary>
    public required Guid ReportId { get; init; }

    /// <summary>Position in the report, from 0.</summary>
    public required int Ordinal { get; init; }

    /// <summary>Assembly (TRX) or enclosing suite (JUnit), when the format says.</summary>
    public string? Group { get; init; }

    /// <summary>The test class or suite.</summary>
    public required string Suite { get; init; }

    /// <summary>The test's name within its suite.</summary>
    public required string Name { get; init; }

    /// <summary><c>Suite.Name</c>: the test's identity from run to run.</summary>
    public required string FullName { get; init; }

    /// <summary>
    /// SHA-256 of <see cref="FullName"/> in lower-case hex. The history of a test is looked up by it,
    /// because full names are too long for an index.
    /// </summary>
    public required string NameHash { get; init; }

    /// <summary>How it ended.</summary>
    public required TestCaseOutcome Outcome { get; init; }

    /// <summary>How long it ran, in milliseconds.</summary>
    public required long DurationMs { get; init; }

    /// <summary>Why it failed or was skipped.</summary>
    public string? Message { get; init; }

    /// <summary>The failure's stack trace.</summary>
    public string? StackTrace { get; init; }

    /// <summary>What it wrote to standard output and error.</summary>
    public string? Output { get; init; }

    /// <summary>Navigation to the report.</summary>
    public Report? Report { get; init; }
}
