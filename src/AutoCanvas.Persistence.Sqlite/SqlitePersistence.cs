using AutoCanvas.Persistence.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Persistence.Sqlite;

/// <summary>Registers SQLite as the AutoCanvas database.</summary>
public static class SqlitePersistence
{
    /// <summary>Configures EF Core for SQLite with the AutoCanvas conventions.</summary>
    public static DbContextOptionsBuilder UseAutoCanvasSqlite(this DbContextOptionsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseSqlite(connectionString).UseSnakeCaseNamingConvention();
    }

    /// <summary>Makes <see cref="AutoCanvasDbContext"/> resolve to the SQLite context.</summary>
    public static IServiceCollection AddAutoCanvasSqlite(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IJobClaimer, SqliteJobClaimer>();
        return services.AddDbContext<AutoCanvasDbContext, SqliteAutoCanvasDbContext>(options => options.UseAutoCanvasSqlite(connectionString));
    }
}
