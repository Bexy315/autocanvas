using AutoCanvas.Server.Application.Common;

namespace AutoCanvas.Server.Application.Automations;

/// <summary>What checking a file without saving it found.</summary>
/// <param name="Valid">Whether saving it would be accepted: no errors, only warnings if any.</param>
/// <param name="Diagnostics">Every error and warning, in the order they appear in the file.</param>
public sealed record AutomationValidationView(bool Valid, IReadOnlyList<DiagnosticView> Diagnostics);
