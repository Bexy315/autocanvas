using System.Net;
using System.Security.Cryptography;
using System.Text;
using AutoCanvas.Server.Application.Forges;

namespace AutoCanvas.Server.Api.Forges;

/// <summary>
/// The page that takes the browser to GitHub with the app's manifest: a form posted to GitHub at
/// once by a script. The UI's own policy allows forms to its own origin only, so this page has a
/// policy of its own: no sources at all, the one script by its hash, and forms to that GitHub only.
/// </summary>
internal static class GitHubLaunchPage
{
    /// <summary>The page's only script.</summary>
    private const string Script = "document.forms[0].submit();";

    private static readonly string ScriptHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Script)));

    /// <summary>The page that posts the manifest to GitHub, and its policy.</summary>
    public static (string Html, string Policy) Launch(GitHubManifestView manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var html = $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><title>To GitHub</title></head>
            <body>
            <form method="post" action="{WebUtility.HtmlEncode(manifest.Action.ToString())}">
            <input type="hidden" name="manifest" value="{WebUtility.HtmlEncode(manifest.Manifest)}">
            <p>Taking you to GitHub to create the app…</p>
            <noscript><button type="submit">Continue to GitHub</button></noscript>
            </form>
            <script>{Script}</script>
            </body>
            </html>
            """;
        var policy = $"default-src 'none'; script-src 'sha256-{ScriptHash}'; form-action {manifest.Action.GetLeftPart(UriPartial.Authority)}; base-uri 'none'; frame-ancestors 'none'";
        return (html, policy);
    }

    /// <summary>A page that says why the browser is not taken to GitHub, and its policy.</summary>
    public static (string Html, string Policy) Refused(string reason)
    {
        var html = $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><title>Not to GitHub</title></head>
            <body>
            <p>{WebUtility.HtmlEncode(reason)}</p>
            <p><a href="/admin/forges">Back to Forges</a></p>
            </body>
            </html>
            """;
        return (html, "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'");
    }
}
