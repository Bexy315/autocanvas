namespace AutoCanvas.Server.Application.Runs;

/// <summary>A resolved script.</summary>
/// <param name="Shell">One of <c>bash</c>, <c>sh</c>, <c>pwsh</c>, <c>cmd</c>.</param>
/// <param name="Text">The script text.</param>
/// <param name="WorkingDirectory">Directory relative to the workspace; <see langword="null"/> for its root.</param>
/// <param name="DefaultShell">
/// The step named no shell, so <paramref name="Shell"/> is the server's choice; the runner falls back
/// to <c>sh</c> when the job's image does not have it.
/// </param>
public sealed record ResolvedScript(string Shell, string Text, string? WorkingDirectory, bool DefaultShell = false);
