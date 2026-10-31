using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Runners;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Tests;

public abstract class RunnerServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    internal static readonly RunnerCapabilities Linux = new(["linux", "docker"], RunnerExecutor.Container, "linux", "amd64", ["http_request"], 2, "test-runner/1.0", ["bash", "sh"]);

    [Fact]
    public async Task A_registration_token_is_shown_once_listed_without_its_secret_and_audited()
    {
        var created = (await RegistrationsAsync(r => r.CreateTokenAsync("  build box  ", null, Cancel))).ShouldSucceed();
        var tokens = await RegistrationsAsync(r => r.ListTokensAsync(Cancel));

        created.Token.ShouldStartWith(RunnerRegistrationService.TokenPrefix);
        created.Details.Description.ShouldBe("build box");
        created.Details.ExpiresAt.ShouldBe(Clock.GetUtcNow() + RunnerRegistrationService.DefaultLifetime);
        created.Details.IsUsable.ShouldBeTrue();
        created.Details.CreatedBy.ShouldBe("system");
        tokens.ShouldHaveSingleItem().Hint.ShouldBe($"acrt_…{created.Token[^4..]}");
        (await ReadAsync(db => db.RunnerRegistrationTokens.SingleAsync(Cancel))).TokenHash.ShouldNotContain(created.Token[5..]);
        (await AuditAsync(AuditActions.RunnerTokenCreated)).ShouldHaveSingleItem().TargetId.ShouldBe(created.Details.Id.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task Tokens_expire_within_thirty_days(int days)
    {
        var error = (await RegistrationsAsync(r => r.CreateTokenAsync(null, Clock.GetUtcNow().AddDays(days), Cancel))).ShouldFail(UseCaseErrorKind.Validation);

        error.Field.ShouldBe("expiresAt");
    }

    [Fact]
    public async Task Descriptions_have_a_limit()
    {
        (await RegistrationsAsync(r => r.CreateTokenAsync(new string('x', 201), null, Cancel))).ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("description");
        (await RegistrationsAsync(r => r.CreateTokenAsync("line\nbreak", null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }

    [Fact]
    public async Task Registering_uses_the_token_up_and_returns_a_credential_the_runner_authenticates_with()
    {
        var token = await TokenAsync();

        var registered = (await RegistrationsAsync(r => r.RegisterAsync(token, " build-1 ", Linux, Cancel))).ShouldSucceed();
        var again = await RegistrationsAsync(r => r.RegisterAsync(token, "build-2", Linux, Cancel));
        var runner = await ConnectionsAsync(c => c.AuthenticateAsync(registered.Credential, Cancel));
        var listed = (await RegistrationsAsync(r => r.ListTokensAsync(Cancel))).ShouldHaveSingleItem();

        registered.Credential.ShouldStartWith(RunnerRegistrationService.CredentialPrefix);
        again.ShouldFail(UseCaseErrorKind.Conflict).Field.ShouldBe("registrationToken");
        runner.ShouldBe(new RunnerState(registered.RunnerId, "build-1", IsPaused: false));
        listed.IsUsable.ShouldBeFalse();
        listed.RunnerId.ShouldBe(registered.RunnerId);
        var audit = (await AuditAsync(AuditActions.RunnerRegistered)).ShouldHaveSingleItem();
        audit.ActorKind.ShouldBe(AuditActorKind.Runner);
        audit.ActorId.ShouldBe(registered.RunnerId);
    }

    [Fact]
    public async Task Unknown_and_expired_tokens_do_not_register()
    {
        var token = await TokenAsync();
        Clock.Advance(RunnerRegistrationService.DefaultLifetime);

        (await RegistrationsAsync(r => r.RegisterAsync(token, "late", Linux, Cancel))).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await RegistrationsAsync(r => r.RegisterAsync("acrt_unknown", "other", Linux, Cancel))).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await RegistrationsAsync(r => r.RegisterAsync("not a token", "other", Linux, Cancel))).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await ReadAsync(db => db.Runners.CountAsync(Cancel))).ShouldBe(0);
    }

    [Fact]
    public async Task Names_are_unique_regardless_of_case_and_a_refused_registration_keeps_the_token()
    {
        await RegisterAsync("Build-1");
        var token = await TokenAsync();

        (await RegistrationsAsync(r => r.RegisterAsync(token, "BUILD-1", Linux, Cancel))).ShouldFail(UseCaseErrorKind.Conflict).Field.ShouldBe("name");
        (await RegistrationsAsync(r => r.RegisterAsync(token, "", Linux, Cancel))).ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("name");
        (await RegistrationsAsync(r => r.RegisterAsync(token, "build-2", Linux, Cancel))).ShouldSucceed();
    }

    [Theory]
    [InlineData("max_concurrent_jobs")]
    [InlineData("labels")]
    [InlineData("os")]
    public async Task Capabilities_that_make_no_sense_are_refused(string field)
    {
        var capabilities = field switch
        {
            "max_concurrent_jobs" => Linux with { MaxConcurrentJobs = 0 },
            "labels" => Linux with { Labels = [.. Enumerable.Range(0, 33).Select(i => $"label-{i}")] },
            _ => Linux with { Os = new string('x', 65) },
        };
        var token = await TokenAsync();

        (await RegistrationsAsync(r => r.RegisterAsync(token, "runner", capabilities, Cancel))).ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe($"capabilities.{field}");
    }

    [Fact]
    public async Task Deleting_a_token_keeps_the_runner_it_registered()
    {
        var created = (await RegistrationsAsync(r => r.CreateTokenAsync(null, null, Cancel))).ShouldSucceed();
        await RegistrationsAsync(r => r.RegisterAsync(created.Token, "keeper", Linux, Cancel));

        (await RegistrationsAsync(r => r.DeleteTokenAsync(created.Details.Id, Cancel))).ShouldSucceed();
        (await RegistrationsAsync(r => r.DeleteTokenAsync(created.Details.Id, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await ReadAsync(db => db.Runners.CountAsync(Cancel))).ShouldBe(1);
        (await AuditAsync(AuditActions.RunnerTokenDeleted)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Runners_are_listed_by_name_page_by_page_and_show_whether_they_are_online()
    {
        var b = await RegisterAsync("b-runner");
        Clock.Advance(TimeSpan.FromMinutes(1));
        await RegisterAsync("A-runner");
        await RegisterAsync("c-runner");

        var first = (await RunnersAsync(r => r.ListAsync(new PageRequest(2), Cancel))).ShouldSucceed();
        var second = (await RunnersAsync(r => r.ListAsync(new PageRequest(2, first.Next), Cancel))).ShouldSucceed();
        var one = (await RunnersAsync(r => r.GetAsync(b.RunnerId, Cancel))).ShouldSucceed();

        first.Items.Select(r => r.Name).ShouldBe(["A-runner", "b-runner"]);
        first.Items[0].IsOnline.ShouldBeTrue();
        first.Items[1].IsOnline.ShouldBeFalse();
        second.Items.ShouldHaveSingleItem().Name.ShouldBe("c-runner");
        second.Next.ShouldBeNull();
        one.Capabilities.ShouldBe(Linux, new CapabilitiesComparer());
        (await RunnersAsync(r => r.GetAsync(Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await RunnersAsync(r => r.ListAsync(new PageRequest(Cursor: "%%%"), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
    }

    [Fact]
    public async Task Runners_are_renamed_paused_and_resumed_with_their_version_checked()
    {
        var runner = await RegisterAsync("build-1");
        await RegisterAsync("build-2");

        var paused = (await RunnersAsync(r => r.UpdateAsync(runner.RunnerId, new RunnerChanges("renamed", IsPaused: true), 1, Cancel))).ShouldSucceed();
        var stale = await RunnersAsync(r => r.UpdateAsync(runner.RunnerId, new RunnerChanges(IsPaused: false), 1, Cancel));
        var taken = await RunnersAsync(r => r.UpdateAsync(runner.RunnerId, new RunnerChanges("BUILD-2"), null, Cancel));
        var unchanged = (await RunnersAsync(r => r.UpdateAsync(runner.RunnerId, new RunnerChanges("renamed", IsPaused: true), null, Cancel))).ShouldSucceed();

        paused.Name.ShouldBe("renamed");
        paused.IsPaused.ShouldBeTrue();
        paused.Version.ShouldBe(2);
        stale.ShouldFail(UseCaseErrorKind.PreconditionFailed);
        taken.ShouldFail(UseCaseErrorKind.Conflict);
        unchanged.Version.ShouldBe(2);
        (await ConnectionsAsync(c => c.StateAsync(runner.RunnerId, Cancel)))!.IsPaused.ShouldBeTrue();
        (await RunnersAsync(r => r.UpdateAsync(runner.RunnerId, new RunnerChanges(" "), null, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await RunnersAsync(r => r.UpdateAsync(Guid.CreateVersion7(), new RunnerChanges(IsPaused: true), null, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await AuditAsync(AuditActions.RunnerUpdated)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Deleting_a_runner_revokes_its_credential_and_gives_up_its_jobs()
    {
        var runner = await RegisterAsync("doomed");
        var attempt = await HeldAttemptAsync(runner.RunnerId);

        (await RunnersAsync(r => r.DeleteAsync(runner.RunnerId, 2, Cancel))).ShouldFail(UseCaseErrorKind.PreconditionFailed);
        (await RunnersAsync(r => r.DeleteAsync(runner.RunnerId, 1, Cancel))).ShouldSucceed();

        (await ConnectionsAsync(c => c.AuthenticateAsync(runner.Credential, Cancel))).ShouldBeNull();
        (await ConnectionsAsync(c => c.HeartbeatAsync(runner.RunnerId, Cancel))).ShouldBeNull();
        (await ConnectionsAsync(c => c.ConnectAsync(runner.RunnerId, Linux, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await ReadAsync(db => db.JobAttempts.SingleAsync(a => a.Id == attempt, Cancel))).LeaseExpiresAt.ShouldBe(Clock.GetUtcNow());
        (await RunnersAsync(r => r.DeleteAsync(runner.RunnerId, null, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await AuditAsync(AuditActions.RunnerDeleted)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Connecting_replaces_the_capabilities_and_heartbeats_keep_the_runner_online()
    {
        var runner = await RegisterAsync("worker");
        var other = Linux with { Labels = ["windows"], Executor = RunnerExecutor.Host };
        Clock.Advance(TimeSpan.FromMinutes(5));

        (await ConnectionsAsync(c => c.ConnectAsync(runner.RunnerId, other, Cancel))).ShouldSucceed().Name.ShouldBe("worker");
        (await ConnectionsAsync(c => c.ConnectAsync(runner.RunnerId, other with { MaxConcurrentJobs = 0 }, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        var view = (await RunnersAsync(r => r.GetAsync(runner.RunnerId, Cancel))).ShouldSucceed();
        Clock.Advance(TimeSpan.FromMinutes(5));
        (await ConnectionsAsync(c => c.HeartbeatAsync(runner.RunnerId, Cancel))).ShouldNotBeNull();
        var later = (await RunnersAsync(r => r.GetAsync(runner.RunnerId, Cancel))).ShouldSucceed();

        view.Capabilities.Labels.ShouldBe(["windows"]);
        view.Capabilities.RunsContainers.ShouldBeFalse();
        later.LastSeenAt.ShouldBe(Clock.GetUtcNow());
        later.IsOnline.ShouldBeTrue();
        (await ConnectionsAsync(c => c.AuthenticateAsync("acp_a_user_token", Cancel))).ShouldBeNull();
        (await ConnectionsAsync(c => c.AuthenticateAsync("acr_unknown", Cancel))).ShouldBeNull();
    }

    private async Task<string> TokenAsync() => (await RegistrationsAsync(r => r.CreateTokenAsync(null, null, Cancel))).ShouldSucceed().Token;

    private async Task<RegisteredRunner> RegisterAsync(string name)
    {
        var token = await TokenAsync();
        return (await RegistrationsAsync(r => r.RegisterAsync(token, name, Linux, Cancel))).ShouldSucceed();
    }

    /// <summary>An attempt of a fresh run that the runner holds, as if it had been offered it.</summary>
    private async Task<Guid> HeldAttemptAsync(Guid runnerId)
    {
        var ada = await ActorAsync("ada");
        var folder = (await FoldersAsync(f => f.CreateAsync(ada, new Folders.NewFolder("team"), Cancel))).ShouldSucceed().Id;
        var automation = (await AutomationsAsync(a => a.CreateAsync(ada, folder, "flow", "version: 1\ntriggers:\n  manual: true\njobs:\n  build:\n    steps:\n      - run: echo hi\n", null, Cancel))).ShouldSucceed().Id;
        var run = (await RunsAsync(r => r.StartAsync(ada, automation, Cancel))).ShouldSucceed().Id;
        return await ReadAsync(async db =>
        {
            var attempt = await db.JobAttempts.Join(db.RunJobs.Where(j => j.RunId == run), a => a.RunJobId, j => j.Id, (a, _) => a).SingleAsync(Cancel);
            attempt.Status = AttemptStatus.Running;
            attempt.RunnerId = runnerId;
            attempt.LeaseExpiresAt = Clock.GetUtcNow().AddMinutes(1);
            await db.SaveChangesAsync(Cancel);
            return attempt.Id;
        });
    }

    private async Task<T> RegistrationsAsync<T>(Func<RunnerRegistrationService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            var audit = new SystemAuditContext();
            return await action(new RunnerRegistrationService(context, new AuditLog(context, audit, Clock), audit, Clock));
        }
    }

    private async Task<T> RunnersAsync<T>(Func<RunnerService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new RunnerService(context, new AuditLog(context, new SystemAuditContext(), Clock), Options.Create(new RunnerOptions()), Clock));
        }
    }

    private async Task<T> ConnectionsAsync<T>(Func<RunnerConnectionService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new RunnerConnectionService(context, Clock));
        }
    }

    /// <summary>Records compare lists by reference; capabilities are equal when their contents are.</summary>
    private sealed class CapabilitiesComparer : IEqualityComparer<RunnerCapabilities>
    {
        public bool Equals(RunnerCapabilities? x, RunnerCapabilities? y) => x is not null && y is not null && string.Equals(x.Write(), y.Write(), StringComparison.Ordinal);

        public int GetHashCode(RunnerCapabilities obj) => obj.Write().GetHashCode(StringComparison.Ordinal);
    }
}

public sealed class RunnerServiceTestsOnSqlite() : RunnerServiceTests(DatabaseProvider.Sqlite);

public sealed class RunnerServiceTestsOnPostgres() : RunnerServiceTests(DatabaseProvider.Postgres);
