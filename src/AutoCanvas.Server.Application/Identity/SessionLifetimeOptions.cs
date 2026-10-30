namespace AutoCanvas.Server.Application.Identity;

/// <summary>How long a sign-in lasts (configuration section <c>Authentication</c>).</summary>
public sealed class SessionLifetimeOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Authentication";

    /// <summary>A session ends after this long without a request. Default: 7 days.</summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromDays(7);

    /// <summary>A session ends this long after the sign-in, however active it is. Default: 30 days.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);
}
