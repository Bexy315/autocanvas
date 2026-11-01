using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>A webhook must take some calls, and no setting should invite floods.</summary>
internal sealed class WebhookOptionsValidator : IValidateOptions<WebhookOptions>
{
    public const int Max = 10_000;

    public ValidateOptionsResult Validate(string? name, WebhookOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.PerMinute is >= 1 and <= Max
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Webhooks:PerMinute must be between 1 and {Max}.");
    }
}
