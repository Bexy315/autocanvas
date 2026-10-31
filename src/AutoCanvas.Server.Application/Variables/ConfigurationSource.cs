namespace AutoCanvas.Server.Application.Variables;

/// <summary>One place that defines a name.</summary>
/// <param name="Kind">Instance, a folder, or the automation (its secrets, or <c>variables:</c> in its file).</param>
/// <param name="FolderId">The folder, for folder definitions.</param>
/// <param name="FolderName">The folder's name, for display.</param>
/// <param name="Value">The variable's value, for callers with the Operator role; never set for secrets.</param>
public sealed record ConfigurationSource(ConfigurationScopeKind Kind, Guid? FolderId, string? FolderName, string? Value);
