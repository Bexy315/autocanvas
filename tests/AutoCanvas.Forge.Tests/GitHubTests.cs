using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoCanvas.Forge.GitHub;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Forge.Tests;

public sealed class GitHubTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private GitHubStub stub = null!;
    private readonly HttpClient http = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private Uri Api => new(stub.Web, "api/v3");

    public async ValueTask InitializeAsync()
    {
        stub = await GitHubStub.StartAsync();
        await stub.Git.CreateAsync("shop");
        stub.Install(7, "acme", (11, "shop"));
    }

    public async ValueTask DisposeAsync()
    {
        http.Dispose();
        await stub.DisposeAsync();
    }

    [Fact]
    public void The_jwt_is_signed_with_the_key_and_valid_for_nine_minutes()
    {
        using var rsa = RSA.Create(2048);

        var jwt = GitHubJwt.Create("42", rsa.ExportRSAPrivateKeyPem(), Now);

        var parts = jwt.Split('.');
        rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).ShouldBeTrue();
        var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1])).RootElement;
        payload.GetProperty("iss").GetString().ShouldBe("42");
        payload.GetProperty("iat").GetInt64().ShouldBe(Now.AddMinutes(-1).ToUnixTimeSeconds());
        payload.GetProperty("exp").GetInt64().ShouldBe(Now.AddMinutes(9).ToUnixTimeSeconds());
    }

    [Fact]
    public void Keys_that_are_not_RSA_private_keys_of_2048_bits_are_refused()
    {
        using var small = RSA.Create(1024);

        Should.Throw<ForgeException>(() => GitHubJwt.Create("42", "not a key", Now)).Message.ShouldContain("PEM");
        Should.Throw<ForgeException>(() => GitHubJwt.Create("42", small.ExportRSAPrivateKeyPem(), Now)).Message.ShouldContain("2048");
    }

    [Fact]
    public async Task The_client_acts_as_the_app_and_as_an_installation()
    {
        var client = new GitHubClient(http);
        var jwt = GitHubJwt.Create(GitHubStub.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture), stub.PrivateKey, DateTimeOffset.UtcNow);

        (await client.GetAppAsync(Api, jwt, Cancel)).OwnerLogin.ShouldBe("acme");
        (await client.ListInstallationsAsync(Api, jwt, Cancel)).ShouldHaveSingleItem().Account.ShouldBe("acme");
        var token = await client.CreateInstallationTokenAsync(Api, jwt, 7, Cancel);
        token.Token.ShouldBe(stub.Token);
        token.ToString().ShouldNotContain(stub.Token);
        (await client.ListInstallationRepositoriesAsync(Api, token.Token, Cancel)).ShouldHaveSingleItem().FullName.ShouldBe("acme/shop");
        var shop = await client.GetRepositoryAsync(Api, token.Token, "acme/shop", Cancel);
        (shop.Id, shop.CloneUrl, shop.DefaultBranch, shop.Private).ShouldBe((11L, stub.Git.Url("shop"), "main", true));
        var created = await client.ConvertManifestAsync(Api, GitHubStub.ManifestCode, Cancel);
        created.WebhookSecret.ShouldBe(stub.WebhookSecret);
        created.ToString().ShouldNotContain(stub.WebhookSecret);
    }

    [Fact]
    public async Task Failures_say_what_GitHub_answered_or_that_it_cannot_be_reached()
    {
        var client = new GitHubClient(http);

        (await Should.ThrowAsync<ForgeException>(() => client.GetAppAsync(Api, "not-a-jwt", Cancel))).Message.ShouldBe("GitHub answered 401: A JSON web token could not be decoded");
        (await Should.ThrowAsync<ForgeException>(() => client.GetRepositoryAsync(Api, stub.Token, "acme/none", Cancel))).Message.ShouldContain("404");
        (await Should.ThrowAsync<ForgeException>(() => client.GetRepositoryAsync(Api, stub.Token, "../escape", Cancel))).Message.ShouldContain("owner/name");
        (await Should.ThrowAsync<ForgeException>(() => client.GetAppAsync(new Uri("http://127.0.0.1:1/api/v3"), "x", Cancel))).Message.ShouldContain("cannot be reached");
    }

    [Fact]
    public async Task The_GitHub_forge_reads_with_git_and_the_installation_token()
    {
        await stub.Git.CommitAsync("shop", new Dictionary<string, string?>(StringComparer.Ordinal) { [".autocanvas/automations/build.yml"] = "version: 1\n" }, "Start");
        var forge = new GitHubForge(TimeSpan.FromSeconds(60));
        var cache = Directory.CreateTempSubdirectory("autocanvas-github-forge-").FullName;
        try
        {
            var access = new RepositoryAccess(stub.Git.Url("shop"), RepositoryAccess.TokenUserName, stub.Token, cache);

            forge.Kind.ShouldBe(ForgeKind.GitHub);
            var head = await forge.ResolveAsync(access, null, Cancel);
            (await forge.ReadAutomationsAsync(access, head.Branch, Cancel)).Files.ShouldHaveSingleItem();
            (await forge.ListRefsAsync(access, Cancel)).Keys.ShouldBe(["refs/heads/main"]);
            (await forge.ChangedFilesAsync(access, "main", new string('0', 40), head.Commit, Cancel)).ShouldBeNull();
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(cache, recursive: true);
        }
    }
}
