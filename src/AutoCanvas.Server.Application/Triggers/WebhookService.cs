using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoCanvas.Core;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Automations;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Queue;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Persistence.Triggers;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Triggers;

/// <summary>
/// Webhook addresses of automations and the calls that start runs through them. An address is a
/// random token in the path, stored only as its hash; a signed address also has a secret, which the
/// caller signs the body with (HMAC-SHA256) and the server keeps encrypted with the master key.
/// Operators of the folder create and delete addresses, Viewers see whether there is one.
/// </summary>
public sealed class WebhookService
{
    /// <summary>The prefix of webhook tokens.</summary>
    public const string TokenPrefix = "ach_";

    /// <summary>The largest body a call may have.</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    /// <summary>The header that carries the signature, <c>sha256=</c> and the hex HMAC of the body.</summary>
    public const string SignatureHeader = "X-AutoCanvas-Signature";

    /// <summary>The scope of signing secrets in the cipher's associated data.</summary>
    internal const string CipherScope = "webhook";

    private readonly AutoCanvasDbContext database;
    private readonly SecretCipher cipher;
    private readonly IAuditLog audit;
    private readonly TimeProvider time;
    private readonly FolderAccess access;
    private readonly RunStarter starter;

    /// <summary>Creates the service for one unit of work.</summary>
    public WebhookService(AutoCanvasDbContext database, SecretCipher cipher, IJobSignal signal, ConfigurationService configuration, IAuditLog audit, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
        this.cipher = cipher;
        this.audit = audit;
        this.time = time;
        access = new FolderAccess(database);
        starter = new RunStarter(database, signal, audit, new RunEngine(database, configuration, time), time);
    }

    /// <summary>The automation's webhook address, without its token (Viewer).</summary>
    public async Task<UseCaseResult<WebhookView>> GetAsync(Actor actor, Guid automationId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(actor, automationId, FolderRole.Viewer, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<WebhookView>(denied);
        }

        var webhook = await database.AutomationWebhooks.AsNoTracking().FirstOrDefaultAsync(w => w.AutomationId == automationId, cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new WebhookView(await EnabledAsync(found.Value!, cancellationToken).ConfigureAwait(false), webhook?.Hint, webhook?.SigningSecret is not null, webhook?.CreatedAt, webhook?.CreatedByName));
    }

    /// <summary>Creates a new webhook address for the automation, replacing the old one (Operator).</summary>
    public async Task<UseCaseResult<CreatedWebhookView>> CreateAsync(Actor actor, Guid automationId, bool requireSignature, CancellationToken cancellationToken)
    {
        var found = await FindAsync(actor, automationId, FolderRole.Operator, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<CreatedWebhookView>(denied);
        }

        var automation = found.Value!;
        var existing = await database.AutomationWebhooks.FirstOrDefaultAsync(w => w.AutomationId == automationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            database.AutomationWebhooks.Remove(existing);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var token = SecretTokens.Create(TokenPrefix);
        var secret = requireSignature ? SecretTokens.Create() : null;
        var hint = $"{TokenPrefix}…{token[^4..]}";
        database.AutomationWebhooks.Add(new AutomationWebhook
        {
            AutomationId = automationId,
            TokenHash = SecretTokens.Hash(token),
            Hint = hint,
            SigningSecret = secret is null ? null : cipher.Encrypt(CipherScope, automationId.ToString(), secret),
            CreatedAt = time.GetUtcNow(),
            CreatedByName = await database.Users.Where(u => u.Id == actor.UserId).Select(u => u.UserName).FirstAsync(cancellationToken).ConfigureAwait(false),
        });
        audit.Record(Entry(AuditActions.WebhookCreated, automation, ("hint", hint), ("signed", requireSignature), ("replaced", existing is not null)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new CreatedWebhookView($"/api/v1/hooks/{token}", token, secret, hint, await EnabledAsync(automation, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>Deletes the automation's webhook address; calls of it fail from then on (Operator).</summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(Actor actor, Guid automationId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(actor, automationId, FolderRole.Operator, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<Done>(denied);
        }

        var webhook = await database.AutomationWebhooks.FirstOrDefaultAsync(w => w.AutomationId == automationId, cancellationToken).ConfigureAwait(false);
        if (webhook is null)
        {
            return UseCase.Failure<Done>(UseCaseError.NotFound("The automation has no webhook address."));
        }

        database.AutomationWebhooks.Remove(webhook);
        audit.Record(Entry(AuditActions.WebhookDeleted, found.Value!, ("hint", webhook.Hint)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }

    /// <summary>
    /// Starts a run for a call of a webhook address. An unknown token, and one of an automation that
    /// does not allow webhook calls, are both not found, so that a caller learns nothing.
    /// </summary>
    public async Task<UseCaseResult<WebhookRunView>> TriggerAsync(string token, WebhookCall call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(call);
        var hash = SecretTokens.Hash(token);
        var webhook = await database.AutomationWebhooks.AsNoTracking().FirstOrDefaultAsync(w => w.TokenHash == hash, cancellationToken).ConfigureAwait(false);
        var automation = webhook is null ? null : await database.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == webhook.AutomationId, cancellationToken).ConfigureAwait(false);
        if (webhook is null || automation is null)
        {
            return UseCase.Failure<WebhookRunView>(NoWebhook);
        }

        var yaml = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == automation.Id && v.Number == automation.CurrentVersion)
            .Select(v => v.Yaml)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        var definition = AutomationLoader.Load(yaml).Definition!;
        if (!definition.Triggers.Webhook)
        {
            return UseCase.Failure<WebhookRunView>(NoWebhook);
        }

        if (webhook.SigningSecret is { } stored && !SignatureMatches(cipher.Decrypt(CipherScope, automation.Id.ToString(), stored), call))
        {
            return UseCase.Failure<WebhookRunView>(new UseCaseError(UseCaseErrorKind.Unauthorized, $"The call must be signed: {SignatureHeader}: sha256=<HMAC-SHA256 of the body with the webhook's secret, in hex>."));
        }

        var started = await starter.StartAsync(automation, yaml, definition, new RunStart(RunTriggerKind.Webhook, null, "webhook", Data(call)), cancellationToken).ConfigureAwait(false);
        return started.Error is { } refused
            ? UseCase.Failure<WebhookRunView>(refused)
            : UseCase.Success(new WebhookRunView(started.Value!.Id, started.Value.Number));
    }

    private static UseCaseError NoWebhook => UseCaseError.NotFound("There is no webhook at this address.");

    private static bool SignatureMatches(string? secret, WebhookCall call) =>
        WebhookSignatures.Matches(secret, call.Body.Span, WebhookSignatures.Prefixed(call.Signature));

    /// <summary>What the run reads as <c>trigger.*</c>: the body (an object when it is JSON), the headers and the query.</summary>
    private static string Data(WebhookCall call)
    {
        var text = Encoding.UTF8.GetString(call.Body.Span);
        JsonNode? body = text;
        if (call.IsJson && text.Length > 0)
        {
            try
            {
                body = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                // Not JSON after all: the run reads it as text.
            }
        }

        var data = new JsonObject
        {
            ["body"] = body,
            ["headers"] = new JsonObject(call.Headers.Select(h => KeyValuePair.Create(h.Key, (JsonNode?)h.Value))),
            ["query"] = new JsonObject(call.Query.Select(q => KeyValuePair.Create(q.Key, (JsonNode?)q.Value))),
        };
        return data.ToJsonString();
    }

    private static AuditEntry Entry(string action, Automation automation, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "automation",
        TargetId = automation.Id.ToString(),
        FolderId = automation.FolderId,
        Details = AuditEntry.DetailsOf(details),
    };

    private async Task<bool> EnabledAsync(Automation automation, CancellationToken cancellationToken)
    {
        var yaml = await database.AutomationVersions.AsNoTracking()
            .Where(v => v.AutomationId == automation.Id && v.Number == automation.CurrentVersion)
            .Select(v => v.Yaml)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        return AutomationLoader.Load(yaml).Definition?.Triggers.Webhook == true;
    }

    private async Task<UseCaseResult<Automation>> FindAsync(Actor actor, Guid automationId, FolderRole needed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var automation = await database.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == automationId, cancellationToken).ConfigureAwait(false);
        if (automation is null)
        {
            return UseCase.Failure<Automation>(UseCaseError.NotFound("The automation does not exist, or you may not see it."));
        }

        var folder = await access.RequireAsync(actor, automation.FolderId, needed, track: false, cancellationToken).ConfigureAwait(false);
        return folder.Error is { } error
            ? UseCase.Failure<Automation>(error.Kind == UseCaseErrorKind.NotFound ? UseCaseError.NotFound("The automation does not exist, or you may not see it.") : error)
            : UseCase.Success(automation);
    }
}
