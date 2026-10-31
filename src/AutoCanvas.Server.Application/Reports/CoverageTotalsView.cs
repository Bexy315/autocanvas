namespace AutoCanvas.Server.Application.Reports;

/// <summary>Covered and coverable lines and branches.</summary>
/// <param name="LinesCovered">Lines run at least once.</param>
/// <param name="LinesValid">Lines that can be run.</param>
/// <param name="BranchesCovered">Branches taken at least once.</param>
/// <param name="BranchesValid">Branches that exist.</param>
public sealed record CoverageTotalsView(int LinesCovered, int LinesValid, int BranchesCovered, int BranchesValid)
{
    /// <summary>Share of lines covered, from 0 to 1; <see langword="null"/> without lines.</summary>
    public double? LineRate => LinesValid == 0 ? null : (double)LinesCovered / LinesValid;

    /// <summary>Share of branches covered, from 0 to 1; <see langword="null"/> without branches.</summary>
    public double? BranchRate => BranchesValid == 0 ? null : (double)BranchesCovered / BranchesValid;

    /// <summary>The sum of two counts.</summary>
    /// <param name="other">The counts to add.</param>
    /// <returns>The combined counts.</returns>
    public CoverageTotalsView Add(CoverageTotalsView other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(LinesCovered + other.LinesCovered, LinesValid + other.LinesValid, BranchesCovered + other.BranchesCovered, BranchesValid + other.BranchesValid);
    }
}
