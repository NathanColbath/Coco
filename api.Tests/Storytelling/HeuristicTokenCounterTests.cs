using ComicStudio.Api.Storytelling;

namespace ComicStudio.Api.Tests.Storytelling;

public class HeuristicTokenCounterTests
{
    private readonly HeuristicTokenCounter counter = new();

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void Returns_zero_for_empty_input(string? text, int expected) =>
        Assert.Equal(expected, counter.Count(text));

    [Fact]
    public void Counts_words_and_punctuation()
    {
        Assert.Equal(3, counter.Count("hello"));
        Assert.Equal(5, counter.Count("hello world"));
        Assert.Equal(7, counter.Count("hello, world!"));
    }

    [Fact]
    public void Grows_with_length()
    {
        var shortText = "The lantern flickered.";
        var longText = string.Concat(Enumerable.Repeat(shortText + " ", 20));

        Assert.True(counter.Count(longText) > counter.Count(shortText) * 10);
    }

    [Fact]
    public void Long_words_cost_more_than_short_ones()
    {
        Assert.True(counter.Count("incomprehensibility") > counter.Count("cat"));
    }
}
