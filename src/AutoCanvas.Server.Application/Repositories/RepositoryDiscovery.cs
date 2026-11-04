using System.Text.Json;
using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Forge;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Automations;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Triggers;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// Reads a repository's automation files at its default branch and makes the folder's repository
/// automations follow them (ADR-0021): a new valid file becomes an automation, a changed one a new
/// version at its commit, a removed one is deleted. A file that is not valid stays a problem, and
/// the automation keeps its last good version. Nothing changes when the repository cannot be read.
/// </summary>
public sealed class RepositoryDiscovery(
    AutoCanvasDbContext database,
    IEnumerable<IForge> forges,
    RepositoryCredentials credentials,
    IOptions<RepositoryOptions> options,
    IAuditLog audit,
    TimeProvider time)
{
    /// <summary>The scope of repository credentials in the cipher's associated data.</summary>
    public const string CredentialScope = "repository";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Where a repository's copy is kept.</summary>
    internal string CacheOf(Guid repositoryId) => Path.Combine(options.Value.CacheDirectory, repositoryId.ToString("N"));

    /// <summary>
    /// How to reach the repository, with its credential; throws <see cref="ForgeException"/> when a
    /// GitHub repository's token cannot be had.
    /// </summary>
    internal async Task<RepositoryAccess> AccessAsync(Repository repository, CancellationToken cancellationToken) => new(
        repository.Url,
        RepositoryCredentials.UserNameOf(repository),
        await credentials.SecretAsync(repository, cancellationToken).ConfigureAwait(false),
        CacheOf(repository.Id));

    internal IForge ForgeOf(Repository repository)
    {
        var kind = repository.Kind == RepositoryKind.GitHub ? ForgeKind.GitHub : ForgeKind.GenericGit;
        return forges.FirstOrDefault(f => f.Kind == kind) ?? throw new InvalidOperationException($"No forge for {kind} is registered.");
    }

    /// <summary>
    /// Reads the repository and applies what changed, in the caller's unit of work (saved by the
    /// caller). <paramref name="actor"/> is who asked, for the visibility checks of
    /// <c>automation_completed</c>.
    /// </summary>
    internal async Task SyncAsync(Actor actor, Repository repository, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        repository.LastSyncedAt = now;
        RepositorySnapshot snapshot;
        ResolvedBranch head;
        try
        {
            var forge = ForgeOf(repository);
            var access = await AccessAsync(repository, cancellationToken).ConfigureAwait(false);
            head = await forge.ResolveAsync(access, repository.DefaultBranch, cancellationToken).ConfigureAwait(false);
            snapshot = await forge.ReadAutomationsAsync(access, head.Branch, cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException exception)
        {
            repository.LastError = Cut(exception.Message, Repository.MaxErrorLength);
            audit.Record(Entry(repository, ("commit", null), ("error", repository.LastError)));
            return;
        }

        var problems = new List<RepositoryProblemView>();
        var triggers = new TriggerIndex(database, new FolderAccess(database), time);
        var existing = await database.Automations.Where(a => a.RepositoryId == repository.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var seen = new HashSet<Guid>();
        foreach (var file in snapshot.Files)
        {
            var applied = await ApplyAsync(actor, repository, snapshot, file, existing, triggers, problems, now, cancellationToken).ConfigureAwait(false);
            if (applied is { } id)
            {
                seen.Add(id);
            }
        }

        foreach (var gone in existing.Where(a => !seen.Contains(a.Id) && !problems.Any(p => string.Equals(p.Path, a.SourcePath, StringComparison.Ordinal))))
        {
            database.Automations.Remove(gone);
            audit.Record(AutomationEntry(AuditActions.AutomationDeleted, gone, ("name", gone.Name), ("commit", snapshot.Commit)));
        }

        repository.ResolvedBranch = head.Branch;
        repository.LastCommit = snapshot.Commit;
        repository.LastError = null;
        repository.Problems = problems.Count == 0 ? null : JsonSerializer.Serialize(problems, Json);
        audit.Record(Entry(repository, ("commit", snapshot.Commit), ("error", null)));
    }

    /// <summary>The problems stored with a repository.</summary>
    internal static IReadOnlyList<RepositoryProblemView> ProblemsOf(Repository repository) =>
        repository.Problems is { } json ? JsonSerializer.Deserialize<List<RepositoryProblemView>>(json, Json) ?? [] : [];

    /// <summary>Applies one file; the automation it is, or <see langword="null"/> when it is a problem.</summary>
    private async Task<Guid?> ApplyAsync(
        Actor actor,
        Repository repository,
        RepositorySnapshot snapshot,
        RepositoryFile file,
        List<Automation> existing,
        TriggerIndex triggers,
        List<RepositoryProblemView> problems,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var automation = existing.FirstOrDefault(a => string.Equals(a.SourcePath, file.Path, StringComparison.Ordinal));
        if (automation is null && Names.Check("name", file.Name, Automation.MaxNameLength, "automation") is { } badName)
        {
            problems.Add(new RepositoryProblemView(file.Path, badName.Message, null));
            return null;
        }

        var loaded = AutomationService.Load(file.Content);
        if (loaded.Error is { } broken)
        {
            problems.AddRange(Errors(file.Path, broken.Diagnostics ?? []));
            return automation?.Id;
        }

        var (plan, triggerProblems) = await triggers.PrepareAsync(actor, repository.FolderId, automation?.Id, loaded.Value!.Triggers, cancellationToken).ConfigureAwait(false);
        if (triggerProblems.Count > 0)
        {
            problems.AddRange(Errors(file.Path, triggerProblems));
            return automation?.Id;
        }

        if (automation is null)
        {
            automation = await NewAutomationAsync(repository, file, snapshot, problems, now, cancellationToken).ConfigureAwait(false);
            if (automation is null)
            {
                return null;
            }

            existing.Add(automation);
        }
        else if (await UnchangedAsync(automation, file, cancellationToken).ConfigureAwait(false))
        {
            return automation.Id;
        }
        else
        {
            audit.Record(AutomationEntry(AuditActions.AutomationSaved, automation, ("version", automation.CurrentVersion + 1), ("commit", snapshot.Commit)));
        }

        AutomationService.Describe(automation, loaded.Value!);
        AddVersion(automation, file, snapshot, now);
        await triggers.ApplyAsync(automation.Id, plan, cancellationToken).ConfigureAwait(false);
        return automation.Id;
    }

    /// <summary>A new repository automation for the file, or <see langword="null"/> when the folder has one of that name.</summary>
    private async Task<Automation?> NewAutomationAsync(Repository repository, RepositoryFile file, RepositorySnapshot snapshot, List<RepositoryProblemView> problems, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var normalized = IdentityNames.Normalize(file.Name);
        if (await database.Automations.AnyAsync(a => a.FolderId == repository.FolderId && a.NormalizedName == normalized, cancellationToken).ConfigureAwait(false))
        {
            problems.Add(new RepositoryProblemView(file.Path, $"The folder has another automation named '{file.Name}'; rename one of them.", null));
            return null;
        }

        var automation = new Automation
        {
            Id = Guid.CreateVersion7(now),
            FolderId = repository.FolderId,
            Name = file.Name,
            NormalizedName = normalized,
            Title = file.Name,
            RepositoryId = repository.Id,
            SourcePath = file.Path,
            CreatedAt = now,
            UpdatedAt = now,
        };
        database.Automations.Add(automation);
        audit.Record(AutomationEntry(AuditActions.AutomationCreated, automation, ("name", automation.Name), ("commit", snapshot.Commit)));
        return automation;
    }

    private async Task<bool> UnchangedAsync(Automation automation, RepositoryFile file, CancellationToken cancellationToken)
    {
        var current = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == automation.Id && v.Number == automation.CurrentVersion)
            .Select(v => v.Yaml)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        return string.Equals(current, file.Content, StringComparison.Ordinal);
    }

    /// <summary>The file as the automation's next version, at the snapshot's commit.</summary>
    private void AddVersion(Automation automation, RepositoryFile file, RepositorySnapshot snapshot, DateTimeOffset now)
    {
        automation.UpdatedAt = now;
        automation.CurrentVersion++;
        database.AutomationVersions.Add(new AutomationVersion
        {
            Id = Guid.CreateVersion7(now),
            AutomationId = automation.Id,
            Number = automation.CurrentVersion,
            Yaml = file.Content,
            Message = Cut(snapshot.Subject, AutomationVersion.MaxMessageLength),
            CommitSha = snapshot.Commit,
            CreatedAt = now,
            CreatedByName = Cut(snapshot.Author.Length > 0 ? snapshot.Author : "git", Persistence.Identity.User.MaxUserNameLength)!,
        });
    }

    private static IEnumerable<RepositoryProblemView> Errors(string path, IReadOnlyList<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => new RepositoryProblemView(path, d.Message, d.Location?.Line));

    private static string? Cut(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;

    private static AuditEntry Entry(Repository repository, params (string Key, object? Value)[] details) => new(AuditActions.RepositorySynced)
    {
        TargetType = "repository",
        TargetId = repository.Id.ToString(),
        FolderId = repository.FolderId,
        Details = AuditEntry.DetailsOf([("name", repository.Name), .. details]),
    };

    private static AuditEntry AutomationEntry(string action, Automation automation, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "automation",
        TargetId = automation.Id.ToString(),
        FolderId = automation.FolderId,
        Details = AuditEntry.DetailsOf(details),
    };
}
