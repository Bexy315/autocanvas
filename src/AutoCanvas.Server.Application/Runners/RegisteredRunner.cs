namespace AutoCanvas.Server.Application.Runners;

/// <summary>A runner that has just registered.</summary>
/// <param name="RunnerId">Its id on this server.</param>
/// <param name="Credential">The secret it authenticates with from now on; returned only this once.</param>
public sealed record RegisteredRunner(Guid RunnerId, string Credential);
