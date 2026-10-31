namespace AutoCanvas.Server.Application.Variables;

/// <summary>One name, everywhere it is defined, and which definition a run reads.</summary>
/// <param name="Name">The name as written where it wins.</param>
/// <param name="Definitions">Every definition in resolution order: instance, folders from the top, automation.</param>
/// <param name="Winner">The nearest definition, the last of <paramref name="Definitions"/>.</param>
public sealed record ConfigurationEntry(string Name, IReadOnlyList<ConfigurationSource> Definitions, ConfigurationSource Winner);
