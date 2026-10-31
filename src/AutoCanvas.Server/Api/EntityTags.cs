using System.Globalization;

namespace AutoCanvas.Server.Api;

/// <summary>
/// ETags of versioned resources (<c>"3"</c>) and the <c>If-Match</c> header that guards changes
/// against lost updates (API conventions: concurrency).
/// </summary>
internal static class EntityTags
{
    public static string Format(int version) => string.Create(CultureInfo.InvariantCulture, $"\"{version}\"");

    /// <summary>
    /// The version the client expects, or <see langword="null"/> when it sent no condition (or
    /// <c>*</c>). A value that is not one of our ETags can never match, so it yields -1.
    /// </summary>
    public static int? ExpectedVersion(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var header = request.Headers.IfMatch.ToString().Trim();
        if (header.Length == 0 || string.Equals(header, "*", StringComparison.Ordinal))
        {
            return null;
        }

        var tag = header.StartsWith("W/", StringComparison.Ordinal) ? header[2..] : header;
        return tag.Length > 2 && tag[0] == '"' && tag[^1] == '"'
            && int.TryParse(tag.AsSpan(1, tag.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : -1;
    }

    /// <summary>The result, with its ETag header on success.</summary>
    public static IResult WithETag<T>(this IResult result, HttpContext context, Application.Common.UseCaseResult<T> outcome, Func<T, int> version)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Error is null)
        {
            context.Response.Headers.ETag = Format(version(outcome.Value!));
        }

        return result;
    }
}
