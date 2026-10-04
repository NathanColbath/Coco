using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<StoryBlockKind>))]
public enum StoryBlockKind
{
    Prose = 0,
    SceneBreak = 1,
    Instruction = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<StoryBlockOrigin>))]
public enum StoryBlockOrigin
{
    User = 0,
    Ai = 1,
}

/// <summary>
/// A node in the story's text tree. Blocks form a chain via <see cref="ParentId"/>;
/// retrying a generation creates a sibling rather than overwriting, so every
/// alternative remains navigable.
/// </summary>
public class StoryBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public Guid? ParentId { get; set; }

    public StoryBlock? Parent { get; set; }

    public StoryBlockKind Kind { get; set; } = StoryBlockKind.Prose;

    public StoryBlockOrigin Origin { get; set; } = StoryBlockOrigin.User;

    public string Text { get; set; } = string.Empty;

    public int TokenCount { get; set; }

    /// <summary>The generation that produced this block, when it came from the model.</summary>
    public Guid? GenerationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
