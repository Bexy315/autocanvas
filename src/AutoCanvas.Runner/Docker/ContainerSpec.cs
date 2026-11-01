namespace AutoCanvas.Runner.Docker;

/// <summary>A container to start.</summary>
/// <param name="Image">The image, pulled before.</param>
/// <param name="Command">What the container runs.</param>
/// <param name="Binds">Bind mounts as <c>host-path:container-path</c>.</param>
/// <param name="Env">Its environment.</param>
/// <param name="Labels">Labels, by which the runner finds its containers again.</param>
internal sealed record ContainerSpec(
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyList<string> Binds,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyDictionary<string, string> Labels);
