namespace AutoCanvas.Persistence.Reports;

/// <summary>Coverage of one package of a coverage report; its counts include classes cut from the details.</summary>
public sealed class ReportCoveragePackage
{
    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 1000;

    /// <summary>The report.</summary>
    public required Guid ReportId { get; init; }

    /// <summary>Position in the report, from 0.</summary>
    public required int Ordinal { get; init; }

    /// <summary>The package name as the tool reports it.</summary>
    public required string Name { get; init; }

    /// <summary>Lines run at least once.</summary>
    public required int LinesCovered { get; init; }

    /// <summary>Lines that can be run.</summary>
    public required int LinesValid { get; init; }

    /// <summary>Branches taken at least once.</summary>
    public required int BranchesCovered { get; init; }

    /// <summary>Branches that exist.</summary>
    public required int BranchesValid { get; init; }

    /// <summary>Navigation to the report.</summary>
    public Report? Report { get; init; }
}
