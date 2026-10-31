namespace AutoCanvas.Server.Application.Automations;

/// <summary>A saved version with its content.</summary>
/// <param name="Number">1 for the first version, counting up.</param>
/// <param name="Message">What changed, in the author's words.</param>
/// <param name="CreatedAt">When it was saved.</param>
/// <param name="CreatedById">The author, unless the user has been deleted since.</param>
/// <param name="CreatedByName">The author's user name at the time.</param>
/// <param name="Yaml">The automation file as saved; save it again to restore this version.</param>
public sealed record AutomationVersionView(int Number, string? Message, DateTimeOffset CreatedAt, Guid? CreatedById, string CreatedByName, string Yaml);
