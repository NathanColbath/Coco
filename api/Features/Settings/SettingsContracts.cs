using ComicStudio.Api.Domain;

namespace ComicStudio.Api.Features.Settings;

public record ConnectNovelAiRequest(string Token);

public record NovelAiConnectionDto(
    bool Connected,
    string? TokenLast4,
    NovelAiTier Tier,
    bool SubscriptionActive,
    int? AnlasBalance,
    DateTimeOffset? CheckedAt,
    string? Message
);

public record UserPreferencesRequest(string? PreferredTextModelId, string? PreferredImageModelId);
