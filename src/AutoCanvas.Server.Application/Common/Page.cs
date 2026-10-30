namespace AutoCanvas.Server.Application.Common;

/// <summary>One page of a list.</summary>
/// <typeparam name="T">The items.</typeparam>
/// <param name="Items">The items of this page, in list order.</param>
/// <param name="Next">Cursor of the next page; <see langword="null"/> on the last page.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, string? Next);
