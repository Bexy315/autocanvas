using AutoCanvas.Core.Expressions;

namespace AutoCanvas.Core.Tests.Expressions;

/// <summary>Resolves paths from a dictionary keyed by the dotted path.</summary>
internal sealed class DictionaryResolver(Dictionary<string, string?> values) : IValueResolver
{
    public bool TryResolve(IReadOnlyList<string> path, out string? value) =>
        values.TryGetValue(string.Join('.', path), out value);
}
