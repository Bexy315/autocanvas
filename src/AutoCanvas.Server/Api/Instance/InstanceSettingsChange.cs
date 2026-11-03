namespace AutoCanvas.Server.Api.Instance;

/// <summary>Both addresses of the installation; empty clears one.</summary>
/// <param name="PublicAddress">The address people and other systems reach the server at.</param>
/// <param name="RunnerAddress">The address runners reach the runner endpoint at.</param>
internal sealed record InstanceSettingsChange(string? PublicAddress, string? RunnerAddress);
