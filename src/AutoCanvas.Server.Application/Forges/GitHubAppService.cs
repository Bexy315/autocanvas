using System.Globalization;
using System.Text.Json;
using AutoCanvas.Forge;
using AutoCanvas.Forge.GitHub;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Forges;
using AutoCanvas.Persistence.Repositories;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Instance;
using AutoCanvas.Server.Application.Variables;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>
/// The instance's GitHub App (ADR-0022), set up by admins (the endpoints check that): created with
/// GitHub's manifest flow, or entered by hand. Its private key and secrets are encrypted with the
/// master key and never come back.
/// </summary>
public sealed class GitHubAppService(
    AutoCanvasDbContext database,
    GitHubAccess github,
    InstanceSettingsService instance,
    SecretCipher cipher,
    IAuditLog audit,
    TimeProvider time)
{
    /// <summary>The path GitHub sends the app's events to.</summary>
    public const string EventsPath = "/api/v1/forges/github/events";

    /// <summary>The page of the web UI GitHub sends the browser back to after creating the app.</summary>
    public const string CreatedPath = "/admin/forges/github/created";

    /// <summary>How long the state of a manifest is valid.</summary>
    public static readonly TimeSpan StateLifetime = TimeSpan.FromHours(1);

    /// <summary>GitHub itself.</summary>
    public static readonly Uri GitHubWeb = new("https://github.com");

    private const string StateScope = "github-manifest";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The app with its installations, or none.</summary>
    public async Task<GitHubForgeView> GetAsync(CancellationToken cancellationToken)
    {
        if (await github.AppAsync(cancellationToken).ConfigureAwait(false) is not { } app)
        {
            return new GitHubForgeView(null);
        }

        IReadOnlyList<GitHubInstallation> installations = [];
        string? error = null;
        try
        {
            installations = await github.Client.ListInstallationsAsync(app.ApiAddress, github.JwtOf(app), cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException exception)
        {
            error = exception.Message;
        }

        var publicAddress = (await instance.GetAsync(cancellationToken).ConfigureAwait(false)).PublicAddress.Value;
        return new GitHubForgeView(new GitHubAppView(
            app.AppId,
            app.Slug,
            app.Name,
            app.OwnerLogin,
            app.HtmlUrl,
            new Uri(app.HtmlUrl.ToString().TrimEnd('/') + "/installations/new"),
            app.WebAddress,
            app.ApiAddress,
            publicAddress is null ? null : new Uri(publicAddress, EventsPath),
            installations,
            error,
            app.CreatedAt,
            app.CreatedByName));
    }

    /// <summary>
    /// The manifest to create the app from, and where the browser posts it. Needs the instance's
    /// public address, which GitHub sends events and the browser back to.
    /// </summary>
    public async Task<UseCaseResult<GitHubManifestView>> ManifestAsync(NewGitHubManifest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await github.AppAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return UseCase.Failure<GitHubManifestView>(UseCaseError.Conflict("The instance has a GitHub App already; remove it first to create another."));
        }

        if ((await instance.GetAsync(cancellationToken).ConfigureAwait(false)).PublicAddress.Value is not { } publicAddress)
        {
            return UseCase.Failure<GitHubManifestView>(UseCaseError.Conflict("Set the instance's public address first (Administration, Instance): GitHub sends the browser back and the events there."));
        }

        if (WebOf(request.WebAddress) is not { } web)
        {
            return UseCase.Failure<GitHubManifestView>(UseCaseError.Invalid("webAddress", "Give GitHub's web address over https, such as https://github.example.org for a GitHub Enterprise Server."));
        }

        var organization = request.Organization?.Trim();
        if (organization is { Length: > 0 } && !organization.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return UseCase.Failure<GitHubManifestView>(UseCaseError.Invalid("organization", "An organization's name has letters, digits and dashes."));
        }

        var state = cipher.Encrypt(StateScope, "state", JsonSerializer.Serialize(new ManifestState(time.GetUtcNow().Add(StateLifetime), web, organization is { Length: > 0 } ? organization : null), Json));
        return UseCase.Success(View(state, web, organization, publicAddress));
    }

    /// <summary>
    /// The manifest again, for the state of a manifest made in the last hour: what the page that
    /// takes the browser to GitHub posts. The state is the proof that an admin asked for it.
    /// </summary>
    public async Task<UseCaseResult<GitHubManifestView>> LaunchAsync(string state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (ReadState(state) is not { } read || read.ExpiresAt < time.GetUtcNow())
        {
            return UseCase.Failure<GitHubManifestView>(UseCaseError.Invalid("state", "The manifest is older than an hour, or not one of this instance; create the app again."));
        }

        if ((await instance.GetAsync(cancellationToken).ConfigureAwait(false)).PublicAddress.Value is not { } publicAddress)
        {
            return UseCase.Failure<GitHubManifestView>(UseCaseError.Conflict("Set the instance's public address first (Administration, Instance)."));
        }

        return UseCase.Success(View(state, read.Web, read.Organization, publicAddress));
    }

    private static GitHubManifestView View(string state, Uri web, string? organization, Uri publicAddress)
    {
        var path = organization is { Length: > 0 } ? $"organizations/{organization}/settings/apps/new" : "settings/apps/new";
        var action = new Uri(new Uri(web.ToString().TrimEnd('/') + "/"), $"{path}?state={Uri.EscapeDataString(state)}");
        return new GitHubManifestView(action, Manifest(publicAddress), state);
    }

    /// <summary>Turns the code GitHub gave the browser into the app, and stores it.</summary>
    public async Task<UseCaseResult<GitHubForgeView>> CompleteAsync(Actor actor, GitHubAppCompletion completion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(completion);
        var state = ReadState(completion.State);
        if (state is null || state.ExpiresAt < time.GetUtcNow())
        {
            return UseCase.Failure<GitHubForgeView>(UseCaseError.Invalid("state", "The answer from GitHub does not belong to a manifest of the last hour; create the app again."));
        }

        if (await github.AppAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return UseCase.Failure<GitHubForgeView>(UseCaseError.Conflict("The instance has a GitHub App already."));
        }

        GitHubAppCredentials created;
        try
        {
            created = await github.Client.ConvertManifestAsync(ApiOf(state.Web), completion.Code, cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException exception)
        {
            return UseCase.Failure<GitHubForgeView>(UseCaseError.Invalid("code", $"GitHub did not hand out the app: {exception.Message}"));
        }

        var info = new GitHubAppInfo(created.Id, created.Slug, created.Name, created.ClientId, created.OwnerLogin, created.HtmlUrl);
        await StoreAsync(actor, info, state.Web, ApiOf(state.Web), created.PrivateKey, created.WebhookSecret, created.ClientSecret, cancellationToken).ConfigureAwait(false);
        audit.Record(Entry(AuditActions.GitHubAppCreated, info));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(await GetAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Enters an existing app, checked with GitHub; also how a new private key or webhook secret is
    /// entered. Another app replaces this one only while no GitHub repository is connected.
    /// </summary>
    public async Task<UseCaseResult<GitHubForgeView>> ConfigureAsync(Actor actor, GitHubAppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(settings);
        if (WebOf(settings.WebAddress) is not { } web)
        {
            return UseCase.Failure<GitHubForgeView>(UseCaseError.Invalid("webAddress", "Give GitHub's web address over https, such as https://github.example.org."));
        }

        var api = string.IsNullOrWhiteSpace(settings.ApiAddress) ? ApiOf(web)
            : Uri.TryCreate(settings.ApiAddress.Trim(), UriKind.Absolute, out var given) && given.Scheme is "https" or "http" ? given : null;
        if (api is null)
        {
            return UseCase.Failure<GitHubForgeView>(UseCaseError.Invalid("apiAddress", "Give GitHub's API address, such as https://github.example.org/api/v3."));
        }

        if (string.IsNullOrWhiteSpace(settings.WebhookSecret))
        {
            return UseCase.Failure<GitHubForgeView>(UseCaseError.Invalid("webhookSecret", "Give the webhook secret set in the app's settings on GitHub."));
        }

        var existing = await github.AppAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.AppId != settings.AppId && await AnyRepositoryAsync(cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<GitHubForgeView>(UseCaseError.Conflict("GitHub repositories are connected through the current app; disconnect them before entering another app."));
        }

        GitHubAppInfo info;
        try
        {
            var jwt = GitHubJwt.Create(settings.AppId.ToString(CultureInfo.InvariantCulture), settings.PrivateKey, time.GetUtcNow());
            info = await github.Client.GetAppAsync(api, jwt, cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException exception)
        {
            return UseCase.Failure<GitHubForgeView>(UseCaseError.Invalid("privateKey", $"GitHub does not take the app id and key: {exception.Message}"));
        }

        await StoreAsync(actor, info, web, api, settings.PrivateKey.Trim(), settings.WebhookSecret.Trim(), clientSecret: null, cancellationToken).ConfigureAwait(false);
        audit.Record(Entry(AuditActions.GitHubAppConfigured, info));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(await GetAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Removes the app from the instance (it stays on GitHub); refused while GitHub repositories are connected.</summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(CancellationToken cancellationToken)
    {
        var app = await database.GitHubApps.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (app is null)
        {
            return UseCase.Failure<Done>(UseCaseError.NotFound("No GitHub App is set up."));
        }

        if (await AnyRepositoryAsync(cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<Done>(UseCaseError.Conflict("GitHub repositories are connected through the app; disconnect them first."));
        }

        database.GitHubApps.Remove(app);
        audit.Record(Entry(AuditActions.GitHubAppDeleted, new GitHubAppInfo(app.AppId, app.Slug, app.Name, app.ClientId, app.OwnerLogin, app.HtmlUrl)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        github.Tokens.Clear();
        return Done.Result;
    }

    /// <summary>The API address of a GitHub web address: <c>api.github.com</c> for GitHub, <c>/api/v3</c> of an Enterprise Server.</summary>
    internal static Uri ApiOf(Uri web) =>
        string.Equals(web.Host, GitHubWeb.Host, StringComparison.OrdinalIgnoreCase) ? new Uri("https://api.github.com") : new Uri(web.ToString().TrimEnd('/') + "/api/v3");

    private static Uri? WebOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return GitHubWeb;
        }

        return Uri.TryCreate(address.Trim(), UriKind.Absolute, out var web) && web.Scheme is "https" or "http" && web.UserInfo.Length == 0 ? web : null;
    }

    private static string Manifest(Uri publicAddress)
    {
        var manifest = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["name"] = $"AutoCanvas ({publicAddress.Host})",
            ["url"] = publicAddress.ToString(),
            ["hook_attributes"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["url"] = new Uri(publicAddress, EventsPath).ToString(), ["active"] = true },
            ["redirect_url"] = new Uri(publicAddress, CreatedPath).ToString(),
            ["public"] = false,
            ["default_permissions"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contents"] = "write",
                ["pull_requests"] = "write",
                ["statuses"] = "write",
                ["checks"] = "write",
                ["metadata"] = "read",
            },
            ["default_events"] = new[] { "push", "pull_request" },
        };
        return JsonSerializer.Serialize(manifest);
    }

    private ManifestState? ReadState(string state)
    {
        if (cipher.Decrypt(StateScope, "state", state) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ManifestState>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task<bool> AnyRepositoryAsync(CancellationToken cancellationToken) =>
        database.Repositories.AnyAsync(r => r.Kind == RepositoryKind.GitHub, cancellationToken);

    private async Task StoreAsync(Actor actor, GitHubAppInfo info, Uri web, Uri api, string privateKey, string webhookSecret, string? clientSecret, CancellationToken cancellationToken)
    {
        var app = await database.GitHubApps.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (app is null)
        {
            app = new GitHubApp
            {
                AppId = info.Id,
                Slug = info.Slug,
                Name = info.Name,
                OwnerLogin = info.OwnerLogin,
                HtmlUrl = info.HtmlUrl,
                WebAddress = web,
                ApiAddress = api,
                PrivateKeyCiphertext = string.Empty,
                WebhookSecretCiphertext = string.Empty,
                CreatedAt = time.GetUtcNow(),
                CreatedByName = string.Empty,
            };
            database.GitHubApps.Add(app);
        }

        (app.AppId, app.Slug, app.Name, app.ClientId, app.OwnerLogin, app.HtmlUrl) = (info.Id, info.Slug, info.Name, info.ClientId, info.OwnerLogin, info.HtmlUrl);
        (app.WebAddress, app.ApiAddress) = (web, api);
        app.PrivateKeyCiphertext = github.Encrypt(privateKey, GitHubAccess.PrivateKeyName);
        app.WebhookSecretCiphertext = github.Encrypt(webhookSecret, GitHubAccess.WebhookSecretName);
        app.ClientSecretCiphertext = clientSecret is null ? null : github.Encrypt(clientSecret, GitHubAccess.ClientSecretName);
        app.CreatedAt = time.GetUtcNow();
        app.CreatedByName = await database.Users.Where(u => u.Id == actor.UserId).Select(u => u.UserName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? "system";
        github.Tokens.Clear();
    }

    private static AuditEntry Entry(string action, GitHubAppInfo app) => new(action)
    {
        TargetType = "github-app",
        TargetId = app.Id.ToString(CultureInfo.InvariantCulture),
        Details = AuditEntry.DetailsOf(("slug", app.Slug), ("owner", app.OwnerLogin)),
    };

    /// <summary>What the state of a manifest carries: until when it is valid, which GitHub, and the organization.</summary>
    private sealed record ManifestState(DateTimeOffset ExpiresAt, Uri Web, string? Organization);
}
