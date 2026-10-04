using ComicStudio.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Data.Seed;

public static class DatabaseSeeder
{
    public static async Task SeedDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");

        await db.Database.OpenConnectionAsync();
        await using (var pragma = db.Database.GetDbConnection().CreateCommand())
        {
            // WAL keeps the background generation workers from blocking the UI's reads.
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            await pragma.ExecuteScalarAsync();
        }

        await db.Database.MigrateAsync();
        await AiModelSeeder.SeedAsync(db);
        await LayoutTemplateSeeder.SeedAsync(db);
        await ResetAbandonedCurationAsync(db, logger);

        logger.LogInformation("Database ready.");
    }

    /// <summary>
    /// Curation runs in-process, so a project still marked Curating after a
    /// restart is abandoned. Without this it would sit there forever, blocking
    /// re-curation and looking like a hang.
    /// </summary>
    private static async Task ResetAbandonedCurationAsync(AppDbContext db, ILogger logger)
    {
        var stuck = await db
            .ComicProjects.Where(project => project.Status == ComicStatus.Curating)
            .ToListAsync();

        if (stuck.Count == 0)
        {
            return;
        }

        foreach (var project in stuck)
        {
            project.Status = ComicStatus.Draft;
        }

        await db.SaveChangesAsync();
        logger.LogWarning(
            "Reset {Count} comic(s) left mid-curation by a restart.",
            stuck.Count
        );
    }
}
