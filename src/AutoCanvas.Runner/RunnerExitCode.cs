namespace AutoCanvas.Runner;

/// <summary>The exit code the runner ends with; the connection sets it when it stops the runner.</summary>
internal sealed class RunnerExitCode
{
    /// <summary>The runner cannot run at all: not registered, or its credential was revoked.</summary>
    public const int NotAuthorized = 2;

    public int Value { get; set; }
}
