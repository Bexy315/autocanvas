namespace AutoCanvas.Server.Api.Identity;

/// <summary>A new password for the signed-in user.</summary>
/// <param name="CurrentPassword">The password in use now.</param>
/// <param name="NewPassword">The new password: 12 to 256 characters, not the user name.</param>
internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
