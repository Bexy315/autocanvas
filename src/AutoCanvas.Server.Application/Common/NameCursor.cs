using System.Buffers.Text;
using System.Text;

namespace AutoCanvas.Server.Application.Common;

/// <summary>
/// Cursors for lists ordered by a unique normalized name: the cursor is the last name of the
/// previous page, encoded so that clients treat it as opaque.
/// </summary>
internal static class NameCursor
{
    public static string Encode(string normalizedName) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(normalizedName));

    public static UseCaseResult<string?> Decode(string? cursor)
    {
        if (cursor is null)
        {
            return UseCase.Success<string?>(null);
        }

        try
        {
            return UseCase.Success<string?>(Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)));
        }
        catch (FormatException)
        {
            return UseCase.Failure<string?>(UseCaseError.Invalid("cursor", "cursor is not a value from a previous page."));
        }
    }
}
