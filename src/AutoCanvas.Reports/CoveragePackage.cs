namespace AutoCanvas.Reports;

/// <summary>Coverage of a package: an assembly (.NET), a directory (Python, JavaScript) or a Java package.</summary>
/// <param name="Name">The package name as the tool reports it.</param>
/// <param name="Counts">Lines and branches of all its classes.</param>
/// <param name="Classes">Its classes, by name.</param>
public sealed record CoveragePackage(string Name, CoverageCounts Counts, IReadOnlyList<CoverageClass> Classes);
