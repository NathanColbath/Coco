namespace ComicStudio.Api.Domain;

/// <summary>
/// A run of consecutive pages that share one place and one beat of the story.
/// A scene is what gives a page its context: where it is, what time it is, and
/// what state the story enters and leaves with.
/// </summary>
public class Scene
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public int Index { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public Guid? EnvironmentId { get; set; }

    /// <summary>Set when the scene ends by relocating somewhere else.</summary>
    public Guid? MovesToEnvironmentId { get; set; }

    public string? TimeOfDay { get; set; }

    public string? Mood { get; set; }

    /// <summary>The dramatic job of this scene.</summary>
    public string? Purpose { get; set; }

    /// <summary>State entering the scene.</summary>
    public string? Enters { get; set; }

    /// <summary>State leaving the scene.</summary>
    public string? Leaves { get; set; }

    public int PageStart { get; set; }

    public int PageCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
