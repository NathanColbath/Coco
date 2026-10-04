using ComicStudio.Api.Domain;
using ComicStudio.Api.Storytelling;

namespace ComicStudio.Api.Tests.Storytelling;

public class LorebookMatcherTests
{
    [Fact]
    public void Parses_keys_from_lines_and_commas()
    {
        var keys = LorebookMatcher.ParseKeys("Ana\nbanana, Clockwork\n\n  garden ");

        Assert.Equal(["Ana", "banana", "Clockwork", "garden"], keys);
    }

    [Fact]
    public void Word_like_keys_respect_boundaries()
    {
        var entry = TestData.Entry("Ana", "Ana is the gardener.", keys: "Ana");
        var selected = LorebookMatcher.Select([entry], "The banana split was lovely.");

        Assert.Empty(selected);
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var entry = TestData.Entry("Ana", "Ana is the gardener.", keys: "ana");
        var selected = LorebookMatcher.Select([entry], "Then ANA arrived.");

        Assert.Single(selected);
    }

    [Fact]
    public void Punctuation_keys_match_as_substrings()
    {
        var entry = TestData.Entry("Sigil", "The sigil burns.", keys: "***");
        var selected = LorebookMatcher.Select([entry], "Some text\n***\nmore text");

        Assert.Single(selected);
    }

    [Fact]
    public void Always_on_entries_activate_and_disabled_entries_are_ignored()
    {
        var alwaysOn = TestData.Entry("World", "Rules of the world.", alwaysOn: true);
        var disabled = TestData.Entry("Hidden", "Secret.", alwaysOn: true, enabled: false);

        var selected = LorebookMatcher.Select([alwaysOn, disabled], "nothing relevant");

        Assert.Equal(["World"], selected.Select(entry => entry.DisplayName));
    }

    [Fact]
    public void Higher_insertion_order_wins_ties()
    {
        var low = TestData.Entry("Low", "low", keys: "garden", insertionOrder: 10);
        var high = TestData.Entry("High", "high", keys: "garden", insertionOrder: 900);

        var selected = LorebookMatcher.Select([low, high], "the garden gate");

        Assert.Equal(["High", "Low"], selected.Select(entry => entry.DisplayName));
    }

    [Fact]
    public void Finds_the_last_match_for_key_relative_insertion()
    {
        var entry = TestData.Entry("Lantern", "The lantern hums.", keys: "lantern");

        var index = LorebookMatcher.LastMatchIndex(
            "a lantern here, and another lantern there",
            entry
        );

        Assert.Equal(28, index);
    }
}
