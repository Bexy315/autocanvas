namespace AutoCanvas.Server.Application.Forges;

/// <summary>A new text of a repository automation, to propose as a pull request.</summary>
/// <param name="Yaml">The automation file as it should be.</param>
public sealed record ChangeProposal(string Yaml)
{
    /// <summary>The pull request's title; the commit message when unset.</summary>
    public string? Title { get; init; }

    /// <summary>The commit message; <c>Change {automation} in AutoCanvas</c> when unset.</summary>
    public string? Message { get; init; }
}
