using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoCanvas.Forge.GitHub;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Forges;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Forges;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

/// <summary>What the forges' webhooks bring in, before anything is read from a repository.</summary>
public abstract class ForgeEventTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private const string AppSecret = "app-webhook-secret";
    private const long ShopId = 9001;
    private static readonly string Before = new('a', 40);
    private static readonly string After = new('b', 40);
    private static readonly string[] Added = ["a.c"];

    [Fact]
    public async Task The_apps_events_are_signed_taken_once_and_stored_for_connected_repositories()
    {
        (await ReceiveAsync("ping", "d0", "{}")).ShouldFail(UseCaseErrorKind.NotFound);
        var repository = await SetUpAsync();
        var push = JsonSerializer.Serialize(new { @ref = "refs/heads/main", before = Before, after = After, repository = new { id = ShopId }, pusher = new { name = "octocat" } });

        (await ReceiveAsync("push", "d1", push, signature: "sha256=00")).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await ReceiveAsync("ping", "d2", "{}")).ShouldSucceed().Message.ShouldBe("pong");
        (await ReceiveAsync("push", "d3", push)).ShouldSucceed().Queued.ShouldBeTrue();
        (await ReceiveAsync("push", "d3", push)).ShouldSucceed().Message.ShouldContain("taken already");
        (await ReceiveAsync("issues", "d4", "{}")).ShouldSucceed().Queued.ShouldBeFalse();
        (await ReceiveAsync("push", "d5", "not json")).ShouldSucceed().Queued.ShouldBeFalse();
        (await ReceiveAsync("push", "d6", "{}")).ShouldSucceed().Queued.ShouldBeFalse();
        var elsewhere = JsonSerializer.Serialize(new { @ref = "refs/heads/main", before = Before, after = After, repository = new { id = 1 } });
        (await ReceiveAsync("push", "d7", elsewhere)).ShouldSucceed().Message.ShouldContain("not connected");

        var stored = await ReadAsync(db => db.RepositoryPushes.AsNoTracking().SingleAsync(Cancel));
        (stored.RepositoryId, stored.Ref, stored.After, stored.Pusher).ShouldBe((repository, "refs/heads/main", After, "octocat"));
    }

    [Theory]
    [InlineData(ShopId, false)]
    [InlineData(4711L, true)]
    public async Task Pull_requests_are_stored_with_whether_they_come_from_a_fork(long headRepository, bool fork)
    {
        var repository = await SetUpAsync();

        (await ReceiveAsync("pull_request", "p1", PullRequest("opened", headRepository))).ShouldSucceed().Queued.ShouldBeTrue();
        (await ReceiveAsync("pull_request", "p2", PullRequest("closed", headRepository))).ShouldSucceed().Queued.ShouldBeFalse();
        (await ReceiveAsync("pull_request", "p3", """{"action":"opened","pull_request":{"number":"x"}}""")).ShouldSucceed().Queued.ShouldBeFalse();

        var stored = await ReadAsync(db => db.RepositoryPullRequests.AsNoTracking().SingleAsync(Cancel));
        (stored.RepositoryId, stored.Number, stored.Action, stored.HeadSha, stored.HeadBranch, stored.BaseBranch, stored.Title, stored.Author, stored.Fork)
            .ShouldBe((repository, 12, "opened", After, "feature/x", "main", "Make it faster", "octocat", fork));
        stored.Url.ShouldBe(new Uri("https://github.com/acme/shop/pull/12"));
    }

    [Fact]
    public async Task A_pull_request_whose_fork_is_gone_counts_as_a_fork()
    {
        await SetUpAsync();
        var body = JsonSerializer.Serialize(new
        {
            action = "synchronize",
            pull_request = new { number = 3, head = new { @ref = "x", sha = After, repo = (object?)null }, @base = new { @ref = "main", repo = new { id = ShopId } } },
        });

        (await ReceiveAsync("pull_request", "p1", body)).ShouldSucceed().Queued.ShouldBeTrue();

        var stored = await ReadAsync(db => db.RepositoryPullRequests.AsNoTracking().SingleAsync(Cancel));
        (stored.Fork, stored.Author, stored.Title, stored.Url).ShouldBe((true, "github", string.Empty, null));
    }

    [Fact]
    public async Task An_uninstalled_app_marks_the_accounts_repositories()
    {
        var repository = await SetUpAsync();

        (await ReceiveAsync("installation", "i1", """{"action":"created","installation":{"id":7}}""")).ShouldSucceed().Message.ShouldContain("nothing to do");
        (await ReceiveAsync("installation", "i2", """{"action":"suspend","installation":{"id":7}}""")).ShouldSucceed();
        (await ReadAsync(db => db.Repositories.AsNoTracking().SingleAsync(r => r.Id == repository, Cancel))).LastError!.ShouldContain("suspended");
        (await ReceiveAsync("installation", "i3", """{"action":"deleted","installation":{"id":7}}""")).ShouldSucceed();
        (await ReadAsync(db => db.Repositories.AsNoTracking().SingleAsync(r => r.Id == repository, Cancel))).LastError!.ShouldContain("uninstalled");
    }

    [Fact]
    public async Task A_repositorys_own_webhook_takes_GitHub_Gitea_and_GitLab_pushes_and_is_managed_by_editors()
    {
        var repository = await SetUpAsync();
        var editor = await ActorAsync("root", isAdmin: true);
        var created = (await WebhooksAsync(w => w.CreateAsync(editor, repository, Cancel))).ShouldSucceed();
        var push = JsonSerializer.Serialize(new { @ref = "refs/heads/main", before = Before, after = After, commits = new[] { new { id = After, message = "Fix", added = Added } } });

        (await HookAsync(created.Token, push, ("X-GitHub-Event", "push"), ("X-Hub-Signature-256", Sign(created.Secret, push)))).ShouldSucceed().Queued.ShouldBeTrue();
        (await HookAsync(created.Token, push, ("X-Gitea-Event", "push"), ("X-Gitea-Signature", Sign(created.Secret, push)[7..]))).ShouldSucceed().Queued.ShouldBeTrue();
        (await HookAsync(created.Token, push, ("X-Gitlab-Event", "Push Hook"), ("X-Gitlab-Token", created.Secret))).ShouldSucceed().Queued.ShouldBeTrue();
        (await HookAsync(created.Token, "{}", ("X-GitHub-Event", "ping"), ("X-Hub-Signature-256", Sign(created.Secret, "{}")))).ShouldSucceed().Message.ShouldBe("pong");
        (await HookAsync(created.Token, push, ("X-Gitlab-Token", "wrong"))).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await HookAsync("acg_unknown", push)).ShouldFail(UseCaseErrorKind.NotFound);
        (await ReadAsync(db => db.RepositoryPushes.CountAsync(Cancel))).ShouldBe(3);

        (await WebhooksAsync(w => w.DeleteAsync(editor, repository, Cancel))).ShouldSucceed();
        (await WebhooksAsync(w => w.DeleteAsync(editor, repository, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        var stranger = await ActorAsync("stranger");
        (await WebhooksAsync(w => w.CreateAsync(stranger, repository, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await WebhooksAsync(w => w.CreateAsync(editor, Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    /// <summary>A folder with a GitHub repository (installation 7) and the GitHub App; the repository's id.</summary>
    private async Task<Guid> SetUpAsync()
    {
        var admin = await ActorAsync("owner", isAdmin: true);
        var folder = (await FoldersAsync(f => f.CreateAsync(admin, new NewFolder("Shop"), Cancel))).ShouldSucceed().Id;
        var cipher = new SecretCipher(Key);
        var id = Guid.CreateVersion7();
        await ReadAsync(async db =>
        {
            db.Repositories.Add(new Repository
            {
                Id = id,
                FolderId = folder,
                Name = "shop",
                NormalizedName = IdentityNames.Normalize("shop"),
                Kind = RepositoryKind.GitHub,
                Url = new Uri("https://github.com/acme/shop.git"),
                InstallationId = 7,
                ExternalId = ShopId,
                CreatedAt = Clock.GetUtcNow(),
                CreatedByName = "owner",
                UpdatedAt = Clock.GetUtcNow(),
            });
            db.GitHubApps.Add(new GitHubApp
            {
                AppId = 42,
                Slug = "autocanvas",
                Name = "AutoCanvas",
                OwnerLogin = "acme",
                HtmlUrl = new Uri("https://github.com/apps/autocanvas"),
                WebAddress = new Uri("https://github.com"),
                ApiAddress = new Uri("https://api.github.com"),
                PrivateKeyCiphertext = cipher.Encrypt(GitHubAccess.SecretScope, GitHubAccess.PrivateKeyName, "not used here"),
                WebhookSecretCiphertext = cipher.Encrypt(GitHubAccess.SecretScope, GitHubAccess.WebhookSecretName, AppSecret),
                CreatedAt = Clock.GetUtcNow(),
                CreatedByName = "owner",
            });
            return await db.SaveChangesAsync(Cancel);
        });
        return id;
    }

    private static string PullRequest(string action, long headRepository) => JsonSerializer.Serialize(new
    {
        action,
        pull_request = new
        {
            number = 12,
            title = "Make it faster",
            html_url = "https://github.com/acme/shop/pull/12",
            user = new { login = "octocat" },
            head = new { @ref = "feature/x", sha = After, repo = new { id = headRepository } },
            @base = new { @ref = "main", repo = new { id = ShopId } },
        },
    });

    private static string Sign(string secret, string body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    private async Task<UseCaseResult<ReceivedHookView>> ReceiveAsync(string eventName, string delivery, string body, string? signature = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-GitHub-Event"] = eventName,
            ["X-GitHub-Delivery"] = delivery,
            ["X-Hub-Signature-256"] = signature ?? Sign(AppSecret, body),
        };
        var context = Database.CreateContext();
        await using (context)
        {
            using var http = new HttpClient();
            var access = new GitHubAccess(context, new GitHubClient(http), new SecretCipher(Key), new GitHubTokens(), Clock);
            using var signal = new RepositoryPushSignal();
            return await new GitHubEventService(context, access, signal, Clock).ReceiveAsync(new RepositoryHookCall(Encoding.UTF8.GetBytes(body), headers), Cancel);
        }
    }

    private async Task<UseCaseResult<ReceivedHookView>> HookAsync(string token, string body, params (string Name, string Value)[] headers) =>
        await WebhooksAsync(w => w.ReceiveAsync(token, new RepositoryHookCall(Encoding.UTF8.GetBytes(body), headers.ToDictionary(h => h.Name, h => h.Value, StringComparer.OrdinalIgnoreCase)), Cancel));

    private async Task<T> WebhooksAsync<T>(Func<RepositoryWebhookService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            using var signal = new RepositoryPushSignal();
            return await action(new RepositoryWebhookService(context, new SecretCipher(Key), signal, new AuditLog(context, new SystemAuditContext(), Clock), Clock));
        }
    }
}

public sealed class ForgeEventTestsOnSqlite() : ForgeEventTests(DatabaseProvider.Sqlite);

public sealed class ForgeEventTestsOnPostgres() : ForgeEventTests(DatabaseProvider.Postgres);
