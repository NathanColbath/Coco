using ComicStudio.Api.Comics;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// Curation cost is almost entirely the model's output, so the panel budget is
/// sized to the scene instead of always asking for a full page-set.
/// </summary>
public class CuratorSizingTests
{
    [Theory]
    [InlineData(0, 8)]
    [InlineData(120, 4)]
    [InlineData(550, 5)]
    [InlineData(1100, 10)]
    [InlineData(4000, 16)]
    public void Sizes_the_panel_budget_to_the_scene(int sceneWords, int expected)
    {
        Assert.Equal(expected, Curator.PanelsFor(sceneWords));
    }

    [Fact]
    public void Never_asks_for_a_page_of_one_or_a_whole_chapter()
    {
        Assert.Equal(4, Curator.PanelsFor(10));
        Assert.Equal(16, Curator.PanelsFor(100_000));
    }

    /// <summary>
    /// Page scripting is the stage that truncates: a scene's whole page count
    /// used to go out in one call and overflow the output budget mid-JSON.
    /// Batches are sized from a conservative per-page token cost instead.
    /// </summary>
    [Theory]
    [InlineData(2048, 900, 2)] // the transport's current clamp: two pages fit
    [InlineData(2560, 900, 2)]
    [InlineData(4096, 900, 4)] // a raised clamp fits four pages
    [InlineData(900, 900, 1)] // exactly one page fits
    [InlineData(100, 900, 1)] // a tiny budget still asks for a page
    [InlineData(0, 900, 1)]
    [InlineData(100_000, 1, 4)] // never more than four pages per call
    public void Sizes_a_page_batch_to_the_output_budget(
        int outputCeiling,
        int tokensPerPage,
        int expected
    )
    {
        Assert.Equal(expected, Curator.ScriptBatchSize(outputCeiling, tokensPerPage));
    }

    [Fact]
    public void Defaults_to_two_pages_per_call_for_the_transport_clamp()
    {
        Assert.Equal(2, Curator.ScriptBatchSize());
    }
}
