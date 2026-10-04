using ComicStudio.Api.Comics;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// The curator prompts are the contract with the text model. Wording is allowed
/// to move, but dropping an input is silent: the model simply stops seeing the
/// style spec, the environment canon or the ledger sections it must fill in.
/// These tests pin the structure of each prompt, not the prose.
/// </summary>
public class CuratorPromptsTests
{
    private const string SceneJson = """{"index":2,"title":"The Rooftop","environment":"Rooftop"}""";

    private const string CastList = "- Aiko (Protagonist): 1girl, red hair, raincoat";

    private const string EnvironmentCanon = "Rooftop: rain-slick tiles, neon sign reading KAWA";

    private const string PreviousPages =
        "page 3: Aiko climbs the fire escape, ends with: the door swings open";

    private const string Continuity = "LOCATION rooftop, night, rain; LAST BEAT the neon sign flickers";

    private const string StyleSpec = "gekiga, heavy ink, screentone at 30 percent, monochrome";

    private const string Palette = "ink black, paper white, 30 percent grey";

    private const string ContentGuidance = "Adult story: describe the moment plainly.";

    private const string DialogueRule = "keep short lines verbatim; compress long speeches";

    private const string Story = "Aiko climbed the fire escape while the rain hammered the roof.";

    private const string KnownCharacters = "- Aiko (Protagonist): 1girl, red hair";

    private const string KnownPlaces = "- Rooftop";

    private static string ScriptedPages() =>
        CuratorPrompts.PageScript(
            firstPage: 4,
            lastPage: 6,
            sceneJson: SceneJson,
            characters: CastList,
            environmentCanon: EnvironmentCanon,
            previousPages: PreviousPages,
            continuity: Continuity,
            styleSpec: StyleSpec,
            palette: Palette,
            contentGuidance: ContentGuidance
        );

    [Fact]
    public void PageScript_carries_every_consistency_input()
    {
        var prompt = ScriptedPages();

        Assert.Contains(SceneJson, prompt);
        Assert.Contains(CastList, prompt);
        Assert.Contains(EnvironmentCanon, prompt);
        Assert.Contains(PreviousPages, prompt);
        Assert.Contains(Continuity, prompt);
        Assert.Contains(StyleSpec, prompt);
        Assert.Contains(Palette, prompt);
        Assert.Contains(ContentGuidance, prompt);
    }

    [Fact]
    public void PageScript_states_the_lettering_limit()
    {
        // Long bubble text renders unreadably, so the model must be told a limit.
        Assert.Contains("60", ScriptedPages());
    }

    [Theory]
    [InlineData("LOCATION")]
    [InlineData("CAST")]
    [InlineData("PROPS")]
    [InlineData("THREADS")]
    [InlineData("PALETTE")]
    [InlineData("LAST BEAT")]
    public void PageScript_requires_each_ledger_section(string section)
    {
        Assert.Contains(section, ScriptedPages());
    }

    [Fact]
    public void ScenePlan_names_the_environments_and_the_page_budget()
    {
        var prompt = CuratorPrompts.ScenePlan(
            storyText: Story,
            totalPages: 37,
            environments: "- Rooftop\n- Ramen shop"
        );

        Assert.Contains("Rooftop", prompt);
        Assert.Contains("Ramen shop", prompt);
        Assert.Contains("37", prompt);
        Assert.Contains(Story, prompt);
    }

    [Fact]
    public void SeamFix_carries_the_seam_list()
    {
        const string seams = "page 1: opens \"A\" | closes \"B\" | last beat: C";

        Assert.Contains(seams, CuratorPrompts.SeamFix(seams));
    }

    [Fact]
    public void PanelBreakdown_carries_the_scene_cast_and_rules()
    {
        var prompt = CuratorPrompts.PanelBreakdown(
            sceneJson: SceneJson,
            characters: CastList,
            dialogueRule: DialogueRule,
            suggestedPanels: 7,
            readingDirection: "right to left",
            contentNote: ContentGuidance
        );

        Assert.Contains(SceneJson, prompt);
        Assert.Contains(CastList, prompt);
        Assert.Contains(DialogueRule, prompt);
        Assert.Contains("7", prompt);
        Assert.Contains("right to left", prompt);
        Assert.Contains(ContentGuidance, prompt);
    }

    [Fact]
    public void Cover_carries_the_title_premise_and_cast()
    {
        var prompt = CuratorPrompts.Cover("The Long Night", "Aiko cannot sleep.", CastList);

        Assert.Contains("The Long Night", prompt);
        Assert.Contains("Aiko cannot sleep.", prompt);
        Assert.Contains(CastList, prompt);
    }

    [Fact]
    public void StoryDiscovery_carries_the_story_and_both_bibles()
    {
        var prompt = CuratorPrompts.StoryDiscovery(Story, KnownCharacters, KnownPlaces);

        Assert.Contains(Story, prompt);
        Assert.Contains(KnownCharacters, prompt);
        Assert.Contains(KnownPlaces, prompt);
    }

    [Fact]
    public void SceneSegmentation_carries_the_story()
    {
        Assert.Contains(Story, CuratorPrompts.SceneSegmentation(Story));
    }

    [Fact]
    public void System_carries_the_extra_instructions()
    {
        Assert.Contains("MARKER-EXTRA", CuratorPrompts.System("MARKER-EXTRA"));
    }

    [Theory]
    [InlineData(nameof(CuratorPrompts.System))]
    [InlineData(nameof(CuratorPrompts.StoryDiscovery))]
    [InlineData(nameof(CuratorPrompts.SceneSegmentation))]
    [InlineData(nameof(CuratorPrompts.PanelBreakdown))]
    [InlineData(nameof(CuratorPrompts.ScenePlan))]
    [InlineData(nameof(CuratorPrompts.PageScript))]
    [InlineData(nameof(CuratorPrompts.SeamFix))]
    [InlineData(nameof(CuratorPrompts.Cover))]
    public void Every_prompt_survives_null_and_empty_input(string call)
    {
        Assert.NotNull(Build(call, null));
        Assert.NotNull(Build(call, string.Empty));
    }

    private static string Build(string call, string? value) =>
        call switch
        {
            nameof(CuratorPrompts.System) => CuratorPrompts.System(value!),
            nameof(CuratorPrompts.StoryDiscovery) =>
                CuratorPrompts.StoryDiscovery(value!, value!, value!),
            nameof(CuratorPrompts.SceneSegmentation) => CuratorPrompts.SceneSegmentation(value!),
            nameof(CuratorPrompts.PanelBreakdown) =>
                CuratorPrompts.PanelBreakdown(value!, value!, value!, 6, value!),
            nameof(CuratorPrompts.ScenePlan) => CuratorPrompts.ScenePlan(value!, 10, value!),
            nameof(CuratorPrompts.PageScript) =>
                CuratorPrompts.PageScript(
                    1,
                    2,
                    value!,
                    value!,
                    value!,
                    value!,
                    value,
                    value!,
                    value!,
                    value!
                ),
            nameof(CuratorPrompts.SeamFix) => CuratorPrompts.SeamFix(value!),
            nameof(CuratorPrompts.Cover) => CuratorPrompts.Cover(value!, value!, value!),
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, null),
        };
}
