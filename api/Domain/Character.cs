using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<CharacterRole>))]
public enum CharacterRole
{
    Protagonist = 0,
    Antagonist = 1,
    Supporting = 2,
    Background = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter<CharacterSheetKind>))]
public enum CharacterSheetKind
{
    Portrait = 0,
    FullBody = 1,
    ExpressionSheet = 2,
}

/// <summary>
/// A person in the story bible. Created by hand or proposed by cast discovery
/// when a story is imported, and shared by every comic adapted from that story.
/// </summary>
public class Character
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Alternative names and nicknames, one per line. Used for cast matching.</summary>
    public string? Aliases { get; set; }

    public CharacterRole Role { get; set; } = CharacterRole.Supporting;

    public string? Bio { get; set; }

    public string? Personality { get; set; }

    public string? SpeechStyle { get; set; }

    public CharacterVisual Visual { get; set; } = new();

    /// <summary>Set once a sheet is approved; panels reference every locked sheet.</summary>
    public bool IsLocked { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<CharacterSheetImage> Sheets { get; set; } = [];
}

/// <summary>
/// How this character is described to the image model. <see cref="PromptTags"/>
/// is the canonical fragment pasted into every character prompt, which is the
/// cheapest and most reliable consistency lever.
/// </summary>
public class CharacterVisual
{
    /// <summary>Danbooru-style tags, e.g. "1girl, long black hair, green eyes, hair ribbon".</summary>
    public string? PromptTags { get; set; }

    /// <summary>Default outfit tags, e.g. "white blouse, brown corset, long skirt".</summary>
    public string? OutfitTags { get; set; }

    /// <summary>Per-character negatives to stop traits leaking between cast members.</summary>
    public string? NegativeTags { get; set; }

    public string? HairColor { get; set; }

    public string? EyeColor { get; set; }

    public string? BodyNotes { get; set; }

    public string? Notes { get; set; }
}

/// <summary>A generated reference image for a character, used as a Precise Reference.</summary>
public class CharacterSheetImage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CharacterId { get; set; }

    public Character? Character { get; set; }

    public Guid ImageAssetId { get; set; }

    public ImageAsset? Image { get; set; }

    public CharacterSheetKind Kind { get; set; }

    public string? PromptUsed { get; set; }

    public long? Seed { get; set; }

    public string? ModelId { get; set; }

    /// <summary>Approved sheets are what panels actually reference.</summary>
    public bool IsLocked { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
