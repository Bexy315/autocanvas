using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>A server without a usable master key does not start.</summary>
internal sealed class SecretsOptionsValidator : IValidateOptions<SecretsOptions>
{
    public ValidateOptionsResult Validate(string? name, SecretsOptions options) =>
        options.Load(out var problem) is null ? ValidateOptionsResult.Fail(problem!) : ValidateOptionsResult.Success;
}
