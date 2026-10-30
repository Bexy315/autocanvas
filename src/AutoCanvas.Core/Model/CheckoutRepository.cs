namespace AutoCanvas.Core.Model;

/// <summary>One repository to clone into a job's workspace.</summary>
/// <param name="Repository"><c>self</c>, the name of a connected repository, or an https address.</param>
/// <param name="Ref">Branch, tag or commit, or <see langword="null"/> for the default.</param>
/// <param name="Path">Directory relative to the workspace, or <see langword="null"/> for its root.</param>
/// <param name="Depth">History depth (0 for full), or <see langword="null"/> for the default of 1.</param>
public sealed record CheckoutRepository(string Repository, string? Ref, string? Path, int? Depth);
