using AutoCanvas.Server.Application.Audit;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>Retention is a number of days, or 0 to keep the audit log forever.</summary>
internal sealed class AuditOptionsValidator : IValidateOptions<AuditOptions>
{
    public ValidateOptionsResult Validate(string? name, AuditOptions options) =>
        options.RetentionDays >= 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Audit:RetentionDays must be 0 (keep forever) or a number of days, e.g. 365.");
}
