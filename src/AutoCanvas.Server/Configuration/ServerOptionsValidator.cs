using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>Rejects invalid server settings at start-up instead of on first use.</summary>
internal sealed class ServerOptionsValidator : IValidateOptions<ServerOptions>
{
    public ValidateOptionsResult Validate(string? name, ServerOptions options)
    {
        if (options.PublicUrl is { } url
            && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsHttp(uri)))
        {
            return ValidateOptionsResult.Fail(
                $"Server:PublicUrl must be an absolute http or https URL such as https://autocanvas.example.org, but is '{url}'.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsHttp(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
        || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
}
