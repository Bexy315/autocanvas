namespace AutoCanvas.Persistence.Forges;

/// <summary>
/// A webhook delivery of the GitHub App that was taken, by GitHub's delivery id, so that a delivery
/// GitHub sends again is handled once. Kept for a few days.
/// </summary>
public sealed class GitHubDelivery
{
    /// <summary>Column size of <see cref="DeliveryId"/>.</summary>
    public const int MaxIdLength = 100;

    /// <summary>GitHub's id of the delivery (<c>X-GitHub-Delivery</c>).</summary>
    public required string DeliveryId { get; init; }

    /// <summary>When it arrived.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }
}
