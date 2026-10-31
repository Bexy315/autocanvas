namespace AutoCanvas.Server.Application.Runs;

/// <summary>Report files to collect when the job ends.</summary>
/// <param name="Format">Format as written: <c>junit</c>, <c>trx</c> or <c>cobertura</c>.</param>
/// <param name="Paths">Glob patterns relative to the workspace.</param>
/// <param name="Name">Label of the report.</param>
public sealed record OfferedReport(string Format, IReadOnlyList<string> Paths, string Name);
