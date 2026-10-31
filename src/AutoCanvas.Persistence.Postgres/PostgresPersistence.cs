using AutoCanvas.Persistence.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Persistence.Postgres;

/// <summary>Registers PostgreSQL as the AutoCanvas database.</summary>
public static class PostgresPersistence
{
    /// <summary>Configures EF Core for PostgreSQL with the AutoCanvas conventions.</summary>
    public static DbContextOptionsBuilder UseAutoCanvasPostgres(this DbContextOptionsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
    }

    /// <summary>Makes <see cref="AutoCanvasDbContext"/> resolve to the PostgreSQL context.</summary>
    public static IServiceCollection AddAutoCanvasPostgres(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IJobClaimer, PostgresJobClaimer>();
        services.AddSingleton<IJobSignal>(new PostgresJobSignal(connectionString));
        return services.AddDbContext<AutoCanvasDbContext, PostgresAutoCanvasDbContext>(options => options.UseAutoCanvasPostgres(connectionString));
    }
}
