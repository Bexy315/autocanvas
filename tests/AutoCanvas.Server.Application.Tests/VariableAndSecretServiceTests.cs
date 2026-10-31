using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Variables;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class VariableAndSecretServiceTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private const string Hello = """
        version: 1
        variables:
          REGION: eu-file
        jobs:
          hello:
            steps:
              - run: echo hello
        """;

    [Fact]
    public async Task Folder_roles_decide_who_sees_names_values_and_changes()
    {
        var ada = await ActorAsync("ada");
        var viewer = await ActorAsync("vic");
        var operatorUser = await ActorAsync("olga");
        var outsider = await ActorAsync("eve");
        var team = await FolderAsync(ada, "Team");
        await GrantAsync(ada, team, viewer, FolderRole.Viewer);
        await GrantAsync(ada, team, operatorUser, FolderRole.Operator);
        var scope = ConfigurationScope.Folder(team);

        (await VariablesAsync(v => v.SetAsync(ada, scope, "REGION", "eu", Cancel))).ShouldSucceed().Created.ShouldBeTrue();
        (await SecretsAsync(s => s.SetAsync(ada, scope, "TOKEN", "s3cr3t-value", Cancel))).ShouldSucceed();

        (await VariablesAsync(v => v.ListAsync(viewer, scope, Cancel))).ShouldSucceed().ShouldHaveSingleItem().Value.ShouldBeNull();
        (await VariablesAsync(v => v.ListAsync(operatorUser, scope, Cancel))).ShouldSucceed().ShouldHaveSingleItem().Value.ShouldBe("eu");
        (await SecretsAsync(s => s.ListAsync(viewer, scope, Cancel))).ShouldSucceed().ShouldHaveSingleItem().Name.ShouldBe("TOKEN");
        (await VariablesAsync(v => v.SetAsync(operatorUser, scope, "REGION", "us", Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await SecretsAsync(s => s.DeleteAsync(operatorUser, scope, "TOKEN", Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await VariablesAsync(v => v.ListAsync(outsider, scope, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await SecretsAsync(s => s.ListAsync(outsider, scope, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Everyone_reads_the_instance_scope_and_only_admins_change_it()
    {
        var root = await ActorAsync("root", isAdmin: true);
        var ada = await ActorAsync("ada");

        (await VariablesAsync(v => v.SetAsync(root, ConfigurationScope.Instance, "DOMAIN", "example.org", Cancel))).ShouldSucceed();
        (await SecretsAsync(s => s.SetAsync(root, ConfigurationScope.Instance, "SMTP_PASSWORD", "mail-pass", Cancel))).ShouldSucceed();

        (await VariablesAsync(v => v.ListAsync(ada, ConfigurationScope.Instance, Cancel))).ShouldSucceed().ShouldHaveSingleItem().Value.ShouldBe("example.org");
        (await SecretsAsync(s => s.ListAsync(ada, ConfigurationScope.Instance, Cancel))).ShouldSucceed().ShouldHaveSingleItem().Name.ShouldBe("SMTP_PASSWORD");
        (await VariablesAsync(v => v.SetAsync(ada, ConfigurationScope.Instance, "DOMAIN", "evil.org", Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
        (await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Instance, "X", "value", Cancel))).ShouldFail(UseCaseErrorKind.Forbidden);
    }

    [Fact]
    public async Task Names_are_unique_per_scope_regardless_of_case_and_follow_the_expression_rules()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var scope = ConfigurationScope.Folder(team);
        await VariablesAsync(v => v.SetAsync(ada, scope, "region", "eu", Cancel));
        Clock.Advance(TimeSpan.FromMinutes(1));

        var replaced = (await VariablesAsync(v => v.SetAsync(ada, scope, "REGION", "us", Cancel))).ShouldSucceed();

        replaced.Created.ShouldBeFalse();
        replaced.Item.Name.ShouldBe("REGION");
        replaced.Item.UpdatedAt.ShouldBe(Clock.GetUtcNow());
        (await VariablesAsync(v => v.ListAsync(ada, scope, Cancel))).ShouldSucceed().ShouldHaveSingleItem().Value.ShouldBe("us");
        foreach (var bad in new[] { "", "1ABC", "A-B", "A B", new string('A', 101) })
        {
            (await VariablesAsync(v => v.SetAsync(ada, scope, bad, "x", Cancel))).ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("name");
        }

        (await VariablesAsync(v => v.SetAsync(ada, scope, "EMPTY_IS_FINE", string.Empty, Cancel))).ShouldSucceed();
        (await SecretsAsync(s => s.SetAsync(ada, scope, "EMPTY", string.Empty, Cancel))).ShouldFail(UseCaseErrorKind.Validation).Field.ShouldBe("value");
        (await SecretsAsync(s => s.SetAsync(ada, scope, "HUGE", new string('x', Secret.MaxValueLength + 1), Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await VariablesAsync(v => v.DeleteAsync(ada, scope, "nope", Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await SecretsAsync(s => s.DeleteAsync(ada, scope, "nope", Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Automation_variables_belong_in_the_file_but_its_secrets_are_stored()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var hello = (await AutomationsAsync(a => a.CreateAsync(ada, team, "hello", Hello, null, Cancel))).ShouldSucceed();
        var scope = ConfigurationScope.Automation(hello.Id);

        (await VariablesAsync(v => v.SetAsync(ada, scope, "REGION", "eu", Cancel))).ShouldFail(UseCaseErrorKind.Validation).Message.ShouldContain("variables:");
        (await VariablesAsync(v => v.ListAsync(ada, scope, Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await VariablesAsync(v => v.DeleteAsync(ada, scope, "REGION", Cancel))).ShouldFail(UseCaseErrorKind.Validation);
        (await SecretsAsync(s => s.SetAsync(ada, scope, "DEPLOY_KEY", "key-value", Cancel))).ShouldSucceed();
        var eve = await ActorAsync("eve");
        (await SecretsAsync(s => s.ListAsync(eve, scope, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await SecretsAsync(s => s.ListAsync(ada, ConfigurationScope.Automation(Guid.CreateVersion7()), Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    [Fact]
    public async Task Secret_values_are_neither_stored_in_plain_text_nor_audited()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Folder(team), "TOKEN", "s3cr3t-value", Cancel));
        await VariablesAsync(v => v.SetAsync(ada, ConfigurationScope.Folder(team), "REGION", "eu-central", Cancel));

        var secret = await ReadAsync(db => db.Secrets.SingleAsync(Cancel));
        var events = await ReadAsync(db => db.AuditEvents.Where(e => e.Action == AuditActions.SecretSet || e.Action == AuditActions.VariableSet).ToListAsync(Cancel));

        secret.Ciphertext.ShouldNotContain("s3cr3t");
        secret.KeyId.ShouldBe(Key.Id);
        new SecretCipher(Key).Decrypt(ValueScopes.Folder(team), "TOKEN", secret.Ciphertext).ShouldBe("s3cr3t-value");
        events.Count.ShouldBe(2);
        JsonSerializer.Serialize(events).ShouldNotContain("s3cr3t");
        JsonSerializer.Serialize(events).ShouldNotContain("eu-central");
        events.ShouldAllBe(e => e.FolderId == team);
    }

    [Fact]
    public async Task Deleting_a_folder_or_an_automation_deletes_its_values()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var hello = (await AutomationsAsync(a => a.CreateAsync(ada, team, "hello", Hello, null, Cancel))).ShouldSucceed();
        await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Automation(hello.Id), "KEY", "key-value", Cancel));
        await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Folder(team), "TOKEN", "token-value", Cancel));
        await VariablesAsync(v => v.SetAsync(ada, ConfigurationScope.Folder(team), "REGION", "eu", Cancel));

        (await AutomationsAsync(a => a.DeleteAsync(ada, hello.Id, null, Cancel))).ShouldSucceed();
        (await ReadAsync(db => db.Secrets.CountAsync(Cancel))).ShouldBe(1);
        (await FoldersAsync(f => f.DeleteAsync(ada, team, null, Cancel))).ShouldSucceed();

        (await ReadAsync(db => db.Secrets.CountAsync(Cancel))).ShouldBe(0);
        (await ReadAsync(db => db.Variables.CountAsync(Cancel))).ShouldBe(0);
        (await AuditAsync(AuditActions.SecretDeleted)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_values_is_audited_by_name()
    {
        var ada = await ActorAsync("ada");
        var team = await FolderAsync(ada, "Team");
        await VariablesAsync(v => v.SetAsync(ada, ConfigurationScope.Folder(team), "Region", "eu", Cancel));
        await SecretsAsync(s => s.SetAsync(ada, ConfigurationScope.Folder(team), "Token", "token-value", Cancel));

        (await VariablesAsync(v => v.DeleteAsync(ada, ConfigurationScope.Folder(team), "REGION", Cancel))).ShouldSucceed();
        (await SecretsAsync(s => s.DeleteAsync(ada, ConfigurationScope.Folder(team), "token", Cancel))).ShouldSucceed();

        (await AuditAsync(AuditActions.VariableDeleted)).ShouldHaveSingleItem().TargetId.ShouldBe($"{ValueScopes.Folder(team)}/Region");
        (await AuditAsync(AuditActions.SecretDeleted)).ShouldHaveSingleItem().TargetId.ShouldBe($"{ValueScopes.Folder(team)}/Token");
    }

    private async Task<Guid> FolderAsync(Actor owner, string name, Guid? parentId = null) =>
        (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder(name, parentId), Cancel))).ShouldSucceed().Id;

    private async Task GrantAsync(Actor owner, Guid folderId, Actor user, FolderRole role) =>
        (await FolderGrantsAsync(g => g.SetAsync(owner, folderId, user.UserId, null, role, Cancel))).ShouldSucceed();
}

public sealed class VariableAndSecretServiceTestsOnSqlite() : VariableAndSecretServiceTests(DatabaseProvider.Sqlite);

public sealed class VariableAndSecretServiceTestsOnPostgres() : VariableAndSecretServiceTests(DatabaseProvider.Postgres);
