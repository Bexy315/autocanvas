namespace AutoCanvas.Server.Application.Runners;

/// <summary>A runner as its connection needs to know it.</summary>
/// <param name="Id">The runner's id.</param>
/// <param name="Name">Its display name.</param>
/// <param name="IsPaused">Whether it is kept from taking new jobs.</param>
public sealed record RunnerState(Guid Id, string Name, bool IsPaused);
