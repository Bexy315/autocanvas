namespace AutoCanvas.Persistence.Reports;

/// <summary>Coverage of one class (or file) of a coverage report.</summary>
public sealed class ReportCoverageClass
{
    /// <summary>Column size of <see cref="Name"/> and <see cref="File"/>.</summary>
    public const int MaxNameLength = 1000;

    /// <summary>Column size of <see cref="Uncovered"/>.</summary>
    public const int MaxUncoveredLength = 8000;

    /// <summary>The report.</summary>
    public required Guid ReportId { get; init; }

    /// <summary>Position in the report, from 0.</summary>
    public required int Ordinal { get; init; }

    /// <summary>The <see cref="ReportCoveragePackage.Ordinal"/> of its package.</summary>
    public required int PackageOrdinal { get; init; }

    /// <summary>The class name as the tool reports it.</summary>
    public required string Name { get; init; }

    /// <summary>The source file.</summary>
    public required string File { get; init; }

    /// <summary>Lines run at least once.</summary>
    public required int LinesCovered { get; init; }

    /// <summary>Lines that can be run.</summary>
    public required int LinesValid { get; init; }

    /// <summary>Branches taken at least once.</summary>
    public required int BranchesCovered { get; init; }

    /// <summary>Branches that exist.</summary>
    public required int BranchesValid { get; init; }

    /// <summary>Lines no test reached, such as <c>10-11,19,45</c>; cut with an ellipsis when very long.</summary>
    public required string Uncovered { get; init; }

    /// <summary>Navigation to the report.</summary>
    public Report? Report { get; init; }
}
