using ComicStudio.Api.Domain;
using Environment = ComicStudio.Api.Domain.Environment;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ComicStudio.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<AiModel> AiModels => Set<AiModel>();

    public DbSet<Story> Stories => Set<Story>();

    public DbSet<StoryBlock> StoryBlocks => Set<StoryBlock>();

    public DbSet<LorebookEntry> LorebookEntries => Set<LorebookEntry>();

    public DbSet<StoryGeneration> StoryGenerations => Set<StoryGeneration>();

    public DbSet<ImageAsset> ImageAssets => Set<ImageAsset>();

    public DbSet<Character> Characters => Set<Character>();

    public DbSet<CharacterSheetImage> CharacterSheetImages => Set<CharacterSheetImage>();

    public DbSet<Environment> Environments => Set<Environment>();

    public DbSet<EnvironmentPlate> EnvironmentPlates => Set<EnvironmentPlate>();

    public DbSet<ComicProject> ComicProjects => Set<ComicProject>();

    public DbSet<ComicPage> ComicPages => Set<ComicPage>();

    public DbSet<Scene> Scenes => Set<Scene>();

    public DbSet<DialogueLine> DialogueLines => Set<DialogueLine>();

    public DbSet<PanelGeneration> PanelGenerations => Set<PanelGeneration>();

    public DbSet<CurationRun> CurationRuns => Set<CurationRun>();

    public DbSet<CoverPlan> CoverPlans => Set<CoverPlan>();

    public DbSet<PageLayoutTemplate> LayoutTemplates => Set<PageLayoutTemplate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        ApplyUtcDateTimes(builder);
    }

    /// <summary>
    /// SQLite cannot order or compare DateTimeOffset columns, and offsets are
    /// noise for this app anyway. Everything is stored as UTC DateTime, which
    /// sorts correctly and reads cleanly in the database.
    /// </summary>
    private static void ApplyUtcDateTimes(ModelBuilder builder)
    {
        var offsetConverter = new ValueConverter<DateTimeOffset, DateTime>(
            value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
        );

        var nullableOffsetConverter = new ValueConverter<DateTimeOffset?, DateTime?>(
            value => value.HasValue ? value.Value.UtcDateTime : null,
            value => value.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                : null
        );

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(offsetConverter);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullableOffsetConverter);
                }
            }
        }
    }
}
