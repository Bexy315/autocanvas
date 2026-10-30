using Microsoft.AspNetCore.Authorization;

namespace AutoCanvas.Server.Authentication;

/// <summary>
/// The user has no password to change first. Part of every standard policy, so an account with a
/// password set by someone else can do nothing but change it (and read itself, sign out).
/// </summary>
internal sealed class PasswordChangedRequirement : IAuthorizationRequirement;
