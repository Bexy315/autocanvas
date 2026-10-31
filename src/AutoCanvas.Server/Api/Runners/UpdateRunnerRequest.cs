namespace AutoCanvas.Server.Api.Runners;

/// <summary>Changes to a runner; omitted fields stay as they are.</summary>
/// <param name="Name">A new display name, unique regardless of case.</param>
/// <param name="IsPaused">True pauses the runner: it finishes its jobs but is offered no new ones. False resumes it.</param>
internal sealed record UpdateRunnerRequest(string? Name = null, bool? IsPaused = null);
