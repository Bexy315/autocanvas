namespace AutoCanvas.Reports;

/// <summary>
/// Coverage of one class (or, for languages without classes, one file). Compiler-generated types
/// such as <c>Ns.Foo/&lt;Bar&gt;d__3</c> and nested types are counted with the class they sit in.
/// </summary>
/// <param name="Name">The class name as the tool reports it, without nested parts.</param>
/// <param name="File">The source file, as the tool reports it.</param>
/// <param name="Counts">Lines and branches of the class.</param>
/// <param name="Uncovered">Lines no test reached, in order.</param>
public sealed record CoverageClass(string Name, string File, CoverageCounts Counts, IReadOnlyList<LineRange> Uncovered);
