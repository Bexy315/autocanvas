namespace AutoCanvas.Server.Application.Repositories;

/// <summary>A repository's webhook address, without its token and secret.</summary>
/// <param name="Hint">The token's start and end, to recognize it by.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="CreatedByName">Who created it.</param>
public sealed record RepositoryWebhookView(string Hint, DateTimeOffset CreatedAt, string CreatedByName);
