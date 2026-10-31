namespace AutoCanvas.Server.Application.Runs;

/// <summary>The image, default environment and readiness command of a preset.</summary>
/// <param name="Image">Image name without tag.</param>
/// <param name="DefaultVersion">Tag used when the service names no <c>version</c>.</param>
/// <param name="Env">Environment the container needs to start.</param>
/// <param name="Ready">Command that succeeds once the service accepts connections.</param>
public sealed record ServicePresetDefinition(string Image, string DefaultVersion, IReadOnlyDictionary<string, string> Env, string Ready);
