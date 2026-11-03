namespace AutoCanvas.Server.Application.Instance;

/// <summary>The installation's addresses (ADR-0020).</summary>
/// <param name="PublicAddress">The address people and other systems reach the server at.</param>
/// <param name="RunnerAddress">The address runners reach the runner endpoint at.</param>
public sealed record InstanceSettingsView(SettingValue PublicAddress, SettingValue RunnerAddress);
