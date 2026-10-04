namespace ComicStudio.Api.Domain;

public class Story
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public AppUser? User { get; set; }

    public string Title { get; set; } = "Untitled story";

    public string? Synopsis { get; set; }

    public string? Genre { get; set; }

    /// <summary>Free-form steering tags, rendered into the ATTG line.</summary>
    public string? Tags { get; set; }

    /// <summary>Registry id; null means "use the account's preferred text model".</summary>
    public string? TextModelId { get; set; }

    public StoryContextSettings Context { get; set; } = new();

    public TextSamplerSettings Sampler { get; set; } = new();

    /// <summary>Leaf of the currently selected branch.</summary>
    public Guid? CursorBlockId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<StoryBlock> Blocks { get; set; } = [];

    public List<LorebookEntry> LorebookEntries { get; set; } = [];

    public List<StoryGeneration> Generations { get; set; } = [];
}
