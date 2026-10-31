using AutoCanvas.Core.Expressions;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Passes reads on and notes which secret values they returned: what a job's environment must not
/// carry, and what a step's runner must mask (<c>mask_values</c>).
/// </summary>
internal sealed class SecretReads(IValueResolver inner) : IValueResolver
{
    private readonly HashSet<string> values = new(StringComparer.Ordinal);

    /// <summary>The secret values read so far.</summary>
    public IReadOnlyCollection<string> Values => values;

    public bool TryResolve(IReadOnlyList<string> path, out string? value)
    {
        ArgumentNullException.ThrowIfNull(path);
        var found = inner.TryResolve(path, out value);
        if (found && path is ["secrets", ..] && !string.IsNullOrEmpty(value))
        {
            values.Add(value);
        }

        return found;
    }

    /// <summary>Renders a template.</summary>
    public string Render(string text) => Template.Parse(text).Template.Render(this);
}
