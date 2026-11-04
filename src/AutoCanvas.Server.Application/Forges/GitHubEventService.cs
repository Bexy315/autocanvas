using System.Text.Json;
using AutoCanvas.Forge;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Forges;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Triggers;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// The GitHub App's webhook (ADR-0022): one address for every event of every installation, signed
/// with the app's webhook secret and taken once per delivery. A push is stored for each connected
/// repository it is of and handled in the background like a push to a repository's own webhook;
/// an uninstalled or suspended installation is shown on its repositories.
/// </summary>
public sealed class GitHubEventService(AutoCanvasDbContext database, GitHubAccess github, RepositoryPushSignal signal, TimeProvider time)
{
    /// <summary>How long delivery ids are kept to recognize a delivery GitHub sends again.</summary>
    public static readonly TimeSpan DeliveryRetention = TimeSpan.FromDays(3);

    /// <summary>Takes a call of the app's webhook.</summary>
    public async Task<UseCaseResult<ReceivedHookView>> ReceiveAsync(RepositoryHookCall call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (await github.AppAsync(cancellationToken).ConfigureAwait(false) is not { } app)
        {
            return UseCase.Failure<ReceivedHookView>(UseCaseError.NotFound("No GitHub App is set up on this instance."));
        }

        string secret;
        try
        {
            secret = github.Decrypt(app.WebhookSecretCiphertext, GitHubAccess.WebhookSecretName);
        }
        catch (ForgeException exception)
        {
            return UseCase.Failure<ReceivedHookView>(new UseCaseError(UseCaseErrorKind.Unauthorized, exception.Message));
        }

        if (!WebhookSignatures.Matches(secret, call.Body.Span, WebhookSignatures.Prefixed(call.Headers.GetValueOrDefault("X-Hub-Signature-256"))))
        {
            return UseCase.Failure<ReceivedHookView>(new UseCaseError(UseCaseErrorKind.Unauthorized, "The call must be signed with the app's webhook secret: X-Hub-Signature-256: sha256=<HMAC-SHA256 of the body, in hex>."));
        }

        if (call.Headers.GetValueOrDefault("X-GitHub-Delivery") is { Length: > 0 } delivery && !await TakeAsync(delivery, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Success(new ReceivedHookView(Queued: false, "This delivery was taken already."));
        }

        var eventName = call.Headers.GetValueOrDefault("X-GitHub-Event") ?? "(none)";
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(call.Body);
        }
        catch (JsonException)
        {
            return UseCase.Success(new ReceivedHookView(Queued: false, "The body is not JSON, so nothing happens."));
        }

        using (document)
        {
            return eventName switch
            {
                "ping" => UseCase.Success(new ReceivedHookView(Queued: false, "pong")),
                "push" => UseCase.Success(await PushAsync(document.RootElement, cancellationToken).ConfigureAwait(false)),
                "installation" => UseCase.Success(await InstallationAsync(document.RootElement, cancellationToken).ConfigureAwait(false)),
                _ => UseCase.Success(new ReceivedHookView(Queued: false, $"The event {eventName} starts nothing.")),
            };
        }
    }

    /// <summary>Stores the push for every connected repository it is of.</summary>
    private async Task<ReceivedHookView> PushAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (PushPayload.Read(root) is not { } push
            || !root.TryGetProperty("repository", out var repository) || !repository.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var externalId))
        {
            return new ReceivedHookView(Queued: false, "The push could not be read, so nothing happens.");
        }

        var connected = await database.Repositories.AsNoTracking()
            .Where(r => r.Kind == RepositoryKind.GitHub && r.ExternalId == externalId)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var id in connected)
        {
            database.RepositoryPushes.Add(new RepositoryPush
            {
                Id = Guid.CreateVersion7(time.GetUtcNow()),
                RepositoryId = id,
                Ref = push.Ref,
                Before = push.Before,
                After = push.After,
                ChangedFiles = push.ChangedFiles is { } files ? JsonSerializer.Serialize(files) : null,
                Pusher = Cut(push.Pusher, RepositoryPush.MaxPusherLength),
                Message = Cut(push.Message, RepositoryPush.MaxMessageLength),
                ReceivedAt = time.GetUtcNow(),
            });
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (connected.Count == 0)
        {
            return new ReceivedHookView(Queued: false, "The repository is not connected in any folder.");
        }

        signal.Notify();
        return new ReceivedHookView(Queued: true, $"The push to {push.Ref} starts its runs shortly, in {connected.Count} folder(s).");
    }

    /// <summary>An installation that went away or was suspended: its repositories say so.</summary>
    private async Task<ReceivedHookView> InstallationAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var action = root.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        if (action is not ("deleted" or "suspend")
            || !root.TryGetProperty("installation", out var installation) || !installation.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
        {
            return new ReceivedHookView(Queued: false, $"The installation was {action ?? "changed"}; nothing to do.");
        }

        var error = string.Equals(action, "deleted", StringComparison.Ordinal)
            ? "The GitHub App was uninstalled from the account of this repository; install it again, or disconnect the repository."
            : "The installation of the GitHub App is suspended on the account of this repository.";
        var changed = await database.Repositories
            .Where(r => r.Kind == RepositoryKind.GitHub && r.InstallationId == id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.LastError, error), cancellationToken).ConfigureAwait(false);
        return new ReceivedHookView(Queued: false, $"The installation was {action}; {changed} repository(ies) marked.");
    }

    /// <summary>Records a delivery; <see langword="false"/> when it was taken already. Old deliveries go.</summary>
    private async Task<bool> TakeAsync(string delivery, CancellationToken cancellationToken)
    {
        var id = delivery.Length > GitHubDelivery.MaxIdLength ? delivery[..GitHubDelivery.MaxIdLength] : delivery;
        var before = time.GetUtcNow() - DeliveryRetention;
        await database.GitHubDeliveries.Where(d => d.ReceivedAt < before).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (await database.GitHubDeliveries.AnyAsync(d => d.DeliveryId == id, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var row = new GitHubDelivery { DeliveryId = id, ReceivedAt = time.GetUtcNow() };
        database.GitHubDeliveries.Add(row);
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            database.Entry(row).State = EntityState.Detached;
            return false;
        }
    }

    private static string? Cut(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;
}
