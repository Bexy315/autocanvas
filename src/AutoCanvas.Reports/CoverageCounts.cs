namespace AutoCanvas.Reports;

/// <summary>Covered and coverable lines and branches.</summary>
/// <param name="LinesCovered">Lines run at least once.</param>
/// <param name="LinesValid">Lines that can be run.</param>
/// <param name="BranchesCovered">Branches taken at least once.</param>
/// <param name="BranchesValid">Branches that exist.</param>
public sealed record CoverageCounts(int LinesCovered, int LinesValid, int BranchesCovered, int BranchesValid)
{
    /// <summary>No lines and no branches.</summary>
    public static CoverageCounts Zero { get; } = new(0, 0, 0, 0);

    /// <summary>Share of lines covered, from 0 to 1; <see langword="null"/> when there are no lines.</summary>
    public double? LineRate => Rate(LinesCovered, LinesValid);

    /// <summary>Share of branches covered, from 0 to 1; <see langword="null"/> when there are no branches.</summary>
    public double? BranchRate => Rate(BranchesCovered, BranchesValid);

    /// <summary>The sum of two counts.</summary>
    /// <param name="other">The counts to add.</param>
    /// <returns>The combined counts.</returns>
    public CoverageCounts Add(CoverageCounts other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(LinesCovered + other.LinesCovered, LinesValid + other.LinesValid, BranchesCovered + other.BranchesCovered, BranchesValid + other.BranchesValid);
    }

    private static double? Rate(int covered, int valid) => valid == 0 ? null : (double)covered / valid;
}
