using AutoCanvas.Server.Application.Identity;

namespace AutoCanvas.Server.Api.Identity;

/// <summary>A successful sign-in. The session itself travels in an HttpOnly cookie, never in the body.</summary>
/// <param name="Me">The signed-in user with their groups.</param>
/// <param name="ExpiresAt">When the session ends at the latest.</param>
internal sealed record SignInResponse(MeView Me, DateTimeOffset ExpiresAt);
