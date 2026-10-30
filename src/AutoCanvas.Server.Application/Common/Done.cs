namespace AutoCanvas.Server.Application.Common;

/// <summary>The value of a use case that succeeds without returning anything.</summary>
public readonly record struct Done
{
    /// <summary>A successful outcome with no value.</summary>
    public static UseCaseResult<Done> Result { get; } = UseCase.Success<Done>(default);
}
