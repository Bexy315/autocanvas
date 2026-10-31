namespace AutoCanvas.Server.Application.Runs;

/// <summary>A container started next to the job.</summary>
/// <param name="Name">Host name the steps reach it under.</param>
/// <param name="Image">Container image.</param>
/// <param name="Env">Environment of the container.</param>
/// <param name="ReadyCommand">Command that must succeed before the first step; <see langword="null"/> for none.</param>
/// <param name="ReadyTimeout">How long to wait for <paramref name="ReadyCommand"/>.</param>
public sealed record OfferedService(string Name, string Image, IReadOnlyDictionary<string, string> Env, string? ReadyCommand, TimeSpan ReadyTimeout);
