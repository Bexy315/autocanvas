namespace AutoCanvas.Server.Application.Common;

/// <summary>Creates <see cref="UseCaseResult{T}"/> values.</summary>
public static class UseCase
{
    /// <summary>A successful outcome.</summary>
    public static UseCaseResult<T> Success<T>(T value) => UseCaseResult<T>.Create(value, null);

    /// <summary>A refused request.</summary>
    public static UseCaseResult<T> Failure<T>(UseCaseError error) => UseCaseResult<T>.Create(default, error);
}
