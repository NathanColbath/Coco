using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<EnvironmentKind>))]
public enum EnvironmentKind
{
    Exterior = 0,
    Interior = 1,
    Landscape = 2,
    Vehicle = 3,
    Abstract = 4,
}

/// <summary>
/// A place in the story bible. Created by hand or proposed by scenery discovery
/// when a story is imported, and shared by every comic adapted from that story.
/// </summary>
public class Environment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Alternative names, one per line. Used for location matching.</summary>
    public string? Aliases { get; set; }

    public EnvironmentKind Kind { get; set; } = EnvironmentKind.Exterior;

    /// <summary>Danbooru-style tags describing the place.</summary>
    public string? VisualTags { get; set; }

    /// <summary>Time of day, weather and light sources.</summary>
    public string? Lighting { get; set; }

    /// <summary>Black-and-white value guidance.</summary>
    public string? PaletteNotes { get; set; }

    /// <summary>Recurring objects, one per line.</summary>
    public string? Features { get; set; }

    public string? Notes { get; set; }

    /// <summary>Page index where the place first appears, when discovered during curation.</summary>
    public int? FirstSeenPage { get; set; }

    /// <summary>The locked establishing image.</summary>
    public Guid? PlateImageId { get; set; }

    /// <summary>Set once a plate is approved; pages reference the locked plate.</summary>
    public bool IsLocked { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<EnvironmentPlate> Plates { get; set; } = [];
}

/// <summary>A generated establishing image for a location, used as a Precise Reference.</summary>
public class EnvironmentPlate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EnvironmentId { get; set; }

    public Environment? Environment { get; set; }

    public Guid ImageAssetId { get; set; }

    public ImageAsset? Image { get; set; }

    public string? PromptUsed { get; set; }

    public long? Seed { get; set; }

    public string? ModelId { get; set; }

    /// <summary>Approved plates are what pages actually reference.</summary>
    public bool IsLocked { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
