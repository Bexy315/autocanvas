using AutoCanvas.Core.Diagnostics;

namespace AutoCanvas.Server.Application.Common;

/// <summary>A problem in an automation file, as the API reports it.</summary>
/// <param name="Severity">Errors keep the file from being saved; warnings do not.</param>
/// <param name="Code">Stable identifier such as <c>AC0103</c>; the user guide explains each one.</param>
/// <param name="Message">What is wrong and how to fix it.</param>
/// <param name="Path">Where in the document, such as <c>jobs.build.needs[0]</c>; empty for the whole file.</param>
/// <param name="Line">1-based line in the file, when the problem can be pinned to one.</param>
/// <param name="Column">1-based column in the file, when the problem can be pinned to one.</param>
public sealed record DiagnosticView(DiagnosticSeverity Severity, string Code, string Message, string Path, int? Line, int? Column)
{
    /// <summary>The API form of a Core diagnostic.</summary>
    public static DiagnosticView From(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return new(diagnostic.Severity, diagnostic.Code, diagnostic.Message, diagnostic.Path, diagnostic.Location?.Line, diagnostic.Location?.Column);
    }
}
