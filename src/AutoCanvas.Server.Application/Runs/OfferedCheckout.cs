namespace AutoCanvas.Server.Application.Runs;

/// <summary>A repository to clone into the workspace.</summary>
/// <param name="Url">Clone URL (https).</param>
/// <param name="Ref">Branch, tag or commit; <see langword="null"/> for the default branch.</param>
/// <param name="Path">Directory relative to the workspace; <see langword="null"/> for its root.</param>
/// <param name="Depth">History depth; 0 for the full history.</param>
/// <param name="UserName">The user name sent with <paramref name="Password"/>; <see langword="null"/> for a token alone.</param>
/// <param name="Password">The repository's token or password, for a connected repository; <see langword="null"/> otherwise.</param>
public sealed record OfferedCheckout(Uri Url, string? Ref, string? Path, int Depth, string? UserName = null, string? Password = null)
{
    /// <inheritdoc />
    public override string ToString() => $"OfferedCheckout {{ Url = {Url}, Ref = {Ref}, Path = {Path}, Depth = {Depth}, Password = {(Password is null ? "none" : "***")} }}";
}
