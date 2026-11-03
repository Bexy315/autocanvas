using System.Net;
using System.Net.Http.Json;
using AutoCanvas.Persistence;
using AutoCanvas.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Tests;

/// <summary>A reverse proxy in front of the server: what it forwards counts only when it is trusted.</summary>
[Collection(CommandLineTests.Name)]
public sealed class ProxyTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>Every request seems to come from the proxy at 10.0.0.2.</summary>
    private static AutoCanvasFactory BehindProxy(string? trusted)
    {
        var factory = new AutoCanvasFactory()
            .WithServices(services => services.AddSingleton<IStartupFilter>(new FromProxy()));
        return trusted is null ? factory : factory.With("Server:TrustedProxies", trusted);
    }

    private static HttpRequestMessage SignIn(string client, string scheme, string userName = "nobody", string password = "wrong password!")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/sign-in", UriKind.Relative))
        {
            Content = JsonContent.Create(new { userName, password }),
        };
        request.Headers.Add("X-Forwarded-For", client);
        request.Headers.Add("X-Forwarded-Proto", scheme);
        return request;
    }

    [Fact]
    public async Task A_trusted_proxy_gives_each_client_its_own_sign_in_limit_and_its_scheme()
    {
        await using var factory = BehindProxy("10.0.0.0/8, 192.168.1.5");
        await factory.CreateUserAsync("root", isAdmin: true);
        var client = factory.CreateApiClient();

        HttpResponseMessage last = new(HttpStatusCode.OK);
        // Other names each time, so the lock of one name does not count: only the address's limit.
        for (var i = 0; i <= 10; i++)
        {
            last = await client.SendAsync(SignIn("203.0.113.1", "https", $"nobody{i}"), Cancel);
        }

        last.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await client.SendAsync(SignIn("203.0.113.2", "https"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The proxy ended TLS: the cookie is for HTTPS only.
        var signedIn = await client.SendAsync(SignIn("203.0.113.3", "https", "root", ApiClient.Password), Cancel);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.OK);
        signedIn.Headers.GetValues("Set-Cookie").Single().ShouldContain("secure", Case.Insensitive);
    }

    [Fact]
    public async Task Without_trusted_proxies_forwarded_headers_are_ignored()
    {
        await using var factory = BehindProxy(trusted: null);
        await factory.CreateUserAsync("root", isAdmin: true);
        var client = factory.CreateApiClient();

        var signedIn = await client.SendAsync(SignIn("203.0.113.3", "https", "root", ApiClient.Password), Cancel);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.OK);
        signedIn.Headers.GetValues("Set-Cookie").Single().ShouldNotContain("secure", Case.Insensitive);
    }

    [Fact]
    public async Task Trusted_proxies_must_be_addresses_or_networks()
    {
        await using var database = await TestDatabase.CreateAsync(DatabaseProvider.Sqlite);

        (await ServerStart.FailsAsync<OptionsValidationException>(database, ("Server:TrustedProxies", "10.0.0.0/8, proxy.example.org"))).ShouldContain("proxy.example.org");
    }

    /// <summary>Makes the connection come from the proxy, before the server's own pipeline.</summary>
    private sealed class FromProxy : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate following) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
                return following(context);
            });
            next(app);
        };
    }
}
