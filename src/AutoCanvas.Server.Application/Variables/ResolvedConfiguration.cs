namespace AutoCanvas.Server.Application.Variables;

/// <summary>The values a run of an automation reads, secrets decrypted. Never leaves the server except to the runner.</summary>
/// <param name="Variables">Variable values by name, regardless of case.</param>
/// <param name="Secrets">Secret values by name, regardless of case.</param>
public sealed record ResolvedConfiguration(IReadOnlyDictionary<string, string> Variables, IReadOnlyDictionary<string, string> Secrets);
