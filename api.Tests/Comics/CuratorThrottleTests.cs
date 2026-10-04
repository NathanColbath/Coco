using ComicStudio.Api.Comics;
using ComicStudio.Api.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// NovelAI rate limits text generation. A throttled curator call must back off
/// and retry: before this existed, one 429 discarded the entire plan.
/// </summary>
public class CuratorThrottleTests
{
    private static CuratorThrottle Throttle(int concurrent = 2) =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Curator:MaxConcurrentTextRequests"] = concurrent.ToString(),
                        ["Curator:RetryBaseDelayMs"] = "1",
                    }
                )
                .Build(),
            NullLogger<CuratorThrottle>.Instance
        );

    [Fact]
    public async Task Retries_a_rate_limited_call_until_it_succeeds()
    {
        var attempts = 0;

        var result = await Throttle().RunAsync(
            () =>
            {
                attempts++;
                return attempts < 3
                    ? throw new NovelAiTextException("rate limited", 429)
                    : Task.FromResult("planned");
            },
            CancellationToken.None
        );

        Assert.Equal("planned", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Retries_server_errors_too()
    {
        var attempts = 0;

        await Throttle()
            .RunAsync(
                () =>
                {
                    attempts++;
                    return attempts < 2
                        ? throw new NovelAiTextException("bad gateway", 502)
                        : Task.FromResult("ok");
                },
                CancellationToken.None
            );

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Gives_up_after_the_retry_budget()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<NovelAiTextException>(() =>
            Throttle()
                .RunAsync(
                    () =>
                    {
                        attempts++;
                        throw new NovelAiTextException("always throttled", 429);
                    },
                    CancellationToken.None
                )
        );

        Assert.Equal(5, attempts); // the call plus four retries
    }

    [Fact]
    public async Task Never_retries_a_rejected_token()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<NovelAiTextException>(() =>
            Throttle()
                .RunAsync(
                    () =>
                    {
                        attempts++;
                        throw new NovelAiTextException("bad token", 401);
                    },
                    CancellationToken.None
                )
        );

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Caps_how_many_calls_run_at_once()
    {
        var throttle = Throttle(concurrent: 2);
        var inFlight = 0;
        var peak = 0;

        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(_ =>
                    throttle.RunAsync(
                        async () =>
                        {
                            var now = Interlocked.Increment(ref inFlight);
                            lock (throttle)
                            {
                                peak = Math.Max(peak, now);
                            }

                            await Task.Delay(5);
                            Interlocked.Decrement(ref inFlight);
                            return "ok";
                        },
                        CancellationToken.None
                    )
                )
        );

        Assert.True(peak <= 2, $"expected at most 2 concurrent calls, saw {peak}");
    }
}
