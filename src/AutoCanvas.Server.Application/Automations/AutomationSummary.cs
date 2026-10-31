namespace AutoCanvas.Server.Application.Automations;

/// <summary>An automation as listed in its folder.</summary>
/// <param name="Id">The automation's id.</param>
/// <param name="Name">The automation's key in its folder.</param>
/// <param name="Title"><c>name:</c> from the file, or the key.</param>
/// <param name="Description"><c>description:</c> from the file.</param>
/// <param name="CurrentVersion">Number of the newest version.</param>
/// <param name="UpdatedAt">When anything about it last changed.</param>
public sealed record AutomationSummary(Guid Id, string Name, string Title, string? Description, int CurrentVersion, DateTimeOffset UpdatedAt);
