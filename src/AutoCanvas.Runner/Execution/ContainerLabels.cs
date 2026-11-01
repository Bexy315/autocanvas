namespace AutoCanvas.Runner.Execution;

/// <summary>
/// Labels on everything the runner starts, so that it finds what a crash left behind — and only its
/// own, when several runners share one Docker host.
/// </summary>
internal static class ContainerLabels
{
    public const string Managed = "autocanvas.managed";

    public const string Runner = "autocanvas.runner";

    public const string Job = "autocanvas.job";

    public static Dictionary<string, string> For(Guid runnerId, string jobId) => new(StringComparer.Ordinal)
    {
        [Managed] = "true",
        [Runner] = runnerId.ToString(),
        [Job] = jobId,
    };
}
