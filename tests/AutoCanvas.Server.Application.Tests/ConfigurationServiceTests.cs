using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class ConfigurationServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private const string Hello = """
        version: 1
        variables:
          region: from-file
        jobs:
          hello:
            steps:
              - run: echo hello
        """;

    [Fact]
    public async Task Runs_read_the_nearest_definition_and_the_file_wins_for_variables()
    {
        var (_, _, _, _, hello) = await SetUpAsync();

        var resolved = (await ConfigurationAsync(c => c.ResolveAsync(hello, Cancel))).ShouldNotBeNull();

        resolved.Variables["REGION"].ShouldBe("from-file");
        resolved.Variables["domain"].ShouldBe("example.org");
        resolved.Variables["TIER"].ShouldBe("app");
        resolved.Secrets["token"].ShouldBe("app-token");
        resolved.Secrets["DEPLOY_KEY"].ShouldBe("automation-key");
        resolved.Secrets["SMTP"].ShouldBe("instance-mail");
        (await ConfigurationAsync(c => c.ResolveAsync(Guid.CreateVersion7(), Cancel))).ShouldBeNull();
    }

    [Fact]
    public async Task Explaining_shows_every_definition_but_never_a_secret_value()
    {
        var (_, ada, team, _, hello) = await SetUpAsync();

        var view = (await ConfigurationAsync(c => c.ExplainAutomationAsync(ada, hello, Cancel))).ShouldSucceed();
        var folderView = (await ConfigurationAsync(c => c.ExplainFolderAsync(ada, team, Cancel))).ShouldSucceed();

        var region = view.Variables.Single(v => string.Equals(v.Name, "region", StringComparison.Ordinal));
        region.Definitions.Select(d => d.Kind).ShouldBe([ConfigurationScopeKind.Instance, ConfigurationScopeKind.Folder, ConfigurationScopeKind.Automation]);
        region.Winner.Value.ShouldBe("from-file");
        region.Definitions[1].FolderName.ShouldBe("Team");
        var token = view.Secrets.Single(s => string.Equals(s.Name, "TOKEN", StringComparison.Ordinal));
        token.Definitions.Select(d => d.FolderName).ShouldBe(["Team", "App"]);
        view.Secrets.SelectMany(s => s.Definitions).ShouldAllBe(d => d.Value == null);
        folderView.Variables.Single(v => string.Equals(v.Name, "REGION", StringComparison.Ordinal)).Winner.Value.ShouldBe("team");
        folderView.Secrets.ShouldNotContain(s => s.Name == "DEPLOY_KEY");
    }

    [Fact]
    public async Task Viewers_see_names_only_and_outsiders_nothing()
    {
        var (_, ada, team, _, hello) = await SetUpAsync();
        var vic = await ActorAsync("vic");
        (await FolderGrantsAsync(g => g.SetAsync(ada, team, vic.UserId, null, FolderRole.Viewer, Cancel))).ShouldSucceed();

        var view = (await ConfigurationAsync(c => c.ExplainAutomationAsync(vic, hello, Cancel))).ShouldSucceed();

        view.Variables.ShouldNotBeEmpty();
        view.Variables.SelectMany(v => v.Definitions).ShouldAllBe(d => d.Value == null);
        var eve = await ActorAsync("eve");
        (await ConfigurationAsync(c => c.ExplainAutomationAsync(eve, hello, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await ConfigurationAsync(c => c.ExplainAutomationAsync(ada, Guid.CreateVersion7(), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await ConfigurationAsync(c => c.ExplainFolderAsync(eve, team, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task A_restricted_folder_does_not_inherit_values_from_above()
    {
        var (_, ada, _, app, hello) = await SetUpAsync();
        (await FoldersAsync(f => f.SetRestrictedAsync(ada, app, true, null, Cancel))).ShouldSucceed();

        var resolved = (await ConfigurationAsync(c => c.ResolveAsync(hello, Cancel))).ShouldNotBeNull();

        resolved.Secrets["TOKEN"].ShouldBe("app-token");
        resolved.Secrets.ContainsKey("TEAM_ONLY").ShouldBeFalse();
        resolved.Variables.ContainsKey("TEAM_ONLY").ShouldBeFalse();
        resolved.Secrets["SMTP"].ShouldBe("instance-mail");
    }

    [Fact]
    public async Task The_master_key_is_recorded_once_and_a_different_one_is_refused()
    {
        (await GuardAsync(g => g.VerifyAsync(Cancel))).ShouldSucceed();
        (await GuardAsync(g => g.VerifyAsync(Cancel))).ShouldSucceed();

        var other = MasterKey.Generate();
        var refused = (await GuardAsync(g => g.VerifyAsync(Cancel), other)).ShouldFail(UseCaseErrorKind.Conflict);

        refused.Message.ShouldContain(Key.Id);
        refused.Message.ShouldContain(other.Id);
    }

    [Fact]
    public async Task Rotation_re_encrypts_every_secret_under_the_new_key()
    {
        var (_, _, _, _, hello) = await SetUpAsync();
        var next = MasterKey.Generate();

        var count = (await GuardAsync(g => g.RotateAsync(next, Cancel))).ShouldSucceed();

        count.ShouldBe(5);
        (await ReadAsync(db => db.Secrets.ToListAsync(Cancel))).ShouldAllBe(s => s.KeyId == next.Id);
        (await GuardAsync(g => g.VerifyAsync(Cancel), next)).ShouldSucceed();
        (await GuardAsync(g => g.VerifyAsync(Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        (await ConfigurationAsync(c => c.ResolveAsync(hello, Cancel), next)).ShouldNotBeNull().Secrets["TOKEN"].ShouldBe("app-token");
        await Should.ThrowAsync<InvalidOperationException>(() => ConfigurationAsync(c => c.ResolveAsync(hello, Cancel)));
        (await AuditAsync(AuditActions.MasterKeyRotated)).ShouldHaveSingleItem().Details.ShouldNotBeNull().ShouldContain(next.Id);
    }

    [Fact]
    public async Task Rotation_changes_nothing_when_a_secret_does_not_decrypt()
    {
        await SetUpAsync();
        await ReadAsync(async db =>
        {
            var secret = await db.Secrets.FirstAsync(s => s.Name == "TOKEN", Cancel);
            secret.Ciphertext = new SecretCipher(MasterKey.Generate()).Encrypt(secret.Scope, secret.NormalizedName, "x");
            return await db.SaveChangesAsync(Cancel);
        });

        var refused = (await GuardAsync(g => g.RotateAsync(MasterKey.Generate(), Cancel))).ShouldFail(UseCaseErrorKind.Conflict);
        var same = (await GuardAsync(g => g.RotateAsync(Key, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        var wrongCurrent = (await GuardAsync(g => g.RotateAsync(MasterKey.Generate(), Cancel), MasterKey.Generate())).ShouldFail(UseCaseErrorKind.Conflict);

        refused.Message.ShouldContain("TOKEN");
        (await ReadAsync(db => db.Secrets.ToListAsync(Cancel))).ShouldAllBe(s => s.KeyId == Key.Id);
        same.Message.ShouldContain("current one");
        wrongCurrent.Message.ShouldContain("not the one");
    }

    /// <summary>
    /// Instance: DOMAIN, REGION=instance, SMTP secret. Team: REGION=team, TEAM_ONLY, TOKEN secret,
    /// TEAM_ONLY secret. Team/App: TIER=app, TOKEN secret. Automation hello in App: DEPLOY_KEY
    /// secret, region in its file.
    /// </summary>
    private async Task<(Actor Root, Actor Ada, Guid Team, Guid App, Guid Hello)> SetUpAsync()
    {
        var root = await ActorAsync("root", isAdmin: true);
        var ada = await ActorAsync("ada");
        var team = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("Team"), Cancel))).ShouldSucceed().Id;
        var app = (await FoldersAsync(f => f.CreateAsync(ada, new NewFolder("App", team), Cancel))).ShouldSucceed().Id;
        var hello = (await AutomationsAsync(a => a.CreateAsync(ada, app, "hello", Hello, null, Cancel))).ShouldSucceed().Id;

        await VariablesAsync(v => v.SetAsync(root, ConfigurationScope.Instance, "DOMAIN", "example.org", Cancel));
        await VariablesAsync(v => v.SetAsync(root, ConfigurationScope.Instance, "REGION", "instance", Cancel));
        await SecretsAsync(s => s.SetAsync(root, ConfigurationScope.Instance, "SMTP", "instance-mail", Cancel));
        await VariablesAsync(v => v.SetAsync(ada, ConfigurationScope.Folder(team), "REGION", "team", Cancel));
        await VariablesAsync(v => v.SetAsync(ada, ConfigurationScope.Folder(team), "TEAM_ONLY", "yes", Cancel));
        await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Folder(team), "TOKEN", "team-token", Cancel));
        await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Folder(team), "TEAM_ONLY", "team-secret", Cancel));
        await VariablesAsync(v => v.SetAsync(ada, ConfigurationScope.Folder(app), "TIER", "app", Cancel));
        await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Folder(app), "TOKEN", "app-token", Cancel));
        await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Automation(hello), "DEPLOY_KEY", "automation-key", Cancel));
        return (root, ada, team, app, hello);
    }
}

public sealed class ConfigurationServiceTestsOnSqlite() : ConfigurationServiceTests(DatabaseProvider.Sqlite);

public sealed class ConfigurationServiceTestsOnPostgres() : ConfigurationServiceTests(DatabaseProvider.Postgres);
