namespace AutoCanvas.Server.Application.Reports;

/// <summary>Coverage of a class, or of a file in languages without classes.</summary>
/// <param name="Index">Its position in the report, from 0.</param>
/// <param name="Package">The <see cref="CoveragePackageView.Index"/> of its package.</param>
/// <param name="Name">The class name as the tool reports it.</param>
/// <param name="File">The source file.</param>
/// <param name="Coverage">Its lines and branches.</param>
/// <param name="Uncovered">Lines no test reached, such as <c>10-11,19,45</c>.</param>
public sealed record CoverageClassView(int Index, int Package, string Name, string File, CoverageTotalsView Coverage, string Uncovered);
