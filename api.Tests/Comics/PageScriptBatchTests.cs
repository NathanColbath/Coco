using ComicStudio.Api.Comics;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// The batch scheduler is what replaced "ask for a whole scene, repaint the
/// same request when it truncates". These tests pin the split arithmetic, the
/// shrink-on-truncation retry, and the rule that a failed batch is reported
/// instead of quietly dropping its pages.
/// </summary>
public class PageScriptBatchTests
{
    private static PageScriptOutcome Pages(params int[] indexes) =>
        new(
            indexes
                .Select(index => new ScriptedPage { Index = index, Prompt = $"page {index}" })
                .ToList(),
            null,
            new StructuredStats(100, 0, 1, false)
        );

    private static PageScriptOutcome Truncated() =>
        new(
            [],
            "the response was cut off before its JSON closed",
            new StructuredStats(2048, 0, 1, true)
        );

    private static PageScriptOutcome ServerFailure(string error) =>
        new([], error, new StructuredStats(0, 0, 1, false));

    private static PageScriptOutcome ForBatch(PageScriptBatch batch) =>
        Pages(
            Enumerable
                .Range(0, batch.Count)
                .Select(offset => batch.FirstPage + offset)
                .ToArray()
        );

    private static string Key(PageScriptBatch batch) => $"{batch.FirstPage}:{batch.Count}";

    [Fact]
    public async Task Splits_a_scene_into_batches_that_cover_every_page()
    {
        var asked = new List<string>();

        var run = await Curator.ScriptBatchesAsync(
            firstPage: 1,
            pageCount: 5,
            initialBatchSize: 2,
            request: (batch, _) =>
            {
                asked.Add(Key(batch));
                return Task.FromResult(ForBatch(batch));
            },
            onSuccess: (_, _) => Task.CompletedTask,
            onFailure: (_, _) => Task.CompletedTask,
            CancellationToken.None
        );

        Assert.Equal(["1:2", "3:2", "5:1"], asked);
        Assert.Equal(3, run.Batches);
        Assert.Equal(5, run.PagesRequested);
        Assert.Equal(5, run.PagesReturned);
        Assert.Equal(0, run.FailedBatches);
        Assert.Equal(0, run.MissingPages);
        Assert.Equal(2, run.BatchSize);
    }

    [Fact]
    public async Task Shrinks_a_truncated_batch_instead_of_resending_it()
    {
        var asked = new List<string>();
        var firstAttempt = true;

        var run = await Curator.ScriptBatchesAsync(
            firstPage: 1,
            pageCount: 3,
            initialBatchSize: 2,
            request: (batch, _) =>
            {
                asked.Add(Key(batch));

                if (firstAttempt && batch.Count == 2)
                {
                    // The two-page request overflowed the output budget; the
                    // scheduler must re-ask for one page, not repeat the pair.
                    firstAttempt = false;
                    return Task.FromResult(Truncated());
                }

                return Task.FromResult(ForBatch(batch));
            },
            onSuccess: (_, _) => Task.CompletedTask,
            onFailure: (_, _) => Task.CompletedTask,
            CancellationToken.None
        );

        Assert.Equal(["1:2", "1:1", "2:1", "3:1"], asked);
        Assert.Equal(0, run.FailedBatches);
        Assert.Equal(3, run.PagesReturned);
        Assert.Equal(1, run.BatchSize);
    }

    [Fact]
    public async Task A_failed_batch_is_reported_and_its_pages_count_as_missing()
    {
        var failures = new List<string>();

        var run = await Curator.ScriptBatchesAsync(
            firstPage: 3,
            pageCount: 2,
            initialBatchSize: 2,
            request: (_, _) => Task.FromResult(Truncated()),
            onSuccess: (_, _) => Task.CompletedTask,
            onFailure: (batch, outcome) =>
            {
                failures.Add($"{Key(batch)}:{outcome.Truncated}");
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        // Two pages truncate, shrink to one and truncate again: that batch is
        // given up on. The remaining page fails on its own.
        Assert.Equal(["3:1:True", "4:1:True"], failures);
        Assert.Equal(2, run.FailedBatches);
        Assert.Equal(2, run.MissingPages);
        Assert.Equal(0, run.PagesReturned);
    }

    [Fact]
    public async Task A_failure_advances_past_its_pages_so_later_batches_keep_their_ranges()
    {
        var asked = new List<string>();

        var run = await Curator.ScriptBatchesAsync(
            firstPage: 1,
            pageCount: 4,
            initialBatchSize: 2,
            request: (batch, _) =>
            {
                asked.Add(Key(batch));
                return Task.FromResult(
                    batch.FirstPage == 1
                        ? ServerFailure("NovelAI rejected the request.")
                        : ForBatch(batch)
                );
            },
            onSuccess: (_, _) => Task.CompletedTask,
            onFailure: (_, _) => Task.CompletedTask,
            CancellationToken.None
        );

        // Pages 1-2 failed; the next batch still starts at page 3.
        Assert.Equal(["1:2", "3:2"], asked);
        Assert.Equal(2, run.PagesReturned);
        Assert.Equal(1, run.FailedBatches);
        Assert.Equal(2, run.MissingPages);
        Assert.Equal(4, run.PagesRequested);
    }

    [Fact]
    public async Task A_success_that_returns_shorter_than_asked_counts_the_gap_as_missing()
    {
        var run = await Curator.ScriptBatchesAsync(
            firstPage: 1,
            pageCount: 2,
            initialBatchSize: 2,
            request: (batch, _) => Task.FromResult(Pages(batch.FirstPage)),
            onSuccess: (_, _) => Task.CompletedTask,
            onFailure: (_, _) => Task.CompletedTask,
            CancellationToken.None
        );

        Assert.Equal(1, run.PagesReturned);
        Assert.Equal(1, run.MissingPages);
        Assert.Equal(0, run.FailedBatches);
    }
}
