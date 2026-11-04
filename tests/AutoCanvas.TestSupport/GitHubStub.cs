using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoCanvas.TestSupport;

/// <summary>
/// A stand-in for GitHub's REST API, as a GitHub App uses it, under <c>/api/v3</c> of
/// <see cref="Web"/> (as on a GitHub Enterprise Server). It has one app with a fresh RSA key and
/// checks the app's JWTs and its installation tokens as GitHub does. Repositories live in
/// <see cref="Git"/>, which takes exactly the installation token as its credential.
/// </summary>
public sealed class GitHubStub : IAsyncDisposable
{
    /// <summary>The app's id.</summary>
    public const long AppId = 4242;

    /// <summary>The code the manifest flow hands out.</summary>
    public const string ManifestCode = "manifest-code";

    private readonly WebApplication app;
    private readonly RSA key = RSA.Create(2048);
    private readonly ConcurrentDictionary<long, (string Account, List<(long Id, string Name)> Repositories)> installations = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<string>> pullRequestFiles = new();
    private readonly ConcurrentQueue<string> tokenRequestBodies = new();
    private int tokenRequests;

    private GitHubStub(WebApplication app, string address, GitHttpServer git, string token)
    {
        this.app = app;
        Web = new Uri(address);
        Git = git;
        Token = token;
        PrivateKey = key.ExportRSAPrivateKeyPem();
    }

    /// <summary>The stub's web address; its API is at <c>/api/v3</c>.</summary>
    public Uri Web { get; }

    /// <summary>The Git server with the repositories.</summary>
    public GitHttpServer Git { get; }

    /// <summary>The installation token every installation gets, the Git server's password.</summary>
    public string Token { get; }

    /// <summary>The app's private key in PEM.</summary>
    public string PrivateKey { get; }

    /// <summary>The app's webhook secret.</summary>
    public string WebhookSecret { get; } = "stub-webhook-secret";

    /// <summary>How many installation tokens were asked for.</summary>
    public int TokenRequests => tokenRequests;

    /// <summary>The bodies of the token requests, as sent (empty for an unrestricted token).</summary>
    public IReadOnlyCollection<string> TokenRequestBodies => tokenRequestBodies;

    /// <summary>Starts the stub with a Git server of its own.</summary>
    public static async Task<GitHubStub> StartAsync()
    {
        var token = "ghs_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var git = await GitHttpServer.StartAsync(("x-access-token", token)).ConfigureAwait(false);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var host = builder.Build();
        GitHubStub? stub = null;
        host.Run(context => stub!.ServeAsync(context));
        await host.StartAsync().ConfigureAwait(false);
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        stub = new GitHubStub(host, address + "/", git, token);
        return stub;
    }

    /// <summary>The files pull request <paramref name="number"/> changes, as GitHub lists them.</summary>
    public void SetPullRequestFiles(int number, params string[] files) => pullRequestFiles[number] = files;

    /// <summary>
    /// Installs the app on an account with access to repositories of the Git server, by name: the
    /// Git server holds them as <c>account/name</c>.
    /// </summary>
    public void Install(long installationId, string account, params (long Id, string Name)[] repositories) =>
        installations[installationId] = (account, [.. repositories]);

    /// <summary>The <c>X-Hub-Signature-256</c> of a body with the app's webhook secret.</summary>
    public string Sign(string body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(body)));

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync().ConfigureAwait(false);
        await Git.DisposeAsync().ConfigureAwait(false);
        key.Dispose();
    }

    private async Task ServeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var (status, body) = segments switch
        {
            ["api", "v3", "app-manifests", var code, "conversions"] when HttpMethods.IsPost(context.Request.Method) => Conversion(code),
            ["api", "v3", "app"] => AsApp(context, () => (200, AppJson())),
            ["api", "v3", "app", "installations"] => AsApp(context, () => (200, installations.Select(i => (object)new
            {
                id = i.Key,
                account = new { login = i.Value.Account, type = "Organization" },
                repository_selection = "selected",
                suspended_at = (string?)null,
            }).ToList())),
            ["api", "v3", "app", "installations", var id, "access_tokens"] when HttpMethods.IsPost(context.Request.Method) => AsApp(context, () => NewToken(id, Body(context))),
            ["api", "v3", "repos", _, _, "pulls", var number, "files"] => AsInstallation(context, () => PullRequestFiles(number, context.Request.Query["page"])),
            ["api", "v3", "installation", "repositories"] => AsInstallation(context, () => (200, new { total_count = AllRepositories().Count(), repositories = AllRepositories().Select(Repository).ToList() })),
            ["api", "v3", "repos", var owner, var name] => AsInstallation(context, () => AllRepositories().FirstOrDefault(r => string.Equals($"{r.Account}/{r.Name}", $"{owner}/{name}", StringComparison.Ordinal)) is { Name: not null } found
                ? (200, Repository(found))
                : (404, new { message = "Not Found" })),
            _ => (404, (object)new { message = "Not Found" }),
        };
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(body)).ConfigureAwait(false);
    }

    private (int, object) Conversion(string code) => string.Equals(code, ManifestCode, StringComparison.Ordinal)
        ? (201, new
        {
            id = AppId,
            slug = "autocanvas-test",
            name = "AutoCanvas (test)",
            client_id = "Iv1.stub",
            client_secret = "stub-client-secret",
            webhook_secret = WebhookSecret,
            pem = PrivateKey,
            owner = new { login = "acme" },
            html_url = new Uri(Web, "apps/autocanvas-test").ToString(),
        })
        : (404, new { message = "Not Found" });

    private object AppJson() => new
    {
        id = AppId,
        slug = "autocanvas-test",
        name = "AutoCanvas (test)",
        client_id = "Iv1.stub",
        owner = new { login = "acme" },
        html_url = new Uri(Web, "apps/autocanvas-test").ToString(),
    };

    private static string Body(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        return reader.ReadToEndAsync().GetAwaiter().GetResult();
    }

    private (int, object) PullRequestFiles(string number, string? page)
    {
        var files = pullRequestFiles.GetValueOrDefault(int.Parse(number, CultureInfo.InvariantCulture)) ?? [];
        var index = int.Parse(page ?? "1", CultureInfo.InvariantCulture) - 1;
        return (200, files.Skip(index * 100).Take(100).Select(f => new { filename = f }).ToList());
    }

    private (int, object) NewToken(string id, string body)
    {
        tokenRequestBodies.Enqueue(body);
        if (!installations.ContainsKey(long.Parse(id, CultureInfo.InvariantCulture)))
        {
            return (404, new { message = "Not Found" });
        }

        Interlocked.Increment(ref tokenRequests);
        return (201, new { token = Token, expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("O", CultureInfo.InvariantCulture) });
    }

    private IEnumerable<(long Id, string Account, string Name)> AllRepositories() =>
        installations.Values.SelectMany(i => i.Repositories.Select(r => (r.Id, i.Account, r.Name)));

    private object Repository((long Id, string Account, string Name) repository) => new
    {
        id = repository.Id,
        full_name = $"{repository.Account}/{repository.Name}",
        clone_url = Git.Url($"{repository.Account}/{repository.Name}").ToString(),
        default_branch = "main",
        @private = true,
    };

    /// <summary>Answers as an app: a valid JWT of the app, signed with its key, not expired.</summary>
    private (int, object) AsApp(HttpContext context, Func<(int, object)> answer) =>
        Bearer(context) is { } jwt && ValidJwt(jwt) ? answer() : (401, new { message = "A JSON web token could not be decoded" });

    /// <summary>Answers as an installation: its token.</summary>
    private (int, object) AsInstallation(HttpContext context, Func<(int, object)> answer) =>
        string.Equals(Bearer(context), Token, StringComparison.Ordinal) ? answer() : (401, new { message = "Bad credentials" });

    private static string? Bearer(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : null;
    }

    private bool ValidJwt(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        if (!key.VerifyData(signed, Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            return false;
        }

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        var root = payload.RootElement;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return string.Equals(root.GetProperty("iss").GetString(), AppId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            && root.GetProperty("exp").GetInt64() > now
            && root.GetProperty("exp").GetInt64() - root.GetProperty("iat").GetInt64() <= 600;
    }
}
