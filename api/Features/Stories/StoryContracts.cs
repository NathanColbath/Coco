using ComicStudio.Api.Domain;
using ComicStudio.Api.Storytelling;

namespace ComicStudio.Api.Features.Stories;

public record StorySummaryDto(
    Guid Id,
    string Title,
    string? Genre,
    string? Tags,
    string? TextModelId,
    int WordCount,
    int BlockCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record StoryBlockDto(
    Guid Id,
    Guid? ParentId,
    StoryBlockKind Kind,
    StoryBlockOrigin Origin,
    string Text,
    int TokenCount,
    Guid? GenerationId,
    DateTimeOffset CreatedAt
)
{
    public static StoryBlockDto From(StoryBlock block) =>
        new(
            block.Id,
            block.ParentId,
            block.Kind,
            block.Origin,
            block.Text,
            block.TokenCount,
            block.GenerationId,
            block.CreatedAt
        );
}

public record StoryDetailDto(
    Guid Id,
    string Title,
    string? Synopsis,
    string? Genre,
    string? Tags,
    string? TextModelId,
    StoryContextSettings Context,
    TextSamplerSettings Sampler,
    Guid? CursorBlockId,
    IReadOnlyList<StoryBlockDto> Blocks,
    IReadOnlyList<LorebookEntryDto> Lorebook,
    string ActiveText,
    int WordCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record LorebookEntryDto(
    Guid Id,
    string DisplayName,
    IReadOnlyList<string> Keys,
    string KeysRaw,
    string Content,
    string? Category,
    bool Enabled,
    bool AlwaysOn,
    int InsertionOrder,
    int InsertionPosition,
    string Prefix,
    string Suffix,
    decimal ReservedTokens,
    LorebookTrimDirection TrimDirection,
    bool KeyRelative
)
{
    public static LorebookEntryDto From(LorebookEntry entry) =>
        new(
            entry.Id,
            entry.DisplayName,
            LorebookMatcher.ParseKeys(entry.KeysRaw),
            entry.KeysRaw,
            entry.Content,
            entry.Category,
            entry.Enabled,
            entry.AlwaysOn,
            entry.InsertionOrder,
            entry.InsertionPosition,
            entry.Prefix,
            entry.Suffix,
            entry.ReservedTokens,
            entry.TrimDirection,
            entry.KeyRelative
        );
}

public record CreateStoryRequest(
    string Title,
    string? Synopsis = null,
    string? Genre = null,
    string? Tags = null,
    string? TextModelId = null
);

public record UpdateStoryRequest(
    string Title,
    string? Synopsis,
    string? Genre,
    string? Tags,
    string? TextModelId,
    StoryContextSettings? Context,
    TextSamplerSettings? Sampler
);

public record AppendBlockRequest(string Text, StoryBlockKind Kind = StoryBlockKind.Prose);

public record UpdateBlockRequest(string Text);

public record SetCursorRequest(Guid? BlockId);

public record GenerateRequest(string? Instruction = null, int? MaxTokens = null, string? ModelId = null);

public record GenerationAcceptedDto(Guid GenerationId, string ModelId, string Transport);

public record GenerationDto(
    Guid Id,
    string ModelId,
    string Transport,
    StoryGenerationStatus Status,
    string? Output,
    string? Error,
    int PromptTokens,
    int OutputTokens,
    Guid? ResultBlockId,
    DateTimeOffset CreatedAt,
    long DurationMs,
    ContextTrace? Trace
);

public record ContextPreviewDto(ContextTrace Trace, string Prompt);

public record CreateLorebookEntryRequest(
    string DisplayName,
    string Keys,
    string Content,
    string? Category = null,
    bool AlwaysOn = false,
    int InsertionOrder = 100,
    int InsertionPosition = 0,
    string Prefix = "",
    string Suffix = "",
    decimal ReservedTokens = 0,
    LorebookTrimDirection TrimDirection = LorebookTrimDirection.DoNotTrim,
    bool KeyRelative = false,
    bool Enabled = true
);
