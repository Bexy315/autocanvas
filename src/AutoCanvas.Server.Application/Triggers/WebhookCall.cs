namespace AutoCanvas.Server.Application.Triggers;

/// <summary>A call of a webhook address, as it arrived.</summary>
/// <param name="Body">The body's bytes, at most <see cref="WebhookService.MaxBodyBytes"/>.</param>
/// <param name="IsJson">Whether the body is JSON, by its content type.</param>
/// <param name="Signature">The <c>X-AutoCanvas-Signature</c> header, if sent.</param>
/// <param name="Headers">The headers, without credentials and the signature.</param>
/// <param name="Query">The query parameters.</param>
public sealed record WebhookCall(ReadOnlyMemory<byte> Body, bool IsJson, string? Signature, IReadOnlyDictionary<string, string> Headers, IReadOnlyDictionary<string, string> Query);
