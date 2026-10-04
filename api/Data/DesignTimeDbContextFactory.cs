using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ComicStudio.Api.Data;

/// <summary>
/// Used by `dotnet ef` so migrations can be created without booting the web host.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // `dotnet ef` runs from the project directory; keep the design-time
        // database beside the real one so paths behave identically.
        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
        Directory.CreateDirectory(dataDirectory);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(dataDirectory, "comicstudio.design.db")}")
            .Options;

        return new AppDbContext(options);
    }
}
