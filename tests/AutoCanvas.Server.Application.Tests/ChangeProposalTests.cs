using AutoCanvas.Forge.GitHub;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Forges;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Instance;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

/// <summary>What Edit → pull request refuses before it asks GitHub anything, and the links to GitHub.</summary>
public abstract class ChangeProposalTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private const string Build = "version: 1\ntriggers:\n  manual: true\njobs:\n  build:\n    steps:\n      - run: make\n";

    [Fact]
    public async Task Proposals_are_refused_for_unknown_managed_unchanged_invalid_and_Generic_Git_automations()
    {
        var editor = await ActorAsync("editor", isAdmin: true);
        var folder = (await FoldersAsync(f => f.CreateAsync(editor, new NewFolder("Shop"), Cancel))).ShouldSucceed().Id;
        var managed = (await AutomationsAsync(a => a.CreateAsync(editor, folder, "managed", Build, null, Cancel))).ShouldSucceed().Id;
        var fromGitHub = await RepositoryAutomationAsync(folder, RepositoryKind.GitHub, "build");
        var fromGit = await RepositoryAutomationAsync(folder, RepositoryKind.GenericGit, "deploy");

        (await ProposeAsync(editor, Guid.CreateVersion7(), Build)).ShouldFail(UseCaseErrorKind.NotFound);
        (await ProposeAsync(editor, managed, Build + "# more\n")).ShouldFail(UseCaseErrorKind.Conflict).Message.ShouldContain("managed in AutoCanvas");
        (await ProposeAsync(editor, fromGit, Build + "# more\n")).ShouldFail(UseCaseErrorKind.Conflict).Message.ShouldContain("in the repository");
        (await ProposeAsync(editor, fromGitHub, Build)).ShouldFail(UseCaseErrorKind.Validation).Message.ShouldContain("Nothing changed");
        (await ProposeAsync(editor, fromGitHub, "version: 1\njobs: {}\n")).ShouldFail(UseCaseErrorKind.Validation).Diagnostics.ShouldNotBeEmpty();
        (await ProposeAsync(editor, fromGitHub, Build + "# more\n")).ShouldFail(UseCaseErrorKind.Conflict).Message.ShouldContain("GitHub App is not set up");
        var stranger = await ActorAsync("stranger");
        (await ProposeAsync(stranger, fromGitHub, Build + "# more\n")).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public void GitHub_repositories_link_to_their_pages_and_Generic_Git_ones_to_nothing()
    {
        var github = Repository(RepositoryKind.GitHub, new Uri("https://github.com/acme/shop.git"));
        var git = Repository(RepositoryKind.GenericGit, new Uri("https://git.example.org/acme/shop.git"));

        RepositoryLinks.Web(github).ShouldBe(new Uri("https://github.com/acme/shop"));
        RepositoryLinks.File(github, "abc", ".autocanvas/automations/build.yml").ShouldBe(new Uri("https://github.com/acme/shop/blob/abc/.autocanvas/automations/build.yml"));
        RepositoryLinks.Commit(github, "abc").ShouldBe(new Uri("https://github.com/acme/shop/commit/abc"));
        RepositoryLinks.PullRequest(github, 12).ShouldBe(new Uri("https://github.com/acme/shop/pull/12"));
        RepositoryLinks.File(github, null, "x").ShouldBeNull();
        RepositoryLinks.PullRequest(github, null).ShouldBeNull();
        RepositoryLinks.Web(git).ShouldBeNull();
        RepositoryLinks.Commit(git, "abc").ShouldBeNull();
        GitHubNames.WebOf(new Uri("https://github.com/shop.git")).ShouldBeNull();
    }

    private static Repository Repository(RepositoryKind kind, Uri url) => new()
    {
        Id = Guid.CreateVersion7(),
        FolderId = Guid.CreateVersion7(),
        Name = "shop",
        NormalizedName = "SHOP",
        Kind = kind,
        Url = url,
        CreatedAt = DateTimeOffset.UnixEpoch,
        CreatedByName = "owner",
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>A repository automation with one version at a commit, in a repository of its own.</summary>
    private async Task<Guid> RepositoryAutomationAsync(Guid folder, RepositoryKind kind, string name)
    {
        var repository = Repository(kind, new Uri($"https://github.com/acme/{name}.git"));
        var automation = Guid.CreateVersion7();
        await ReadAsync(async db =>
        {
            db.Repositories.Add(new Repository
            {
                Id = repository.Id,
                FolderId = folder,
                Name = name,
                NormalizedName = IdentityNames.Normalize(name),
                Kind = kind,
                Url = repository.Url,
                ResolvedBranch = "main",
                InstallationId = kind == RepositoryKind.GitHub ? 7 : null,
                CreatedAt = Clock.GetUtcNow(),
                CreatedByName = "owner",
                UpdatedAt = Clock.GetUtcNow(),
            });
            db.Automations.Add(new Automation
            {
                Id = automation,
                FolderId = folder,
                Name = name,
                NormalizedName = IdentityNames.Normalize(name),
                Title = name,
                RepositoryId = repository.Id,
                SourcePath = $".autocanvas/automations/{name}.yml",
                CurrentVersion = 1,
                CreatedAt = Clock.GetUtcNow(),
                UpdatedAt = Clock.GetUtcNow(),
            });
            db.AutomationVersions.Add(new AutomationVersion
            {
                Id = Guid.CreateVersion7(),
                AutomationId = automation,
                Number = 1,
                Yaml = Build,
                CommitSha = new string('a', 40),
                CreatedAt = Clock.GetUtcNow(),
                CreatedByName = "git",
            });
            return await db.SaveChangesAsync(Cancel);
        });
        return automation;
    }

    private async Task<UseCaseResult<ProposedChangeView>> ProposeAsync(Actor actor, Guid automation, string yaml)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            using var http = new HttpClient();
            var access = new GitHubAccess(context, new GitHubClient(http), new SecretCipher(Key), new GitHubTokens(), Clock);
            var instance = new InstanceSettingsService(context, new NoConfiguration(), new AuditLog(context, new SystemAuditContext(), Clock), Clock);
            return await new GitHubChangeProposals(context, access, instance, new AuditLog(context, new SystemAuditContext(), Clock), Clock)
                .ProposeAsync(actor, automation, new ChangeProposal(yaml), Cancel);
        }
    }

    /// <summary>An installation whose configuration sets no addresses.</summary>
    private sealed class NoConfiguration : IInstanceConfiguration
    {
        public Uri? PublicUrl => null;

        public Uri? RunnerUrl => null;
    }
}

public sealed class ChangeProposalTestsOnSqlite() : ChangeProposalTests(DatabaseProvider.Sqlite);

public sealed class ChangeProposalTestsOnPostgres() : ChangeProposalTests(DatabaseProvider.Postgres);
