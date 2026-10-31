using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Common;

namespace AutoCanvas.Server.Application.Automations;

/// <summary>An automation with the content of its newest version.</summary>
/// <param name="Id">The automation's id.</param>
/// <param name="FolderId">The folder it lives in.</param>
/// <param name="Name">The automation's key in its folder.</param>
/// <param name="Title"><c>name:</c> from the file, or the key.</param>
/// <param name="Description"><c>description:</c> from the file.</param>
/// <param name="CurrentVersion">Number of the newest version; the ETag of the API.</param>
/// <param name="Yaml">The automation file of the newest version.</param>
/// <param name="Warnings">Things in the file that work but deserve a look.</param>
/// <param name="MyRole">The caller's role on the automation's folder.</param>
/// <param name="CreatedAt">When the automation was created.</param>
/// <param name="UpdatedAt">When anything about it last changed.</param>
public sealed record AutomationView(
    Guid Id,
    Guid FolderId,
    string Name,
    string Title,
    string? Description,
    int CurrentVersion,
    string Yaml,
    IReadOnlyList<DiagnosticView> Warnings,
    FolderRole MyRole,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
