namespace AutoCanvas.Core.Model;

/// <summary>A case of a <see cref="SwitchJob"/>.</summary>
/// <param name="Name">Case name, referred to by <see cref="JobNeed.Case"/>.</param>
/// <param name="When">Value the expression must equal, as text; <see langword="null"/> for the default case.</param>
/// <param name="IsDefault">Taken when no other case matches.</param>
public sealed record SwitchCase(string Name, string? When, bool IsDefault);
