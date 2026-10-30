using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Server.Application.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Server.Tests;

/// <summary>Helpers for API tests: clients that send the CSRF header, users, sign-in.</summary>
internal static class ApiClient
{
    public const string Password = "a long enough password";

    /// <summary>A client with its own cookie jar that sends <c>X-AutoCanvas-Request: 1</c>.</summary>
    public static HttpClient CreateApiClient(this AutoCanvasFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-AutoCanvas-Request", "1");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("autocanvas-tests/1.0");
        return client;
    }

    /// <summary>Creates a user directly through the use case, as the command line would.</summary>
    public static async Task<UserView> CreateUserAsync(this AutoCanvasFactory factory, string userName, bool isAdmin = false, bool mustChangePassword = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<UserService>().CreateAsync(
            new NewUser(userName, Password) { IsAdmin = isAdmin, MustChangePassword = mustChangePassword },
            TestContext.Current.CancellationToken);
        result.Error.ShouldBeNull();
        return result.Value!;
    }

    /// <summary>A client signed in as a new user.</summary>
    public static async Task<HttpClient> SignedInAsync(this AutoCanvasFactory factory, string userName, bool isAdmin = false, bool mustChangePassword = false)
    {
        await factory.CreateUserAsync(userName, isAdmin, mustChangePassword);
        var client = factory.CreateApiClient();
        (await client.SignInAsync(userName)).EnsureSuccessStatusCode();
        return client;
    }

    public static Task<HttpResponseMessage> SignInAsync(this HttpClient client, string userName, string password = Password) =>
        client.PostAsJsonAsync("/api/v1/auth/sign-in", new { userName, password }, TestContext.Current.CancellationToken);

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    public static async Task<string> ProblemTypeAsync(this HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("type").GetString()!.Split('#')[1];
}
