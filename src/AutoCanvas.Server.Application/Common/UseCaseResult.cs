namespace AutoCanvas.Server.Application.Common;

/// <summary>The outcome of a use case that returns a value: the value, or why there is none.</summary>
/// <typeparam name="T">The value on success.</typeparam>
public sealed class UseCaseResult<T>
{
    private UseCaseResult(T? value, UseCaseError? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>The value; set when <see cref="Error"/> is not.</summary>
    public T? Value { get; }

    /// <summary>Why the request was refused; <see langword="null"/> on success.</summary>
    public UseCaseError? Error { get; }

    internal static UseCaseResult<T> Create(T? value, UseCaseError? error) => new(value, error);
}
