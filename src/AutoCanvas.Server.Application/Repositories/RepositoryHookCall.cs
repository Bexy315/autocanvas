namespace AutoCanvas.Server.Application.Repositories;

/// <summary>A call of a repository's webhook address by its forge, as it arrived.</summary>
/// <param name="Body">The body's bytes, at most 1 MiB.</param>
/// <param name="Headers">The headers, by name regardless of case.</param>
public sealed record RepositoryHookCall(ReadOnlyMemory<byte> Body, IReadOnlyDictionary<string, string> Headers);
