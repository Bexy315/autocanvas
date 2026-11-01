namespace AutoCanvas.Server.Configuration;

/// <summary>Limits of webhook calls (configuration section <c>Webhooks</c>).</summary>
internal sealed class WebhookOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Webhooks";

    /// <summary>How many calls one webhook address takes per minute; more are refused with 429. Default: 30.</summary>
    public int PerMinute { get; set; } = 30;
}
