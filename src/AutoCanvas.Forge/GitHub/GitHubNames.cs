namespace AutoCanvas.Forge.GitHub;

/// <summary>Names of GitHub repositories.</summary>
public static class GitHubNames
{
    /// <summary>
    /// The <c>owner/name</c> of a repository from its clone address, such as
    /// <c>https://github.com/acme/shop.git</c>; <see langword="null"/> when the address has another shape.
    /// </summary>
    public static string? FullNameOf(Uri cloneUrl)
    {
        ArgumentNullException.ThrowIfNull(cloneUrl);
        var parts = cloneUrl.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2)
        {
            return null;
        }

        var name = parts[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[^1][..^4] : parts[^1];
        return parts[^2].Length > 0 && name.Length > 0 ? $"{parts[^2]}/{name}" : null;
    }
}
