using AutoCanvas.Server.Application.Identity;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Authentication;

/// <summary>Session durations must be positive, and a session cannot idle longer than it lives.</summary>
internal sealed class SessionOptionsValidator : IValidateOptions<SessionLifetimeOptions>
{
    public ValidateOptionsResult Validate(string? name, SessionLifetimeOptions options)
    {
        if (options.SessionIdleTimeout <= TimeSpan.Zero || options.SessionLifetime <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("Authentication:SessionIdleTimeout and Authentication:SessionLifetime must be positive, e.g. 7.00:00:00 for seven days.");
        }

        return options.SessionIdleTimeout > options.SessionLifetime
            ? ValidateOptionsResult.Fail("Authentication:SessionIdleTimeout must not be longer than Authentication:SessionLifetime.")
            : ValidateOptionsResult.Success;
    }
}
