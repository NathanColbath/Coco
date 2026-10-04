using Microsoft.AspNetCore.Identity;

namespace ComicStudio.Api.Domain;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Data Protection encrypted NovelAI persistent token. Never returned to clients.</summary>
    public string? NovelAiTokenCipher { get; set; }

    public string? NovelAiTokenLast4 { get; set; }

    public NovelAiTier NovelAiTier { get; set; } = NovelAiTier.Unknown;

    public int? AnlasBalance { get; set; }

    public DateTimeOffset? NovelAiCheckedAt { get; set; }

    public bool NovelAiSubscriptionActive { get; set; }

    public string? PreferredTextModelId { get; set; }

    public string? PreferredImageModelId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
