namespace AutoCanvas.Runner.Docker;

/// <summary>A command to run in a container.</summary>
/// <param name="Command">The program and its arguments.</param>
/// <param name="WorkingDirectory">Where it runs, inside the container.</param>
/// <param name="Env">Environment on top of the container's.</param>
internal sealed record ExecSpec(IReadOnlyList<string> Command, string WorkingDirectory, IReadOnlyDictionary<string, string> Env);
