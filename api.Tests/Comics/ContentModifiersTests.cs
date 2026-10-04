using ComicStudio.Api.Comics;
using ComicStudio.Api.Domain;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// A content rating is not a single tag. Above General the safety tags have to be
/// stripped, or a preset that lists "nsfw" in its negatives silently cancels the
/// setting — which is the failure mode that makes people think the option does
/// nothing.
/// </summary>
public class ContentModifiersTests
{
    private const string PresetNegatives =
        "color, watermark, lowres, bad anatomy, multiple views, panel border";

    [Fact]
    public void General_pushes_adult_content_away()
    {
        var negative = ContentModifiers.ApplyToNegative(PresetNegatives, ContentRating.General);

        Assert.Contains("nsfw", negative);
        Assert.Contains("explicit", negative);
        // The preset's own negatives survive.
        Assert.Contains("lowres", negative);
        Assert.Contains("multiple views", negative);
    }

    [Fact]
    public void General_appends_safety_tags_without_touching_the_preset()
    {
        var negative = ContentModifiers.ApplyToNegative(PresetNegatives, ContentRating.General);

        // The preset's list is untouched and the safety tags go on the end.
        Assert.StartsWith(PresetNegatives, negative);
        foreach (var tag in PresetNegatives.Split(", "))
        {
            Assert.Contains(tag, negative);
        }
    }

    [Fact]
    public void General_does_not_add_a_safety_tag_the_preset_already_lists()
    {
        // "NSFW" in a preset must not end up in the request twice.
        var negative = ContentModifiers.ApplyToNegative("NSFW, lowres", ContentRating.General);
        var tags = negative.Split(", ", StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(tags, tag => string.Equals(tag, "nsfw", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("lowres", negative);
    }

    [Fact]
    public void Adult_ratings_strip_only_the_blocking_tags()
    {
        const string preset =
            "color, watermark, lowres, bad anatomy, multiple views, panel border, nsfw, nude, sex, censored";

        var negative = ContentModifiers.ApplyToNegative(preset, ContentRating.Mature);

        foreach (
            var unrelated in new[]
            {
                "color",
                "watermark",
                "lowres",
                "bad anatomy",
                "multiple views",
                "panel border",
            }
        )
        {
            Assert.Contains(unrelated, negative);
        }

        foreach (var blocked in new[] { "nsfw", "nude", "sex" })
        {
            Assert.DoesNotContain(blocked, negative);
        }
    }

    [Fact]
    public void Adult_ratings_do_not_duplicate_a_censoring_tag_the_preset_already_lists()
    {
        var negative = ContentModifiers.ApplyToNegative("censored, lowres", ContentRating.Explicit);
        var tags = negative.Split(", ", StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(tags, tag => string.Equals(tag, "censored", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("lowres", negative);
    }

    [Fact]
    public void General_leaves_the_positive_alone()
    {
        var positive = ContentModifiers.ApplyToPositive("manga, garden at dusk", ContentRating.General);

        Assert.Equal("manga, garden at dusk", positive);
    }

    [Fact]
    public void Adult_ratings_strip_safety_tags_that_would_cancel_them()
    {
        var negative = ContentModifiers.ApplyToNegative(
            "nsfw, nude, lowres, bad anatomy",
            ContentRating.Explicit
        );

        Assert.DoesNotContain("nsfw", negative);
        Assert.DoesNotContain("nude", negative);
        // Unrelated negatives are kept.
        Assert.Contains("lowres", negative);
        Assert.Contains("bad anatomy", negative);
    }

    [Fact]
    public void Adult_ratings_exclude_censoring()
    {
        var negative = ContentModifiers.ApplyToNegative(PresetNegatives, ContentRating.Mature);

        Assert.Contains("censored", negative);
        Assert.Contains("mosaic censoring", negative);
    }

    [Fact]
    public void Adult_ratings_ask_for_an_uncensored_result()
    {
        var positive = ContentModifiers.ApplyToPositive("manga, two figures", ContentRating.Explicit);

        Assert.Contains("uncensored", positive);
        Assert.StartsWith("manga, two figures", positive);
    }

    [Theory]
    [InlineData(ContentRating.General, false)]
    [InlineData(ContentRating.Mature, true)]
    [InlineData(ContentRating.Explicit, true)]
    public void Flags_a_curated_model_on_an_adult_project(ContentRating rating, bool expected)
    {
        // Curated models are safety tuned, so they fight an adult rating.
        Assert.Equal(
            expected,
            ContentModifiers.ModelFightsRating("nai-diffusion-5-curated", rating)
        );
    }

    [Fact]
    public void Full_models_are_fine_at_any_rating()
    {
        Assert.False(
            ContentModifiers.ModelFightsRating("nai-diffusion-5-full", ContentRating.Explicit)
        );
    }

    [Fact]
    public void The_curator_is_told_not_to_soften_an_adult_scene()
    {
        Assert.Contains("Do not sanitise", ContentModifiers.CuratorNote(ContentRating.Explicit)!);
        Assert.Contains("general audience", ContentModifiers.CuratorNote(ContentRating.General)!);
    }
}
