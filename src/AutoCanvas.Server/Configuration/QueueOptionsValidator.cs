using AutoCanvas.Server.Application.Runs;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>A lease must give runners time for a few heartbeats, but notice a lost runner within the hour.</summary>
internal sealed class QueueOptionsValidator : IValidateOptions<QueueOptions>
{
    public ValidateOptionsResult Validate(string? name, QueueOptions options) =>
        options.LeaseDuration >= TimeSpan.FromSeconds(10) && options.LeaseDuration <= TimeSpan.FromHours(1)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Queue:LeaseDuration must be between 00:00:10 and 01:00:00.");
}
