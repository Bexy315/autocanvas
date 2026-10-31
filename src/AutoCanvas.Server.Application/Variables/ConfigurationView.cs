namespace AutoCanvas.Server.Application.Variables;

/// <summary>What a folder or an automation can read, and from where; never a secret value.</summary>
/// <param name="Variables">Every variable name that reaches it.</param>
/// <param name="Secrets">Every secret name that reaches it, without values.</param>
public sealed record ConfigurationView(IReadOnlyList<ConfigurationEntry> Variables, IReadOnlyList<ConfigurationEntry> Secrets);
