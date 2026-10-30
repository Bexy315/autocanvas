using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AutoCanvas.Persistence.Postgres;

/// <summary>Lets <c>dotnet ef</c> create the context to generate migrations; never connects.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the dotnet ef tools through reflection.")]
internal sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAutoCanvasDbContext>
{
    public PostgresAutoCanvasDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PostgresAutoCanvasDbContext>();
        options.UseAutoCanvasPostgres("Host=localhost;Database=autocanvas_design_time");
        return new(options.Options);
    }
}
