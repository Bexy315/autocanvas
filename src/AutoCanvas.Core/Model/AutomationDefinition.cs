using System.Text.Json;

namespace AutoCanvas.Core.Model;

/// <summary>
/// An automation as written in an automation file (format version 1), after parsing. It is
/// structurally valid but not yet checked for meaning, such as whether every needed job exists.
/// </summary>
/// <param name="Version">Format version of the file.</param>
/// <param name="Name">Display name, or <see langword="null"/> to use the file name.</param>
/// <param name="Description">What the automation does.</param>
/// <param name="Triggers">What starts a run; <see cref="Model.Triggers.None"/> when the file has no triggers.</param>
/// <param name="Variables">Variables readable as <c>${{ vars.NAME }}</c>, as written.</param>
/// <param name="TimeoutMinutes">Default time limit per job.</param>
/// <param name="Concurrency">Whether runs may overlap.</param>
/// <param name="ChangedWhen">Expression telling whether a successful run changed anything.</param>
/// <param name="Jobs">The jobs, in file order.</param>
/// <param name="Extensions">Top-level <c>x-…</c> keys, kept as data.</param>
public sealed record AutomationDefinition(
    int Version,
    string? Name,
    string? Description,
    Triggers Triggers,
    IReadOnlyDictionary<string, string> Variables,
    int? TimeoutMinutes,
    Concurrency Concurrency,
    string? ChangedWhen,
    IReadOnlyList<JobDefinition> Jobs,
    IReadOnlyDictionary<string, JsonElement> Extensions);
