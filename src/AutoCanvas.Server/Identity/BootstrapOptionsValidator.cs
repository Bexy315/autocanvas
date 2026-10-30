using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Identity;

/// <summary>The bootstrap admin needs both a name and a password, or neither.</summary>
internal sealed class BootstrapOptionsValidator : IValidateOptions<BootstrapOptions>
{
    public ValidateOptionsResult Validate(string? name, BootstrapOptions options) =>
        string.IsNullOrEmpty(options.AdminUserName) == string.IsNullOrEmpty(options.AdminPassword)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Bootstrap:AdminUserName and Bootstrap:AdminPassword must be set together.");
}
