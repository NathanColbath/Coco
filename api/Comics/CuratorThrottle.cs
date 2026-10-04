using ComicStudio.Api.Text;

namespace ComicStudio.Api.Comics;

/// <summary>
/// Caps how many curator calls are in flight and absorbs NovelAI's rate limiting.
///
/// Text generation is rate limited per account, so planning every scene at once
/// earns a 429 rather than going faster. This holds a small number of concurrent
/// requests, retries transient failures with exponential backoff, and only gives
/// up when NovelAI keeps refusing.
/// </summary>
public class CuratorThrottle(IConfiguration configuration, ILogger<CuratorThrottle> logger)
{
    private const int MaxRetries = 4;

    private readonly SemaphoreSlim gate = new(
        Math.Clamp(configuration.GetValue("Curator:MaxConcurrentTextRequests", 2), 1, 8)
    );

    /// <summary>Configurable so tests do not wait out real backoff delays.</summary>
    private readonly int baseDelayMs = Math.Max(1, configuration.GetValue("Curator:RetryBaseDelayMs", 600));

    public int MaxConcurrent => gate.CurrentCount;

    public async Task<string> RunAsync(Func<Task<string>> call, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await call();
            }
            catch (NovelAiTextException exception)
                when (IsTransient(exception) && attempt < MaxRetries)
            {
                logger.LogWarning(
                    "NovelAI throttled a curator call ({Status}); retrying ({Attempt}/{Max}).",
                    exception.StatusCode,
                    attempt + 1,
                    MaxRetries
                );
            }
            finally
            {
                // Released before backing off, so a waiting call uses the slot.
                gate.Release();
            }

            await Task.Delay(Backoff(attempt), cancellationToken);
        }
    }

    private static bool IsTransient(NovelAiTextException exception) =>
        exception.StatusCode is 429 or >= 500;

    private TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(
            (baseDelayMs * Math.Pow(2, attempt)) + Random.Shared.Next(0, baseDelayMs / 2 + 1)
        );
}
