using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Persistence.Identity;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Automations;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace AutoCanvas.Server.Application.Tests;

/// <summary>Base for identity use case tests: a migrated database, a cheap hasher and a clock.</summary>
public abstract class IdentityTest(DatabaseProvider provider) : DatabaseTest(provider), IAsyncLifetime
{
    protected static readonly PasswordHasher Hasher = new(new PasswordHashingOptions { Iterations = 1_000 });

    protected FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        await Database.MigrateAsync(cancellationToken: Cancel);
    }

    /// <summary>Runs a use case on a fresh context, as one request would.</summary>
    protected async Task<T> UsersAsync<T>(Func<UserService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new UserService(context, Hasher, new AuditLog(context, new SystemAuditContext(), Clock), Clock));
        }
    }

    /// <summary>Runs a group use case on a fresh context.</summary>
    protected async Task<T> GroupsAsync<T>(Func<GroupService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new GroupService(context, new AuditLog(context, new SystemAuditContext(), Clock), Clock));
        }
    }

    /// <summary>Runs a folder use case on a fresh context.</summary>
    protected async Task<T> FoldersAsync<T>(Func<FolderService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new FolderService(context, new AuditLog(context, new SystemAuditContext(), Clock), Clock));
        }
    }

    /// <summary>Runs a grant use case on a fresh context.</summary>
    protected async Task<T> FolderGrantsAsync<T>(Func<FolderGrantService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new FolderGrantService(context, new AuditLog(context, new SystemAuditContext(), Clock), Clock));
        }
    }

    /// <summary>Runs an automation use case on a fresh context.</summary>
    protected async Task<T> AutomationsAsync<T>(Func<AutomationService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new AutomationService(context, new AuditLog(context, new SystemAuditContext(), Clock), Clock));
        }
    }

    /// <summary>A new non-admin user as folder actor.</summary>
    protected async Task<Actor> ActorAsync(string userName, bool isAdmin = false) =>
        new((await CreateUserAsync(userName, isAdmin)).Id, isAdmin);

    /// <summary>Reads from a fresh context.</summary>
    protected async Task<T> ReadAsync<T>(Func<AutoCanvasDbContext, Task<T>> query)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await query(context);
        }
    }

    protected async Task<IReadOnlyList<AuditEvent>> AuditAsync(string action) =>
        await ReadAsync(db => db.AuditEvents.AsNoTracking().Where(e => e.Action == action).ToListAsync(Cancel));

    protected async Task<UserView> CreateUserAsync(string userName, bool isAdmin = false) =>
        (await UsersAsync(users => users.CreateAsync(new NewUser(userName, "a long enough password") { IsAdmin = isAdmin }, Cancel)))
        .ShouldSucceed();

    protected Task AddSessionAsync(Guid userId) => ReadAsync(async db =>
    {
        db.Sessions.Add(new Session
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            CreatedAt = Clock.GetUtcNow(),
            LastSeenAt = Clock.GetUtcNow(),
            ExpiresAt = Clock.GetUtcNow().AddDays(1),
        });
        return await db.SaveChangesAsync(Cancel);
    });
}

internal static class ResultAssertions
{
    public static T ShouldSucceed<T>(this UseCaseResult<T> result)
    {
        result.Error.ShouldBeNull();
        return result.Value!;
    }

    public static UseCaseError ShouldFail<T>(this UseCaseResult<T> result, UseCaseErrorKind kind)
    {
        var error = result.Error.ShouldNotBeNull();
        error.Kind.ShouldBe(kind);
        return error;
    }
}
