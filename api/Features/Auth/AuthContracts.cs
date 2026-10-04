using ComicStudio.Api.Domain;

namespace ComicStudio.Api.Features.Auth;

public record RegisterRequest(string Email, string Password, string? DisplayName);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public record UserDto(
    string Id,
    string Email,
    string DisplayName,
    bool NovelAiConnected,
    string? NovelAiTokenLast4,
    NovelAiTier NovelAiTier,
    bool NovelAiSubscriptionActive,
    int? AnlasBalance,
    DateTimeOffset? NovelAiCheckedAt,
    string? PreferredTextModelId,
    string? PreferredImageModelId
)
{
    public static UserDto From(AppUser user) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            !string.IsNullOrEmpty(user.NovelAiTokenCipher),
            user.NovelAiTokenLast4,
            user.NovelAiTier,
            user.NovelAiSubscriptionActive,
            user.AnlasBalance,
            user.NovelAiCheckedAt,
            user.PreferredTextModelId,
            user.PreferredImageModelId
        );
}
