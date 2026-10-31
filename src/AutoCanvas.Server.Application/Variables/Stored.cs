namespace AutoCanvas.Server.Application.Variables;

/// <summary>The outcome of setting a value by name.</summary>
/// <param name="Item">What is stored now.</param>
/// <param name="Created">Whether the name was new in its scope, rather than replaced.</param>
public sealed record Stored<T>(T Item, bool Created);
