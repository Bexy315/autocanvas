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

        foreach (var entry in (options.TrustedProxies ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                _ = ServerOptions.Network(entry);
            }
            catch (FormatException)
            {
                return ValidateOptionsResult.Fail(
                    $"Server:TrustedProxies must list addresses or networks such as 10.0.0.2 or 172.16.0.0/12, separated by commas, but '{entry}' is neither.");
            }
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsHttp(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
        || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
}
