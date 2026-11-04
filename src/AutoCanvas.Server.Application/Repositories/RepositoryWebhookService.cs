using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Folders;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Triggers;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// The webhook addresses of connected repositories (ADR-0021), which their forges call on pushes.
/// Editors of the folder create and delete one; the token and the secret are shown once. A call
/// must prove the secret, as GitHub and Gitea do (an HMAC-SHA256 signature of the body) or as GitLab
/// does (the secret in a header). A push is stored and handled in the background, so the forge gets
/// its answer at once; polls and webhooks seeing the same push start its runs once.
/// </summary>
public sealed class RepositoryWebhookService(
    AutoCanvasDbContext database,
    SecretCipher cipher,
    RepositoryPushSignal signal,
    IAuditLog audit,
    TimeProvider time)
{
    /// <summary>The prefix of repository webhook tokens.</summary>
    public const string TokenPrefix = "acg_";

    /// <summary>The scope of webhook secrets in the cipher's associated data.</summary>
    public const string SecretScope = "repository-webhook";

    /// <summary>The headers forges name their event in, in the order they are read.</summary>
    private static readonly string[] EventHeaders = ["X-GitHub-Event", "X-Gitea-Event", "X-Gogs-Event", "X-Gitlab-Event"];

    private readonly FolderAccess access = new(database);

    /// <summary>Creates a new webhook address for the repository, replacing the old one (Editor).</summary>
    public async Task<UseCaseResult<CreatedRepositoryWebhookView>> CreateAsync(Actor actor, Guid repositoryId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(actor, repositoryId, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<CreatedRepositoryWebhookView>(denied);
        }

        var existing = await database.RepositoryWebhooks.FirstOrDefaultAsync(w => w.RepositoryId == repositoryId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            database.RepositoryWebhooks.Remove(existing);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var token = SecretTokens.Create(TokenPrefix);
        var secret = SecretTokens.Create();
        var hint = $"{TokenPrefix}…{token[^4..]}";
        database.RepositoryWebhooks.Add(new RepositoryWebhook
        {
            RepositoryId = repositoryId,
            TokenHash = SecretTokens.Hash(token),
            Hint = hint,
            SecretCiphertext = cipher.Encrypt(SecretScope, repositoryId.ToString(), secret),
            CreatedAt = time.GetUtcNow(),
            CreatedByName = await database.Users.Where(u => u.Id == actor.UserId).Select(u => u.UserName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? "system",
        });
        audit.Record(Entry(AuditActions.RepositoryWebhookCreated, found.Value!, ("hint", hint), ("replaced", existing is not null)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new CreatedRepositoryWebhookView($"/api/v1/hooks/{token}", token, secret, hint));
    }

    /// <summary>Deletes the repository's webhook address; calls of it fail from then on (Editor).</summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(Actor actor, Guid repositoryId, CancellationToken cancellationToken)
    {
        var found = await FindAsync(actor, repositoryId, cancellationToken).ConfigureAwait(false);
        if (found.Error is { } denied)
        {
            return UseCase.Failure<Done>(denied);
        }

        var webhook = await database.RepositoryWebhooks.FirstOrDefaultAsync(w => w.RepositoryId == repositoryId, cancellationToken).ConfigureAwait(false);
        if (webhook is null)
        {
            return UseCase.Failure<Done>(UseCaseError.NotFound("The repository has no webhook address."));
        }

        database.RepositoryWebhooks.Remove(webhook);
        audit.Record(Entry(AuditActions.RepositoryWebhookDeleted, found.Value!, ("hint", webhook.Hint)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }

    /// <summary>
    /// Takes a forge's call of a webhook address: a push is stored for the background to handle;
    /// GitHub's <c>ping</c> and other events are answered without doing anything.
    /// </summary>
    public async Task<UseCaseResult<ReceivedHookView>> ReceiveAsync(string token, RepositoryHookCall call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(call);
        var hash = SecretTokens.Hash(token);
        var webhook = await database.RepositoryWebhooks.AsNoTracking().FirstOrDefaultAsync(w => w.TokenHash == hash, cancellationToken).ConfigureAwait(false);
        if (webhook is null)
        {
            return UseCase.Failure<ReceivedHookView>(UseCaseError.NotFound("There is no webhook at this address."));
        }

        if (!Authentic(cipher.Decrypt(SecretScope, webhook.RepositoryId.ToString(), webhook.SecretCiphertext), call))
        {
            return UseCase.Failure<ReceivedHookView>(new UseCaseError(
                UseCaseErrorKind.Unauthorized,
                $"The call must prove the webhook's secret: X-Hub-Signature-256 (or {WebhookService.SignatureHeader}): sha256=<HMAC-SHA256 of the body, in hex>, X-Gitea-Signature: <the same HMAC, in hex>, or X-Gitlab-Token: <the secret>."));
        }

        var eventName = EventHeaders.Select(h => call.Headers.GetValueOrDefault(h)).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (string.Equals(eventName, "ping", StringComparison.OrdinalIgnoreCase))
        {
            return UseCase.Success(new ReceivedHookView(Queued: false, "pong"));
        }

        var push = PushPayload.IsPush(eventName) ? Read(call.Body) : null;
        if (push is null)
        {
            return UseCase.Success(new ReceivedHookView(Queued: false, $"Only pushes start runs; this call ({eventName ?? "no event"}) is not one, so nothing happens."));
        }

        database.RepositoryPushes.Add(new RepositoryPush
        {
            Id = Guid.CreateVersion7(time.GetUtcNow()),
            RepositoryId = webhook.RepositoryId,
            Ref = push.Ref,
            Before = push.Before,
            After = push.After,
            ChangedFiles = push.ChangedFiles is { } files ? JsonSerializer.Serialize(files) : null,
            Pusher = Cut(push.Pusher, RepositoryPush.MaxPusherLength),
            Message = Cut(push.Message, RepositoryPush.MaxMessageLength),
            ReceivedAt = time.GetUtcNow(),
        });
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        signal.Notify();
        return UseCase.Success(new ReceivedHookView(Queued: true, $"The push to {push.Ref} starts its runs shortly."));
    }

    private static bool Authentic(string? secret, RepositoryHookCall call)
    {
        if (call.Headers.TryGetValue("X-Gitlab-Token", out var sent))
        {
            return WebhookSignatures.SameSecret(secret, sent);
        }

        var prefixed = WebhookSignatures.Prefixed(call.Headers.GetValueOrDefault("X-Hub-Signature-256") ?? call.Headers.GetValueOrDefault(WebhookService.SignatureHeader));
        return WebhookSignatures.Matches(secret, call.Body.Span, prefixed ?? call.Headers.GetValueOrDefault("X-Gitea-Signature")?.Trim());
    }

    private static PushEvent? Read(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return PushPayload.Read(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<UseCaseResult<Repository>> FindAsync(Actor actor, Guid repositoryId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var repository = await database.Repositories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken).ConfigureAwait(false);
        var notFound = UseCaseError.NotFound("The repository does not exist, or you may not see it.");
        if (repository is null)
        {
            return UseCase.Failure<Repository>(notFound);
        }

        var folder = await access.RequireAsync(actor, repository.FolderId, FolderRole.Editor, track: false, cancellationToken).ConfigureAwait(false);
        return folder.Error is { } error
            ? UseCase.Failure<Repository>(error.Kind == UseCaseErrorKind.NotFound ? notFound : error)
            : UseCase.Success(repository);
    }

    private static string? Cut(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;

    private static AuditEntry Entry(string action, Repository repository, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "repository",
        TargetId = repository.Id.ToString(),
        FolderId = repository.FolderId,
        Details = AuditEntry.DetailsOf([("name", repository.Name), .. details]),
    };
}
