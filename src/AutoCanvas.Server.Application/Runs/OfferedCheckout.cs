namespace AutoCanvas.Server.Application.Runs;

/// <summary>A repository to clone into the workspace.</summary>
/// <param name="Url">Clone URL (https).</param>
/// <param name="Ref">Branch, tag or commit; <see langword="null"/> for the default branch.</param>
/// <param name="Path">Directory relative to the workspace; <see langword="null"/> for its root.</param>
/// <param name="Depth">History depth; 0 for the full history.</param>
public sealed record OfferedCheckout(Uri Url, string? Ref, string? Path, int Depth);
