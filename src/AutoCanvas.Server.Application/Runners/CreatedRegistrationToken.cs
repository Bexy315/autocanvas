namespace AutoCanvas.Server.Application.Runners;

/// <summary>A new runner registration token. <see cref="Token"/> is shown this once.</summary>
/// <param name="Token">The secret to pass to the runner's <c>register</c> command.</param>
/// <param name="Details">The token as it will be listed.</param>
public sealed record CreatedRegistrationToken(string Token, RegistrationTokenView Details);
