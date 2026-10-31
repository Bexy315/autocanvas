namespace AutoCanvas.Server.Application.Variables;

/// <summary>A variable as listed in its scope.</summary>
/// <param name="Name">The name as written.</param>
/// <param name="Value">The value; empty for callers without the Operator role, who see only names.</param>
/// <param name="UpdatedAt">When it was last set.</param>
/// <param name="UpdatedByName">Who set it last.</param>
public sealed record VariableView(string Name, string? Value, DateTimeOffset UpdatedAt, string UpdatedByName);
