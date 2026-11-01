namespace AutoCanvas.Runner;

/// <summary>Entry point of the runner. A named class rather than top-level statements, so that tests can host the server's <c>Program</c> next to it.</summary>
internal static class RunnerProgram
{
    public static Task<int> Main(string[] args) => RunnerCommandLine.RunAsync(args);
}
