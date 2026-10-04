using ComicStudio.Api.Domain;

namespace ComicStudio.Api.Features.Comics;

public record ComicSummaryDto(
    Guid Id,
    string Title,
    Guid StoryId,
    string StoryTitle,
    ComicStatus Status,
    int PageCount,
    int GeneratedPageCount,
    Guid? CoverImageId,
    DateTimeOffset UpdatedAt
);

public record DialogueLineDto(
    Guid Id,
    int Index,
    DialogueKind Kind,
    Guid? SpeakerCharacterId,
    string? SpeakerName,
    string Text,
    string? Position
);

public record PageDto(
    Guid Id,
    int Index,
    string LayoutTemplateId,
    string LayoutTemplateName,
    PageStatus Status,
    string? SceneTitle,
    string? Script,
    string? Continuity,
    string? PositivePrompt,
    string? NegativePrompt,
    long? Seed,
    Guid? PageImageId,
    Guid? RenderImageId,
    int AttemptCount,
    int TargetWidth,
    int TargetHeight,
    IReadOnlyList<DialogueLineDto> Dialogue,
    string? EnvironmentName,
    string? ModelUsed,
    string? ModelOverride
);

public record CharacterSheetDto(
    Guid Id,
    CharacterSheetKind Kind,
    Guid ImageAssetId,
    bool IsLocked,
    long? Seed,
    string? ModelId,
    DateTimeOffset CreatedAt
);

public record CharacterDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Aliases,
    CharacterRole Role,
    string? Bio,
    string? Personality,
    string? SpeechStyle,
    CharacterVisual Visual,
    bool IsLocked,
    bool HasLockedSheet,
    IReadOnlyList<CharacterSheetDto> Sheets
);

public record CoverDto(
    Guid Id,
    string? Prompt,
    string? NegativePrompt,
    Guid? ImageAssetId,
    Guid? RenderedImageId,
    string TitleText,
    string TitleFont,
    double TitleX,
    double TitleY,
    double TitleMaxWidth
);

public record ComicDetailDto(
    Guid Id,
    string Title,
    string? Premise,
    Guid StoryId,
    string StoryTitle,
    ReadingDirection Direction,
    ColorMode ColorMode,
    ContentRating Rating,
    ComicStatus Status,
    ComicStylePreset Style,
    int PageWidth,
    int PageHeight,
    DialogueAdaptation DialogueAdaptation,
    Guid? StyleAnchorImageId,
    bool IsLegacy,
    Guid? CoverImageId,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PageDto> Pages,
    IReadOnlyList<CharacterDto> Cast,
    CoverDto? Cover
);

public record CreateComicRequest(Guid StoryId, string? Title, string? Premise);

public record UpdateComicRequest(
    string Title,
    string? Premise,
    ReadingDirection Direction,
    ColorMode ColorMode,
    ContentRating Rating,
    DialogueAdaptation DialogueAdaptation,
    int PageWidth,
    int PageHeight,
    ComicStylePreset? Style,
    Guid? StyleAnchorImageId = null
);

public record DialogueRequest(
    DialogueKind Kind,
    Guid? SpeakerCharacterId,
    string Text,
    string? Position
);

/// <summary>Hand-editing of a page's script, ledger and lettering.</summary>
public record UpdatePageRequest(
    string? Script,
    string? Continuity,
    string? SceneTitle,
    List<DialogueRequest>? Dialogue,
    string? ModelOverride = null
);

public record GeneratePageRequest(bool HighResolution = true, long? Seed = null);

public record BatchGenerateRequest(bool HighResolution = true, bool IncludeDrawn = false);

public record BatchGenerateAcceptedDto(int Queued, int Skipped);

public record GenerateSheetRequest(CharacterSheetKind Kind, string? ExtraTags = null, long? Seed = null);

public record UpsertCharacterRequest(
    string Name,
    string? Aliases,
    CharacterRole Role,
    string? Bio,
    string? Personality,
    string? SpeechStyle,
    CharacterVisual? Visual
);

public record PageAttemptDto(
    Guid Id,
    PanelGenerationStatus Status,
    string ModelId,
    int Width,
    int Height,
    int Steps,
    long? Seed,
    int? CostAnlas,
    bool WasFree,
    Guid? ImageAssetId,
    string? Error,
    long DurationMs,
    DateTimeOffset CreatedAt
);

public record LayoutTemplateDto(
    string Id,
    string Name,
    LayoutCategory Category,
    int PanelCount,
    double Gutter,
    double Margin,
    double BorderThickness,
    string? Description,
    IReadOnlyList<LayoutPanelSlot> Slots
);

public record UpdateCoverRequest(string? Prompt, string? Negative, string TitleText, string TitleFont, double TitleX, double TitleY, double TitleMaxWidth);

public record GenerationQueuedDto(Guid GenerationId, Guid PageId);

/// <summary>What a delete would destroy, so nothing is removed by surprise.</summary>
public record DeletionPreviewDto(
    int Pages,
    int Images,
    long Bytes,
    bool HasCover,
    int CurationRuns,
    IReadOnlyList<DeleteImpactDto> Cascades
);

public record DeleteImpactDto(string Kind, string Title, int Pages, int Images, long Bytes);

public record DeleteRequest(bool Confirmed = false);
