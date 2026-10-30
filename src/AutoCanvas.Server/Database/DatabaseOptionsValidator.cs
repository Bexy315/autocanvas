using AutoCanvas.Persistence;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Database;

/// <summary>Rejects database settings that cannot work, before anything connects.</summary>
internal sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        if (!Enum.IsDefined(options.Provider))
        {
            return ValidateOptionsResult.Fail("Database:Provider must be Sqlite or Postgres.");
        }

        if (options.Provider == DatabaseProvider.Postgres && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail(
                "Database:ConnectionString is required for PostgreSQL, e.g. Host=db;Database=autocanvas;Username=autocanvas;Password=….");
        }

        return ValidateOptionsResult.Success;
    }
}
