namespace AutoCanvas.Core.Model;

/// <summary>Report files a job publishes.</summary>
/// <param name="Format">Format name as written: <c>junit</c>, <c>trx</c> or <c>cobertura</c>.</param>
/// <param name="Paths">Glob patterns relative to the workspace.</param>
/// <param name="Name">Label in the UI, or <see langword="null"/> for the format.</param>
public sealed record ReportDefinition(string Format, IReadOnlyList<string> Paths, string? Name);
