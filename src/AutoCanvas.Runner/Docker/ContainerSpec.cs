namespace AutoCanvas.Runner.Docker;

/// <summary>A container to start.</summary>
/// <param name="Image">The image, pulled before.</param>
/// <param name="Command">What the container runs; the image's default command when empty.</param>
/// <param name="Binds">Bind mounts as <c>host-path:container-path</c>, and named volumes as <c>volume:container-path</c>.</param>
/// <param name="Env">Its environment.</param>
/// <param name="Labels">Labels, by which the runner finds its containers again.</param>
internal sealed record ContainerSpec(
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyList<string> Binds,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyDictionary<string, string> Labels)
{
    /// <summary>The program <see cref="Command"/> is given to; the image's own when <see langword="null"/>.</summary>
    public IReadOnlyList<string>? Entrypoint { get; init; }

    /// <summary>The network the container joins; Docker's default bridge when <see langword="null"/>.</summary>
    public string? Network { get; init; }

    /// <summary>The name other containers on <see cref="Network"/> reach it by.</summary>
    public string? NetworkAlias { get; init; }

    /// <summary>
    /// Docker security options. Every container gets <c>no-new-privileges</c>; only BuildKit, which
    /// makes namespaces of its own, is given other ones.
    /// </summary>
    public IReadOnlyList<string> SecurityOptions { get; init; } = ["no-new-privileges"];
}
