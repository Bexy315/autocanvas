using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutoCanvas.Protocol.Runner.V1;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// The git calls for one checkout. The credential reaches git as an HTTP header through
/// <c>GIT_CONFIG_*</c> variables: never as an argument (visible in the process list) and never in
/// the clone's <c>.git/config</c> (readable by every later step).
/// </summary>
internal static partial class CheckoutCommand
{
    /// <summary>The git commands, each run in the target's parent directory or the target itself.</summary>
    /// <param name="checkout">The checkout.</param>
    /// <param name="target">Where the clone goes, inside the container.</param>
    public static List<string[]> Commands(Checkout checkout, string target)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        var depth = checkout.Depth > 0 ? checkout.Depth.ToString(CultureInfo.InvariantCulture) : null;
        if (Sha().IsMatch(checkout.Ref))
        {
            // A commit cannot be cloned by name; fetch exactly it instead.
            return
            [
                ["git", "init", "--quiet", target],
                ["git", "-C", target, "remote", "add", "origin", "--", checkout.Url],
                ["git", "-C", target, "fetch", "--no-tags", .. (depth is null ? Array.Empty<string>() : ["--depth", depth]), "origin", checkout.Ref],
                ["git", "-C", target, "checkout", "--quiet", "--detach", "FETCH_HEAD"],
            ];
        }

        var clone = new List<string> { "git", "clone" };
        if (depth is not null)
        {
            clone.AddRange(["--depth", depth]);
        }

        if (checkout.Ref.Length > 0)
        {
            clone.AddRange(["--branch", checkout.Ref]);
        }

        // "--" first, so that a URL or path starting with a dash is never read as an option.
        clone.AddRange(["--", checkout.Url, target]);
        return [[.. clone]];
    }

    /// <summary>The environment of the git calls: no prompts, and the credential as a header.</summary>
    public static Dictionary<string, string> Environment(Checkout checkout)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["GIT_TERMINAL_PROMPT"] = "0" };
        if (checkout.Password.Length > 0)
        {
            environment["GIT_CONFIG_COUNT"] = "1";
            environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
            environment["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {Basic(checkout)}";
        }

        return environment;
    }

    /// <summary>What must not show in the log: the password and the header made of it.</summary>
    public static IEnumerable<string> Secrets(Checkout checkout)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        return checkout.Password.Length == 0 ? [] : [checkout.Password, Basic(checkout)];
    }

    private static string Basic(Checkout checkout) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{(checkout.Username.Length > 0 ? checkout.Username : "x-access-token")}:{checkout.Password}"));

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Sha();
}
