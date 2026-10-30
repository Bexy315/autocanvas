namespace AutoCanvas.Server.Api.Identity;

/// <summary>A password chosen for a user by an admin; the user must change it at the next sign-in.</summary>
/// <param name="Password">The new password: 12 to 256 characters.</param>
internal sealed record ResetPasswordRequest(string Password);
