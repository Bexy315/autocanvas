namespace AutoCanvas.Server.Application.Repositories;

/// <summary>Why a file of a repository is not an automation of the folder (yet).</summary>
/// <param name="Path">The file, from the repository's root.</param>
/// <param name="Message">What is wrong, in words for people.</param>
/// <param name="Line">The line in the file, when the problem can be pinned to one.</param>
public sealed record RepositoryProblemView(string Path, string Message, int? Line);
