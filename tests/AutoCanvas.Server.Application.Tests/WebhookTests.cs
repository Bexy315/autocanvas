using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Triggers;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Tests;

public abstract class WebhookTests(DatabaseProvider provider) : IdentityTest(provider)
{
    private const string Hooked = """
        version: 1
        triggers:
          webhook: true
        jobs:
          build:
            runs_on: linux
            steps:
              - run: echo ${{ trigger.body.ref }}

        """;

    private Actor owner = null!;
    private Guid folder;

    [Fact]
    public async Task A_call_of_the_address_starts_a_run_with_the_request()
    {
        var automation = await SetUpAsync(Hooked);
        var created = (await HooksAsync(w => w.CreateAsync(owner, automation, requireSignature: false, Cancel))).ShouldSucceed();

        var started = (await CallAsync(created.Token, Call("""{ "ref": "main", "count": 2 }""", isJson: true))).ShouldSucceed();

        created.Path.ShouldBe($"/api/v1/hooks/{created.Token}");
        created.Token.ShouldStartWith(WebhookService.TokenPrefix);
        created.SigningSecret.ShouldBeNull();
        created.Enabled.ShouldBeTrue();
        started.Number.ShouldBe(1);
        var run = await ReadAsync(db => db.Runs.SingleAsync(r => r.Id == started.RunId, Cancel));
        run.Trigger.ShouldBe(RunTriggerKind.Webhook);
        run.TriggeredByName.ShouldBe("webhook");
        var data = JsonDocument.Parse(run.TriggerData!).RootElement;
        data.GetProperty("body").GetProperty("ref").GetString().ShouldBe("main");
        data.GetProperty("headers").GetProperty("X-Delivery").GetString().ShouldBe("42");
        data.GetProperty("query").GetProperty("source").GetString().ShouldBe("ci");
        var view = (await HooksAsync(w => w.GetAsync(owner, automation, Cancel))).ShouldSucceed();
        view.Hint.ShouldBe(created.Hint);
        view.RequiresSignature.ShouldBeFalse();
        view.CreatedByName.ShouldStartWith("owner");
        (await AuditAsync(AuditActions.WebhookCreated)).ShouldHaveSingleItem().Details!.ShouldContain(created.Hint[^4..]);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_read_as_text()
    {
        var automation = await SetUpAsync(Hooked);
        var created = (await HooksAsync(w => w.CreateAsync(owner, automation, false, Cancel))).ShouldSucceed();

        var json = (await CallAsync(created.Token, Call("not json {", isJson: true))).ShouldSucceed();
        var text = (await CallAsync(created.Token, Call("plain words", isJson: false))).ShouldSucceed();

        (await BodyAsync(json.RunId)).GetString().ShouldBe("not json {");
        (await BodyAsync(text.RunId)).GetString().ShouldBe("plain words");
    }

    [Fact]
    public async Task A_signed_address_takes_only_calls_signed_with_its_secret()
    {
        var automation = await SetUpAsync(Hooked);
        var created = (await HooksAsync(w => w.CreateAsync(owner, automation, requireSignature: true, Cancel))).ShouldSucceed();
        const string Body = """{ "ref": "main" }""";
        var signature = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(created.SigningSecret!), Encoding.UTF8.GetBytes(Body)));

        (await CallAsync(created.Token, Call(Body, isJson: true, signature))).ShouldSucceed();
        (await CallAsync(created.Token, Call(Body, isJson: true, signature.ToUpperInvariant().Replace("SHA256=", "sha256=", StringComparison.Ordinal)))).ShouldSucceed();
        (await CallAsync(created.Token, Call(Body + " ", isJson: true, signature))).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await CallAsync(created.Token, Call(Body, isJson: true))).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await CallAsync(created.Token, Call(Body, isJson: true, "sha256=zz"))).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await CallAsync(created.Token, Call(Body, isJson: true, "md5=abc"))).ShouldFail(UseCaseErrorKind.Unauthorized);
        (await HooksAsync(w => w.GetAsync(owner, automation, Cancel))).ShouldSucceed().RequiresSignature.ShouldBeTrue();
        var stored = await ReadAsync(db => db.AutomationWebhooks.SingleAsync(Cancel));
        stored.SigningSecret.ShouldNotBeNull().ShouldNotContain(created.SigningSecret!);
    }

    [Fact]
    public async Task Only_a_current_address_of_an_automation_that_allows_webhooks_starts_runs()
    {
        var automation = await SetUpAsync(Hooked);
        var first = (await HooksAsync(w => w.CreateAsync(owner, automation, false, Cancel))).ShouldSucceed();
        var second = (await HooksAsync(w => w.CreateAsync(owner, automation, false, Cancel))).ShouldSucceed();

        (await CallAsync(first.Token, Call("{}", true))).ShouldFail(UseCaseErrorKind.NotFound);
        (await CallAsync("ach_unknown", Call("{}", true))).ShouldFail(UseCaseErrorKind.NotFound);
        (await AuditAsync(AuditActions.WebhookCreated)).Count.ShouldBe(2);

        (await AutomationsAsync(a => a.SaveAsync(owner, automation, Hooked.Replace("webhook: true", "manual: true", StringComparison.Ordinal), null, null, Cancel))).ShouldSucceed();
        (await CallAsync(second.Token, Call("{}", true))).ShouldFail(UseCaseErrorKind.NotFound);
        (await HooksAsync(w => w.GetAsync(owner, automation, Cancel))).ShouldSucceed().Enabled.ShouldBeFalse();

        (await HooksAsync(w => w.DeleteAsync(owner, automation, Cancel))).ShouldSucceed();
        (await HooksAsync(w => w.DeleteAsync(owner, automation, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await HooksAsync(w => w.GetAsync(owner, automation, Cancel))).ShouldSucceed().Hint.ShouldBeNull();
        (await AuditAsync(AuditActions.WebhookDeleted)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_single_automation_still_running_refuses_the_call_and_strangers_see_nothing()
    {
        var automation = await SetUpAsync(Hooked + "concurrency: single\n");
        var created = (await HooksAsync(w => w.CreateAsync(owner, automation, false, Cancel))).ShouldSucceed();
        (await CallAsync(created.Token, Call("{}", true))).ShouldSucceed();
        var stranger = await ActorAsync("stranger" + Guid.NewGuid().ToString("N")[..6]);

        (await CallAsync(created.Token, Call("{}", true))).ShouldFail(UseCaseErrorKind.Conflict);
        (await HooksAsync(w => w.GetAsync(stranger, automation, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await HooksAsync(w => w.CreateAsync(stranger, automation, false, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
        (await HooksAsync(w => w.DeleteAsync(stranger, automation, Cancel))).ShouldFail(UseCaseErrorKind.NotFound);
    }

    private static WebhookCall Call(string body, bool isJson, string? signature = null) => new(
        Encoding.UTF8.GetBytes(body),
        isJson,
        signature,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Delivery"] = "42" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["source"] = "ci" });

    private async Task<Guid> SetUpAsync(string yaml)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        owner = await ActorAsync("owner" + unique);
        folder = (await FoldersAsync(f => f.CreateAsync(owner, new NewFolder("hooks" + unique), Cancel))).ShouldSucceed().Id;
        return (await AutomationsAsync(a => a.CreateAsync(owner, folder, "hooked", yaml, null, Cancel))).ShouldSucceed().Id;
    }

    private async Task<JsonElement> BodyAsync(Guid run) =>
        JsonDocument.Parse((await ReadAsync(db => db.Runs.SingleAsync(r => r.Id == run, Cancel))).TriggerData!).RootElement.GetProperty("body").Clone();

    private Task<UseCaseResult<WebhookRunView>> CallAsync(string token, WebhookCall call) => HooksAsync(w => w.TriggerAsync(token, call, Cancel));

    private async Task<T> HooksAsync<T>(Func<WebhookService, Task<T>> action)
    {
        var context = Database.CreateContext();
        await using (context)
        {
            return await action(new WebhookService(context, new SecretCipher(Key), Signal, new ConfigurationService(context, new SecretCipher(Key)), new AuditLog(context, new SystemAuditContext(), Clock), Clock));
        }
    }
}

public sealed class WebhookTestsOnSqlite() : WebhookTests(DatabaseProvider.Sqlite);

public sealed class WebhookTestsOnPostgres() : WebhookTests(DatabaseProvider.Postgres);
