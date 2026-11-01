namespace AutoCanvas.Runner.Docker;

/// <summary>An image reference split for the pull API: repository and tag or digest.</summary>
/// <param name="Repository">Registry, path and name, such as <c>ghcr.io/org/app</c>.</param>
/// <param name="Tag">The tag, or the digest (<c>sha256:…</c>); <c>latest</c> when the reference has neither.</param>
internal sealed record ImageReference(string Repository, string Tag)
{
    public static ImageReference Parse(string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        var at = image.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
        {
            return new ImageReference(image[..at], image[(at + 1)..]);
        }

        // A colon after the last slash starts the tag; one before it belongs to a registry's port.
        var colon = image.LastIndexOf(':');
        return colon > image.LastIndexOf('/') ? new ImageReference(image[..colon], image[(colon + 1)..]) : new ImageReference(image, "latest");
    }
}
