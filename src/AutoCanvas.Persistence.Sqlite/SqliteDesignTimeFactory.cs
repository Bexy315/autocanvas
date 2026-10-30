using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AutoCanvas.Persistence.Sqlite;

/// <summary>Lets <c>dotnet ef</c> create the context to generate migrations; never connects.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the dotnet ef tools through reflection.")]
internal sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAutoCanvasDbContext>
{
    public SqliteAutoCanvasDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqliteAutoCanvasDbContext>();
        options.UseAutoCanvasSqlite("Data Source=autocanvas_design_time.db");
        return new(options.Options);
    }
}
