namespace AutoCanvas.Server.Api.Identity;

/// <summary>Changes to a user; omitted properties stay as they are.</summary>
/// <param name="DisplayName">A new display name.</param>
/// <param name="Email">A new contact address; an empty string removes it.</param>
/// <param name="IsAdmin">Grant or take away instance administration.</param>
/// <param name="IsDisabled">Block or unblock the user; blocking signs them out everywhere.</param>
internal sealed record UpdateUserRequest(string? DisplayName = null, string? Email = null, bool? IsAdmin = null, bool? IsDisabled = null);
