namespace AutoCanvas.Server.Application.Forges;

/// <summary>The pull request a change was proposed as.</summary>
/// <param name="Number">Its number.</param>
/// <param name="Url">Its page on the forge.</param>
/// <param name="Branch">The branch with the change.</param>
public sealed record ProposedChangeView(int Number, Uri Url, string Branch);
