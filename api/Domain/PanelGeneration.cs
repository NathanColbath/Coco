using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ImageJobKind>))]
public enum ImageJobKind
{
    Panel = 0,
    CharacterSheet = 1,
    Cover = 2,
    Page = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter<PanelGenerationStatus>))]
public enum PanelGenerationStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>
/// One attempt at drawing something. Panels, character sheets and covers share
/// this record so progress, cost and history work the same way for all three.
/// </summary>
public class PanelGeneration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ImageJobKind Kind { get; set; } = ImageJobKind.Panel;

    /// <summary>Panel, sheet or cover id depending on <see cref="Kind"/>.</summary>
    public Guid TargetId { get; set; }

    /// <summary>Which kind of sheet to draw, when <see cref="Kind"/> is CharacterSheet.</summary>
    public CharacterSheetKind? SheetKind { get; set; }

    public Guid ProjectId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    /// <summary>The exact request body sent to NovelAI.</summary>
    public string RequestJson { get; set; } = "{}";

    public long? Seed { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int Steps { get; set; }

    public double Scale { get; set; }

    public bool IsHighResolution { get; set; }

    /// <summary>Anlas actually spent, measured as a subscription balance delta.</summary>
    public int? CostAnlas { get; set; }

    public bool WasFree { get; set; }

    public PanelGenerationStatus Status { get; set; } = PanelGenerationStatus.Queued;

    public Guid? ImageAssetId { get; set; }

    public ImageAsset? Image { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public long DurationMs { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<CurationStatus>))]
public enum CurationStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter<CurationStage>))]
public enum CurationStage
{
    CastDiscovery = 0,
    SceneSegmentation = 1,
    PanelBreakdown = 2,
    PagePacking = 3,
    PanelDirection = 4,
    Cover = 5,
    Done = 6,
}

/// <summary>
/// An audit record of one pass of the curator. The raw model output is kept so
/// a bad plan can be diagnosed (or replayed) rather than guessed at.
/// </summary>
public class CurationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public ComicProject? Project { get; set; }

    public string UserId { get; set; } = string.Empty;

    public CurationStage Stage { get; set; }

    public CurationStatus Status { get; set; } = CurationStatus.Queued;

    public string ModelId { get; set; } = string.Empty;

    public string? OptionsJson { get; set; }

    /// <summary>The curator's raw response, before parsing.</summary>
    public string? RawOutputJson { get; set; }

    /// <summary>Validated result of the pass.</summary>
    public string? ResultJson { get; set; }

    public int PromptTokens { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FinishedAt { get; set; }

    public long DurationMs { get; set; }
}

/// <summary>The cover art plan for a comic: key art plus the composed title.</summary>
public class CoverPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public ComicProject? Project { get; set; }

    public string? Prompt { get; set; }

    public string? NegativePrompt { get; set; }

    public Guid? ImageAssetId { get; set; }

    public long? Seed { get; set; }

    public string? ModelId { get; set; }

    public string TitleText { get; set; } = string.Empty;

    public string TitleFont { get; set; } = "Anton";

    public double TitleX { get; set; } = 0.5;

    public double TitleY { get; set; } = 0.12;

    public double TitleMaxWidth { get; set; } = 0.8;

    public string? ExtraText { get; set; }

    /// <summary>Composed cover with the title drawn on, ready for export.</summary>
    public Guid? RenderedImageId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
