using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace AutoCanvas.Server.Application.Tests;

public abstract class AuditLogTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Tags = ["prod"];
    private readonly FakeTimeProvider clock = new(Now);

    [Fact]
    public async Task Entries_are_written_with_actor_time_and_details_when_the_use_case_saves()
    {
        await Database.MigrateAsync(cancellationToken: Cancel);
        var folder = Guid.CreateVersion7();
        var user = new AuditActor(AuditActorKind.User, Guid.CreateVersion7(), "ada");

        var context = Database.CreateContext();
        await using (context)
        {
            Log(context, new TestAuditContext(user, "203.0.113.7", "curl/8.10")).Record(new AuditEntry("folder.created")
            {
                TargetType = "folder",
                TargetId = folder.ToString(),
                FolderId = folder,
                Details = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "Deployments", ["tags"] = Tags },
            });
            await context.SaveChangesAsync(Cancel);
        }

        var e = (await ReadAllAsync()).ShouldHaveSingleItem();
        e.OccurredAt.ShouldBe(Now);
        e.Id.Version.ShouldBe(7);
        e.ActorKind.ShouldBe(AuditActorKind.User);
        e.ActorId.ShouldBe(user.Id);
        e.ActorName.ShouldBe("ada");
        e.Action.ShouldBe("folder.created");
        e.TargetType.ShouldBe("folder");
        e.TargetId.ShouldBe(folder.ToString());
        e.FolderId.ShouldBe(folder);
        e.ClientAddress.ShouldBe("203.0.113.7");
        e.UserAgent.ShouldBe("curl/8.10");
        var details = JsonDocument.Parse(e.Details!).RootElement;
        details.GetProperty("name").GetString().ShouldBe("Deployments");
        details.GetProperty("tags")[0].GetString().ShouldBe("prod");
    }

    [Fact]
    public async Task Entries_roll_back_with_the_change_they_describe()
    {
        await Database.MigrateAsync(cancellationToken: Cancel);

        var context = Database.CreateContext();
        await using (context)
        {
            var transaction = await context.Database.BeginTransactionAsync(Cancel);
            await using (transaction)
            {
                Log(context).Record(new AuditEntry("folder.created"));
                await context.SaveChangesAsync(Cancel);
                await transaction.RollbackAsync(Cancel);
            }
        }

        (await ReadAllAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_written_without_saving()
    {
        await Database.MigrateAsync(cancellationToken: Cancel);

        var context = Database.CreateContext();
        await using (context)
        {
            Log(context).Record(new AuditEntry("folder.created"));
        }

        (await ReadAllAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Client_values_longer_than_their_column_are_cut()
    {
        await Database.MigrateAsync(cancellationToken: Cancel);
        var longName = new string('n', AuditEvent.MaxActorNameLength + 10);
        var longAgent = new string('a', AuditEvent.MaxUserAgentLength + 10);

        var context = Database.CreateContext();
        await using (context)
        {
            var actor = new AuditActor(AuditActorKind.User, Guid.CreateVersion7(), longName);
            Log(context, new TestAuditContext(actor, null, longAgent)).Record(new AuditEntry("folder.created"));
            await context.SaveChangesAsync(Cancel);
        }

        var e = (await ReadAllAsync()).ShouldHaveSingleItem();
        e.ActorName.Length.ShouldBe(AuditEvent.MaxActorNameLength);
        e.UserAgent!.Length.ShouldBe(AuditEvent.MaxUserAgentLength);
        e.ClientAddress.ShouldBeNull();
        e.Details.ShouldBeNull();
    }

    private AuditLog Log(AutoCanvasDbContext context, IAuditContext? audit = null) =>
        new(context, audit ?? new SystemAuditContext(), clock);

    private async Task<List<AuditEvent>> ReadAllAsync()
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await context.AuditEvents.AsNoTracking().ToListAsync(Cancel);
        }
    }
}

public sealed class AuditLogTestsOnSqlite() : AuditLogTests(DatabaseProvider.Sqlite);

public sealed class AuditLogTestsOnPostgres() : AuditLogTests(DatabaseProvider.Postgres);

internal sealed record TestAuditContext(AuditActor Actor, string? ClientAddress, string? UserAgent) : IAuditContext;
