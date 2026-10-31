namespace AutoCanvas.Server.Application.Reports;

/// <summary>Coverage of a package: an assembly (.NET), a directory (Python, JavaScript) or a Java package.</summary>
/// <param name="Index">Its position in the report, from 0; classes name their package by it.</param>
/// <param name="Name">The package name as the tool reports it.</param>
/// <param name="Coverage">Its lines and branches, including classes cut from the details.</param>
public sealed record CoveragePackageView(int Index, string Name, CoverageTotalsView Coverage);
