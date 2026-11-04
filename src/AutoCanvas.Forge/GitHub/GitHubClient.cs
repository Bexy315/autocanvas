using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AutoCanvas.Forge.GitHub;

/// <summary>
/// The parts of GitHub's REST API a GitHub App needs (ADR-0022), at an API address of
/// <c>https://api.github.com</c> or a GitHub Enterprise Server's. Every failure is a
/// <see cref="ForgeException"/> with GitHub's message, never with a token in it.
/// </summary>
public sealed class GitHubClient(HttpClient http)
{
    /// <summary>The API version requested.</summary>
    public const string ApiVersion = "2022-11-28";

    /// <summary>The most repositories read from one installation (100 per page).</summary>
    public const int MaxRepositories = 1000;

    /// <summary>Turns the code GitHub gave the browser after a manifest into the new app and its secrets (no credential needed).</summary>
    public async Task<GitHubAppCredentials> ConvertManifestAsync(Uri api, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(code);
        using var document = await SendAsync(HttpMethod.Post, Address(api, $"app-manifests/{Uri.EscapeDataString(code)}/conversions"), credential: null, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        return new GitHubAppCredentials(
            root.GetProperty("id").GetInt64(),
            Text(root, "slug"),
            Text(root, "name"),
            Text(root, "client_id"),
            Text(root, "client_secret"),
            Text(root, "webhook_secret"),
            Text(root, "pem"),
            Text(root.GetProperty("owner"), "login"),
            new Uri(Text(root, "html_url")));
    }

    /// <summary>The app the token is of; proves that the app's id and key belong together.</summary>
    public async Task<GitHubAppInfo> GetAppAsync(Uri api, string jwt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        using var document = await SendAsync(HttpMethod.Get, Address(api, "app"), jwt, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        return new GitHubAppInfo(
            root.GetProperty("id").GetInt64(),
            Text(root, "slug"),
            Text(root, "name"),
            root.TryGetProperty("client_id", out var client) && client.ValueKind == JsonValueKind.String ? client.GetString() : null,
            Text(root.GetProperty("owner"), "login"),
            new Uri(Text(root, "html_url")));
    }

    /// <summary>Where the app is installed (the first 100 installations).</summary>
    public async Task<IReadOnlyList<GitHubInstallation>> ListInstallationsAsync(Uri api, string jwt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        using var document = await SendAsync(HttpMethod.Get, Address(api, "app/installations?per_page=100"), jwt, cancellationToken).ConfigureAwait(false);
        return [.. document.RootElement.EnumerateArray().Select(i => new GitHubInstallation(
            i.GetProperty("id").GetInt64(),
            Text(i.GetProperty("account"), "login"),
            Text(i.GetProperty("account"), "type"),
            string.Equals(Text(i, "repository_selection"), "all", StringComparison.Ordinal),
            i.TryGetProperty("suspended_at", out var suspended) && suspended.ValueKind == JsonValueKind.String))];
    }

    /// <summary>A new installation token (valid an hour) for an installation of the app.</summary>
    public async Task<GitHubToken> CreateInstallationTokenAsync(Uri api, string jwt, long installationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        using var document = await SendAsync(HttpMethod.Post, Address(api, $"app/installations/{installationId.ToString(CultureInfo.InvariantCulture)}/access_tokens"), jwt, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        return new GitHubToken(Text(root, "token"), DateTimeOffset.Parse(Text(root, "expires_at"), CultureInfo.InvariantCulture));
    }

    /// <summary>The repositories an installation token reaches, page by page up to <see cref="MaxRepositories"/>.</summary>
    public async Task<IReadOnlyList<GitHubRepository>> ListInstallationRepositoriesAsync(Uri api, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        var repositories = new List<GitHubRepository>();
        for (var page = 1; repositories.Count < MaxRepositories; page++)
        {
            using var document = await SendAsync(HttpMethod.Get, Address(api, $"installation/repositories?per_page=100&page={page.ToString(CultureInfo.InvariantCulture)}"), token, cancellationToken).ConfigureAwait(false);
            var batch = document.RootElement.GetProperty("repositories").EnumerateArray().Select(Repository).ToList();
            repositories.AddRange(batch);
            if (batch.Count < 100)
            {
                break;
            }
        }

        return repositories;
    }

    /// <summary>One repository, by <c>owner/name</c>, as the token sees it.</summary>
    public async Task<GitHubRepository> GetRepositoryAsync(Uri api, string token, string fullName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(fullName);
        var parts = fullName.Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0 || p is "." or ".."))
        {
            throw new ForgeException($"'{fullName}' is not a repository's owner/name.");
        }

        using var document = await SendAsync(HttpMethod.Get, Address(api, $"repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}"), token, cancellationToken).ConfigureAwait(false);
        return Repository(document.RootElement);
    }

    private static GitHubRepository Repository(JsonElement element) => new(
        element.GetProperty("id").GetInt64(),
        Text(element, "full_name"),
        new Uri(Text(element, "clone_url")),
        Text(element, "default_branch"),
        element.TryGetProperty("private", out var isPrivate) && isPrivate.ValueKind == JsonValueKind.True);

    private static Uri Address(Uri api, string path) => new(new Uri(api.ToString().TrimEnd('/') + "/"), path);

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new ForgeException($"GitHub's answer has no '{name}'.");

    private async Task<JsonDocument> SendAsync(HttpMethod method, Uri address, string? credential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, address);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AutoCanvas", "0"));
        if (credential is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ForgeException($"GitHub cannot be reached at {address.GetLeftPart(UriPartial.Authority)}: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ForgeException($"GitHub did not answer at {address.GetLeftPart(UriPartial.Authority)} in time.", exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new ForgeException($"GitHub answered {(int)response.StatusCode}: {Message(body)}");
            }

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException exception)
            {
                throw new ForgeException("GitHub's answer is not JSON.", exception);
            }
        }
    }

    private static string Message(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString()! : "no reason given.";
        }
        catch (JsonException)
        {
            return "no reason given.";
        }
    }
}
