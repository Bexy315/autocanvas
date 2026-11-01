using AutoCanvas.Persistence.Triggers;

namespace AutoCanvas.Server.Application.Triggers;

/// <summary>The automation another one follows (<c>automation_completed</c>).</summary>
/// <param name="FolderId">Its folder.</param>
/// <param name="Automation">Its name; <see langword="null"/> while no automation has that name there.</param>
/// <param name="On">Which of its results start a run.</param>
public sealed record CompletionView(Guid FolderId, string? Automation, CompletionOutcome On);
