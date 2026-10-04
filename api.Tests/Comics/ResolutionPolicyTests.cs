using ComicStudio.Api.Comics;
using ComicStudio.Api.NovelAi.Image;

namespace ComicStudio.Api.Tests.Comics;

public class ResolutionPolicyTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.68)]
    [InlineData(1.47)]
    [InlineData(0.25)]
    public void Produces_legal_sizes(double aspect)
    {
        var (width, height) = ResolutionPolicy.ForAspect(aspect);

        Assert.Equal(0, width % ResolutionPolicy.SizeStep);
        Assert.Equal(0, height % ResolutionPolicy.SizeStep);
        Assert.True(width > 0 && height > 0);
    }

    [Fact]
    public void Stays_inside_the_free_generation_budget_by_default()
    {
        foreach (var aspect in new[] { 0.4, 0.7, 1.0, 1.4, 2.2 })
        {
            var (width, height) = ResolutionPolicy.ForAspect(aspect);

            Assert.True(
                (long)width * height <= ResolutionPolicy.FreePixelBudget,
                $"{width}x{height} exceeds the free budget for aspect {aspect}"
            );
        }
    }

    [Fact]
    public void Uses_most_of_the_budget()
    {
        var (width, height) = ResolutionPolicy.ForAspect(0.7);

        // Within one size step of the ceiling.
        Assert.True((long)width * height > ResolutionPolicy.FreePixelBudget * 0.85);
    }

    [Fact]
    public void Matches_the_requested_shape_closely()
    {
        var (width, height) = ResolutionPolicy.ForAspect(0.7);

        Assert.InRange((double)width / height, 0.62, 0.78);
    }

    [Fact]
    public void Paid_budget_produces_larger_images()
    {
        var free = ResolutionPolicy.ForAspect(0.7, ResolutionPolicy.FreePixelBudget);
        var paid = ResolutionPolicy.ForAspect(0.7, ResolutionPolicy.MaxPixelBudget);

        Assert.True((long)paid.Width * paid.Height > (long)free.Width * free.Height);
    }

    [Fact]
    public void Clamps_steps_for_free_generation()
    {
        Assert.Equal(28, ResolutionPolicy.StepsFor(allowPaidQuality: false, requestedSteps: 50));
        Assert.Equal(50, ResolutionPolicy.StepsFor(allowPaidQuality: true, requestedSteps: 50));
        Assert.Equal(20, ResolutionPolicy.StepsFor(allowPaidQuality: false, requestedSteps: 20));
    }

    [Fact]
    public void Free_generation_predicate_matches_novels_rules()
    {
        Assert.True(ResolutionPolicy.QualifiesForFreeGeneration(1024, 1024, 28));
        Assert.False(ResolutionPolicy.QualifiesForFreeGeneration(1024, 1024, 29));
        Assert.False(ResolutionPolicy.QualifiesForFreeGeneration(1472, 1472, 28));
    }

    [Fact]
    public void Panel_aspect_uses_page_dimensions()
    {
        // A wide panel on a tall page is still landscape.
        var aspect = ResolutionPolicy.AspectForPanel(0.9, 0.2, 1600, 2263);

        Assert.True(aspect > 2);
    }
}

public class PagePackerTests
{
    private static PlannedPanel Panel(string shot = "Medium", string importance = "normal", int dialogue = 0) =>
        new()
        {
            Shot = shot,
            Importance = importance,
            Action = "something happens",
            Dialogue = Enumerable
                .Range(0, dialogue)
                .Select(index => new PlannedDialogue { Text = $"line {index}" })
                .ToList(),
        };

    [Fact]
    public void Splash_shot_gets_its_own_page()
    {
        var pages = PagePacker.Pack([Panel(), Panel(shot: "Splash"), Panel()]);

        Assert.Equal(3, pages.Count);
        Assert.Equal("splash", pages[1].TemplateId);
        Assert.Single(pages[1].Panels);
    }

    [Fact]
    public void High_importance_wide_shot_gets_a_splash_page()
    {
        var pages = PagePacker.Pack([Panel(shot: "Wide", importance: "high")]);

        Assert.Single(pages);
        Assert.Equal("splash", pages[0].TemplateId);
    }

    [Fact]
    public void Never_strands_a_single_panel_on_a_page()
    {
        // Five ordinary panels: four plus one would orphan the fifth.
        var pages = PagePacker.Pack(Enumerable.Range(0, 5).Select(_ => Panel()).ToList());

        Assert.All(pages, page => Assert.True(page.Panels.Count >= 2));
        Assert.Equal(5, pages.Sum(page => page.Panels.Count));
    }

    [Fact]
    public void Every_panel_is_placed_exactly_once()
    {
        var panels = Enumerable.Range(0, 23).Select(_ => Panel()).ToList();

        var pages = PagePacker.Pack(panels);

        Assert.Equal(panels.Count, pages.Sum(page => page.Panels.Count));
        Assert.All(pages, page => Assert.True(page.Panels.Count <= 6));
    }

    [Fact]
    public void Chooses_dialogue_layouts_for_talky_pages()
    {
        var pages = PagePacker.Pack(
        [
            Panel(dialogue: 2),
            Panel(dialogue: 1),
            Panel(dialogue: 1),
        ]);

        Assert.Equal("strip-3", pages[0].TemplateId);
    }

    [Fact]
    public void Uses_the_four_panel_grid_for_a_full_page()
    {
        var pages = PagePacker.Pack(Enumerable.Range(0, 4).Select(_ => Panel()).ToList());

        Assert.Single(pages);
        Assert.Equal("grid-2x2", pages[0].TemplateId);
    }
}
