namespace AutoCanvas.Server.Application.Common;

/// <summary>Which page of a list to return (API conventions: cursor paging).</summary>
/// <param name="Limit">Items per page; default 50, at most 200.</param>
/// <param name="Cursor">The <see cref="Page{T}.Next"/> value of the previous page.</param>
public sealed record PageRequest(int? Limit = null, string? Cursor = null)
{
    /// <summary>Items per page when the caller does not say.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The largest page a caller can ask for.</summary>
    public const int MaxLimit = 200;

    /// <summary>The effective page size, or an error if the caller asked for an impossible one.</summary>
    internal UseCaseResult<int> ResolveLimit() => Limit switch
    {
        null => UseCase.Success<int>(DefaultLimit),
        >= 1 and <= MaxLimit => UseCase.Success<int>(Limit.Value),
        _ => UseCase.Failure<int>(UseCaseError.Invalid("limit", $"limit must be between 1 and {MaxLimit}.")),
    };
}
