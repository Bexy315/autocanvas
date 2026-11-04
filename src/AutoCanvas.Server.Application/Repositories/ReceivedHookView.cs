namespace AutoCanvas.Server.Application.Repositories;

/// <summary>What became of a call of a repository's webhook address.</summary>
/// <param name="Queued">Whether it was a push, which starts its runs shortly.</param>
/// <param name="Message">What happened, in words for the forge's delivery log.</param>
public sealed record ReceivedHookView(bool Queued, string Message);
