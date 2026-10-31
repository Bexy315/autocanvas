using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Tests;

public abstract class AuditQueryServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    [Fact]
    public async Task Admins_read_everything_and_others_nothing_of_the_whole_log()
    {
        var root = await ActorAsync("root", isAdmin: true);
        var ada = await ActorAsync("ada");
        await FolderAsync(ada, "Team");

        var all = (await AuditQueryAsync(a => a.ListAsync(root, new AuditFilter(), new PageRequest(), Cancel))).ShouldSucceed();

        all.Items.Select(e => e.Action).ShouldContain(AuditActions.FolderCreated, StringComparer.Ordinal);
        all.Items.Select(e => e.Action).ShouldContain(AuditActions.UserCreated, StringComparer.Ordinal);
        all.Items.ShouldBe(all.Items.OrderByDescending(e => e.OccurredAt).ToList());
        (await AuditQueryAsync(a => a.ListAsync(ada, new AuditFilter(), new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
    }

    [Fact]
    public async Task Owners_read_their_folders_but_not_restricted_ones_they_do_not_own()
    {
        var root = await ActorAsync("root", isAdmin: true);
        var ada = await ActorAsync("ada");
        var bob = await ActorAsync("bob");
        var team = await FolderAsync(ada, "Team");
        var app = await FolderAsync(ada, "App", team);
        var prod = await FolderAsync(ada, "Prod", team);
        var other = await FolderAsync(bob, "Other");
        (await FoldersAsync(f => f.SetRestrictedAsync(root, prod, true, null, Cancel))).ShouldSucceed();
        await VariablesAsync(v => v.SetAsync(ada, ConfigurationScope.Folder(app), "APP", "1", Cancel));
        await VariablesAsync(v => v.SetAsync(root, ConfigurationScope.Folder(prod), "PROD", "1", Cancel));
        await VariablesAsync(v => v.SetAsync(bob, ConfigurationScope.Folder(other), "OTHER", "1", Cancel));
        (await FolderGrantsAsync(g => g.SetAsync(ada, team, bob.UserId, null, FolderRole.Editor, Cancel))).ShouldSucceed();

        var log = (await AuditQueryAsync(a => a.ListForFolderAsync(ada, team, new AuditFilter(), new PageRequest(), Cancel))).ShouldSucceed();

        log.Items.Select(e => e.FolderId).Distinct().ShouldBe([team, app], ignoreOrder: true);
        log.Items.ShouldContain(e => e.Action == AuditActions.VariableSet && e.FolderId == app);
        log.Items.ShouldNotContain(e => e.FolderId == prod);
        (await AuditQueryAsync(a => a.ListForFolderAsync(root, team, new AuditFilter(), new PageRequest(), Cancel))).ShouldSucceed()
            .Items.ShouldContain(e => e.FolderId == prod);
        (await AuditQueryAsync(a => a.ListForFolderAsync(bob, team, new AuditFilter(), new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await AuditQueryAsync(a => a.ListForFolderAsync(ada, other, new AuditFilter(), new PageRequest(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Filters_narrow_the_log_and_combine()
    {
        var root = await ActorAsync("root", isAdmin: true);
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var start = Clock.GetUtcNow();
        Clock.Advance(TimeSpan.FromHours(1));
        await VariablesAsync(v => v.SetAsync(ada, ConfigurationScope.Folder(team), "REGION", "eu", Cancel));
        (await FoldersAsync(f => f.UpdateAsync(ada, team, new FolderChanges { Name = "Squad" }, null, Cancel))).ShouldSucceed();

        async Task<IReadOnlyList<string>> ActionsAsync(AuditFilter filter) =>
            [.. (await AuditQueryAsync(a => a.ListAsync(root, filter, new PageRequest(), Cancel))).ShouldSucceed().Items.Select(e => e.Action)];

        // Creating a folder also grants its creator Owner (folder.grant_set) in the same instant.
        (await ActionsAsync(new AuditFilter(Action: AuditActions.FolderCreated))).ShouldBe([AuditActions.FolderCreated]);
        (await ActionsAsync(new AuditFilter(Action: "folder.*"))).ShouldBe([AuditActions.FolderUpdated, AuditActions.FolderCreated, AuditActions.FolderGrantSet], ignoreOrder: true);
        (await ActionsAsync(new AuditFilter(From: start.AddMinutes(1)))).ShouldBe([AuditActions.FolderUpdated, AuditActions.VariableSet], ignoreOrder: true);
        (await ActionsAsync(new AuditFilter(To: start.AddMinutes(1), Action: "folder.*"))).ShouldBe([AuditActions.FolderCreated, AuditActions.FolderGrantSet], ignoreOrder: true);
        (await ActionsAsync(new AuditFilter(TargetType: "folder", TargetId: team.ToString()))).ShouldContain(a => a == AuditActions.FolderUpdated);
        (await ActionsAsync(new AuditFilter(TargetType: "folder", TargetId: team.ToString()))).ShouldNotContain(a => a == AuditActions.VariableSet);
        (await ActionsAsync(new AuditFilter(ActorId: Guid.CreateVersion7()))).ShouldBeEmpty();
        (await ActionsAsync(new AuditFilter(Action: "nothing.*"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Pages_cover_every_entry_once_even_within_one_instant()
    {
        var root = await ActorAsync("root", isAdmin: true);
        for (var i = 0; i < 4; i++)
        {
            await VariablesAsync(v => v.SetAsync(root, ConfigurationScope.Instance, $"V{i}", "x", Cancel));
        }

        Clock.Advance(TimeSpan.FromSeconds(1));
        await VariablesAsync(v => v.SetAsync(root, ConfigurationScope.Instance, "LATER", "x", Cancel));
        var total = await ReadAsync(db => db.AuditEvents.CountAsync(Cancel));

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = (await AuditQueryAsync(a => a.ListAsync(root, new AuditFilter(), new PageRequest(2, cursor), Cancel))).ShouldSucceed();
            seen.AddRange(page.Items.Select(e => e.Id));
            cursor = page.Next;
        }
        while (cursor is not null);

        seen.Count.ShouldBe(total);
        seen.ShouldBeUnique();
        (await AuditQueryAsync(a => a.ListAsync(root, new AuditFilter(), new PageRequest(2, "not-a-cursor"), Cancel))).ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("cursor");
        (await AuditQueryAsync(a => a.ListAsync(root, new AuditFilter(), new PageRequest(0), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }

    [Fact]
    public async Task Details_come_back_as_json_objects()
    {
        var root = await ActorAsync("root", isAdmin: true);
        await VariablesAsync(v => v.SetAsync(root, ConfigurationScope.Instance, "DOMAIN", "example.org", Cancel));

        var entry = (await AuditQueryAsync(a => a.ListAsync(root, new AuditFilter(Action: AuditActions.VariableSet), new PageRequest(), Cancel))).ShouldSucceed().Items.Single();

        entry.Details.ShouldNotBeNull().GetProperty("name").GetString().ShouldBe("DOMAIN");
        entry.TargetType.ShouldBe("variable");
    }

    [Fact]
    public async Task Retention_deletes_old_entries_and_says_so()
    {
        var root = await ActorAsync("root", isAdmin: true);
        await VariablesAsync(v => v.SetAsync(root, ConfigurationScope.Instance, "OLD", "x", Cancel));
        var old = await ReadAsync(db => db.AuditEvents.CountAsync(Cancel));
        Clock.Advance(TimeSpan.FromDays(400));
        await VariablesAsync(v => v.SetAsync(root, ConfigurationScope.Instance, "NEW", "x", Cancel));

        (await RetentionAsync(0)).ShouldBe(0);
        (await RetentionAsync(365)).ShouldBe(old);
        (await RetentionAsync(365)).ShouldBe(0);

        var left = await ReadAsync(db => db.AuditEvents.Select(e => e.Action).ToListAsync(Cancel));
        left.ShouldBe([AuditActions.VariableSet, AuditActions.AuditPruned], ignoreOrder: true);
        (await AuditAsync(AuditActions.AuditPruned)).ShouldHaveSingleItem().Details.ShouldNotBeNull().ShouldContain($"\"deleted\":{old}");
    }

    private async Task<T> AuditQueryAsync<T>(Func<AuditQueryService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new AuditQueryService(context));
        }
    }

    private async Task<int> RetentionAsync(int days)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            var retention = new AuditRetention(context, new AuditLog(context, new SystemAuditContext(), Clock), Options.Create(new AuditOptions { RetentionDays = days }), Clock);
            return await retention.PruneAsync(Cancel);
        }
    }

    private async Task<Guid> FolderAsync(Actor owner, string name, Guid? parentId = null) =>
        (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder(name, parentId), Cancel))).ShouldSucceed().Id;
}

public sealed class AuditQueryServiceTestsOnSqlite() : AuditQueryServiceTests(DatabaseProvider.Sqlite);

public sealed class AuditQueryServiceTestsOnPostgres() : AuditQueryServiceTests(DatabaseProvider.Postgres);
