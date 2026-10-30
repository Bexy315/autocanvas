using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Parsing;

/// <summary>The outcome of parsing an automation file.</summary>
/// <param name="Definition">The automation, or <see langword="null"/> when the file has errors.</param>
/// <param name="Diagnostics">Every problem found, in document order.</param>
public sealed record ParseResult(AutomationDefinition? Definition, IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>Whether the file was read without errors; warnings do not count.</summary>
    public bool Succeeded => Definition is not null;
}
