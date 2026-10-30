namespace AutoCanvas.Reports;

/// <summary>A coverage report, computed from its lines rather than taken from the totals a tool writes.</summary>
/// <param name="Total">Lines and branches of all packages.</param>
/// <param name="Packages">The packages, by name.</param>
/// <param name="Truncated">Whether classes beyond <see cref="ReportLimits.MaxClasses"/> were left out of the details; <paramref name="Total"/> includes them.</param>
public sealed record CoverageReport(CoverageCounts Total, IReadOnlyList<CoveragePackage> Packages, bool Truncated);
