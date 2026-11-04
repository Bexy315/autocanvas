namespace AutoCanvas.Forge;

/// <summary>How to reach one repository.</summary>
/// <param name="Url">Its clone address, <c>http</c> or <c>https</c>.</param>
/// <param name="UserName">The user name for the credential; empty for a token alone.</param>
/// <param name="Secret">A token or password; <see langword="null"/> for a public repository.</param>
/// <param name="CacheDirectory">A directory of its own where the forge may keep a copy of the repository.</param>
public sealed record RepositoryAccess(Uri Url, string? UserName, string? Secret, string CacheDirectory)
{
    /// <summary>The user name sent with a token that comes without one, as the forges expect it.</summary>
    public const string TokenUserName = "x-access-token";

    /// <inheritdoc />
    public override string ToString() => $"RepositoryAccess {{ Url = {Url}, UserName = {UserName}, Secret = {(Secret is null ? "none" : "***")} }}";
}
