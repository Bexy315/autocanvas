namespace AutoCanvas.Core.Expressions;

/// <summary>Supplies the values that expressions read, such as variables and job outputs.</summary>
public interface IValueResolver
{
    /// <summary>Looks up the value at <paramref name="path"/>, for example <c>["vars", "REGION"]</c>.</summary>
    /// <param name="path">The path segments.</param>
    /// <param name="value">The value, or <see langword="null"/> if the path is known but has no value.</param>
    /// <returns><see langword="false"/> if nothing is known at this path; the expression then reads <c>null</c>.</returns>
    bool TryResolve(IReadOnlyList<string> path, out string? value);
}
