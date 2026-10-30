namespace AutoCanvas.Core.Validation;

/// <summary>What an expression may read at one place in an automation.</summary>
/// <param name="Place">Describes the place for messages, such as "a variable" or "job 'build'".</param>
/// <param name="Variables">Whether <c>vars</c> and <c>secrets</c> may be read.</param>
/// <param name="Jobs">Jobs whose results and outputs may be read; <see langword="null"/> when <c>jobs</c> may not be read at all.</param>
/// <param name="Steps">Step ids whose outputs may be read; <see langword="null"/> when <c>steps</c> may not be read at all.</param>
internal sealed record ExpressionScope(
    string Place,
    bool Variables,
    IReadOnlySet<string>? Jobs,
    IReadOnlySet<string>? Steps);
