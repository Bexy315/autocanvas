namespace AutoCanvas.Core.Diagnostics;

/// <summary>How serious a <see cref="Diagnostic"/> is.</summary>
public enum DiagnosticSeverity
{
    /// <summary>The automation cannot be used until this is fixed.</summary>
    Error,

    /// <summary>The automation works, but probably not as intended.</summary>
    Warning,
}
