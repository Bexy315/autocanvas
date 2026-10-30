using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoCanvas.Server.Application;

/// <summary>Registers the server's use cases. A database provider must be registered as well.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the use cases and the audit log. Unless the host registers its own
    /// <see cref="IAuditContext"/>, changes are attributed to the system.
    /// </summary>
    public static IServiceCollection AddAutoCanvasApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAuditContext, SystemAuditContext>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<DatabaseMigrator>();
        services.AddScoped<DatabaseMaintenance>();
        return services;
    }
}
