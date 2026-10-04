using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ImageAssetKind>))]
public enum ImageAssetKind
{
    Panel = 0,
    CharacterSheet = 1,
    Cover = 2,
    Upload = 3,
    PageRender = 4,
    Thumbnail = 5,

    /// <summary>Art for a whole page, drawn as one image.</summary>
    PageArt = 6,
}

/// <summary>
/// A stored image. Bytes live on disk under the artifacts path; the database
/// keeps the hash, dimensions and the NovelAI metadata that produced it.
/// </summary>
public class ImageAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public Guid? StoryId { get; set; }

    public Guid? ProjectId { get; set; }

    public ImageAssetKind Kind { get; set; }

    /// <summary>Path relative to the configured artifacts root.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public long Bytes { get; set; }

    public string MimeType { get; set; } = "image/png";

    /// <summary>The PNG comment NovelAI embeds, kept for reproducibility.</summary>
    public string? SourceMetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
