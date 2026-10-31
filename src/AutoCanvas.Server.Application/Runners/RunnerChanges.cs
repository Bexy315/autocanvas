namespace AutoCanvas.Server.Application.Runners;

/// <summary>What an administrator changes on a runner; <see langword="null"/> leaves a field as it is.</summary>
/// <param name="Name">A new display name.</param>
/// <param name="IsPaused">Pause (no new jobs) or resume the runner.</param>
public sealed record RunnerChanges(string? Name = null, bool? IsPaused = null);
