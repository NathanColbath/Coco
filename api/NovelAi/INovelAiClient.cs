using ComicStudio.Api.Domain;

namespace ComicStudio.Api.NovelAi;

/// <summary>Normalised view of a NovelAI account, derived from /user/subscription.</summary>
public record NovelAiAccount(
    NovelAiTier Tier,
    bool Active,
    DateTimeOffset? ExpiresAt,
    int? AnlasBalance,
    int? TrainingStepsLeft
);

public enum NovelAiProbeStatus
{
    Ok,
    Unauthorized,
    Unreachable,
    InvalidResponse,
}

public record NovelAiProbeResult(
    NovelAiProbeStatus Status,
    NovelAiAccount? Account = null,
    string? Message = null,
    int? StatusCode = null
);

public interface INovelAiClient
{
    /// <summary>
    /// Validates a persistent token and returns the account summary.
    /// A token is only ever held in memory for the duration of the call.
    /// </summary>
    Task<NovelAiProbeResult> GetAccountAsync(string token, CancellationToken cancellationToken = default);
}
