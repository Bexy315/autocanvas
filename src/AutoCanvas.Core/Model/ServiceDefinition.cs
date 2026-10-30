namespace AutoCanvas.Core.Model;

/// <summary>A container started next to a job. Exactly one of <see cref="Preset"/> and <see cref="Image"/> is set.</summary>
/// <param name="Name">Host name under which the steps reach the service.</param>
/// <param name="Preset">Known service with image, environment and readiness check.</param>
/// <param name="Version">Version of the preset, as written.</param>
/// <param name="Image">Container image, instead of a preset.</param>
/// <param name="Env">Environment of the service container, as written.</param>
/// <param name="Ready">Command that must succeed before the steps start.</param>
public sealed record ServiceDefinition(
    string Name,
    ServicePreset? Preset,
    string? Version,
    string? Image,
    IReadOnlyDictionary<string, string> Env,
    string? Ready);
