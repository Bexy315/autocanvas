namespace AutoCanvas.Server.Api.Identity;

/// <summary>Credentials for signing in.</summary>
/// <param name="UserName">The user name; case does not matter.</param>
/// <param name="Password">The password.</param>
internal sealed record SignInRequest(string UserName, string Password);
