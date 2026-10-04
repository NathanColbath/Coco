using ComicStudio.Api.Domain;
using ComicStudio.Api.Storytelling;

namespace ComicStudio.Api.Comics;

public record CuratorContext(
    string Token,
    Domain.AiModel Model,
    int ContextTokens,
    DialogueAdaptation DialogueAdaptation,
    ReadingDirection Direction,
    ContentRating Rating = ContentRating.General
)
{
    /// <summary>Room left for the model's answer.</summary>
    public int InputBudget => Math.Max(2000, ContextTokens - 3500);
}

public record CoverSuggestion(string Prompt, string Negative);

/// <summary>
/// What one combined discovery pass found, with the counters the run records.
/// </summary>
public record StoryDiscoveryOutcome(
    List<DiscoveredCharacter> Characters,
    List<DiscoveredEnvironment> Environments,
    StructuredStats Stats
);

/// <summary>
/// What one page-script call produced. A truncated result is not a parse
/// mistake: it says the batch was too big, and the caller must re-ask smaller.
/// </summary>
public record PageScriptOutcome(
    List<ScriptedPage> Pages,
    string? Error,
    StructuredStats Stats
)
{
    public bool Succeeded => Pages.Count > 0;

    public bool Truncated => Stats.Truncated;

    public int Attempts => Stats.Attempts;

    public int ContentChars => Stats.ContentChars;

    public int ReasoningChars => Stats.ReasoningChars;
}

/// <summary>What scene planning returned, with the counters for the run.</summary>
public record ScenePlanOutcome(List<ScriptedScene> Scenes, string? Error, StructuredStats Stats);

/// <summary>One deterministic batch of a scene's pages.</summary>
public readonly record struct PageScriptBatch(int FirstPage, int Count);

/// <summary>
/// The scheduler's ledger for one scene: what was asked for, what came back,
/// and what is missing because a batch could not be scripted at all.
/// </summary>
public record ScriptBatchRun(
    int Batches,
    int PagesRequested,
    int PagesReturned,
    int FailedBatches,
    int MissingPages,
    int BatchSize,
    StructuredStats Stats
);

/// <summary>
/// The adaptation editor. Each method is one model call with a strict JSON
/// contract; everything that can be decided without a model — layout, prompting,
/// positioning — is decided elsewhere, deterministically.
/// </summary>
public class Curator(StructuredTextService structured, ITokenCounter counter)
{
    /// <summary>
    /// Roughly how many tokens one rich page script costs: panels, bubbles and
    /// the continuity ledger together, before the model's reasoning is counted.
    /// Deliberately generous; a batch that is too small only costs a call.
    /// </summary>
    public const int PageScriptTokensPerPage = 900;

    /// <summary>
    /// The output ceiling the transport is known to honour. NovelAI's chat
    /// endpoint clamps to 2048 tokens and reasoning shares that budget, so
    /// batches are sized for the clamp even if a later transport fix raises it.
    /// </summary>
    public const int ConservativeOutputCeiling = 2048;

    /// <summary>Even a raised ceiling gets at most this many pages per call.</summary>
    public const int MaxPagesPerScriptBatch = 4;

    /// <summary>
    /// How many pages one scripting call asks for. A scene's page count is a
    /// wish, not a budget: asking for five pages in one call overflows the
    /// output budget and truncates mid-JSON, so the scene is split into
    /// deterministic batches that fit. Always at least one page.
    /// </summary>
    public static int ScriptBatchSize(
        int outputCeiling = ConservativeOutputCeiling,
        int tokensPerPage = PageScriptTokensPerPage
    ) =>
        Math.Clamp(
            outputCeiling / Math.Max(1, tokensPerPage),
            1,
            MaxPagesPerScriptBatch
        );

    /// <summary>
    /// Discovers the cast and the places in one pass over the story. The two
    /// questions used to be sent separately, which read the whole story twice
    /// before a single page was written.
    /// </summary>
    public async Task<StoryDiscoveryOutcome> DiscoverStoryAsync(
        string storyText,
        string knownCharacters,
        string knownEnvironments,
        CuratorContext context,
        CancellationToken cancellationToken = default
    )
    {
        var chunks = TextChunker.Chunk(storyText, counter, context.InputBudget);
        var characters = new Dictionary<string, DiscoveredCharacter>(StringComparer.OrdinalIgnoreCase);
        var environments = new Dictionary<string, DiscoveredEnvironment>(StringComparer.OrdinalIgnoreCase);
        var stats = default(StructuredStats);

        foreach (var chunk in chunks)
        {
            var result = await structured.CompleteAsync<StoryDiscoveryResult>(
                new StructuredRequest
                {
                    Model = context.Model,
                    Token = context.Token,
                    System = CuratorPrompts.System(
                        "You are discovering the cast and the places in a story."
                    ),
                    User = CuratorPrompts.StoryDiscovery(
                        chunk,
                        knownCharacters,
                        knownEnvironments
                    ),
                    MaxTokens = 3000,
                    Temperature = 0.45,
                },
                cancellationToken
            );

            stats += StructuredStats.From(result);

            if (!result.Succeeded)
            {
                continue;
            }

            MergeDiscoveredCharacters(characters, result.Value!.Characters);
            MergeDiscoveredEnvironments(environments, result.Value!.Environments);
        }

        return new StoryDiscoveryOutcome(
            characters.Values.ToList(),
            environments.Values.ToList(),
            stats
        );
    }

    private static void MergeDiscoveredCharacters(
        Dictionary<string, DiscoveredCharacter> merged,
        IEnumerable<DiscoveredCharacter> characters
    )
    {
        foreach (var character in characters)
        {
            if (string.IsNullOrWhiteSpace(character.Name))
            {
                continue;
            }

            if (merged.TryGetValue(character.Name, out var existing))
            {
                // Later chunks often add detail; keep the richest tags.
                if (string.IsNullOrWhiteSpace(existing.PromptTags))
                {
                    existing.PromptTags = character.PromptTags;
                }

                if (string.IsNullOrWhiteSpace(existing.OutfitTags))
                {
                    existing.OutfitTags = character.OutfitTags;
                }

                if (string.IsNullOrWhiteSpace(existing.Bio))
                {
                    existing.Bio = character.Bio;
                }

                continue;
            }

            merged[character.Name] = character;
        }
    }

    private static void MergeDiscoveredEnvironments(
        Dictionary<string, DiscoveredEnvironment> merged,
        IEnumerable<DiscoveredEnvironment> environments
    )
    {
        foreach (var environment in environments)
        {
            if (string.IsNullOrWhiteSpace(environment.Name))
            {
                continue;
            }

            if (merged.TryGetValue(environment.Name, out var existing))
            {
                // Later chunks often add detail; keep the richest fields.
                existing.VisualTags ??= environment.VisualTags;
                existing.Lighting ??= environment.Lighting;
                existing.PaletteNotes ??= environment.PaletteNotes;
                existing.Features ??= environment.Features;
                existing.Notes ??= environment.Notes;
                existing.FirstSeenPage ??= environment.FirstSeenPage;

                if (existing.Aliases.Count == 0)
                {
                    existing.Aliases = environment.Aliases;
                }

                continue;
            }

            merged[environment.Name] = environment;
        }
    }

    public async Task<SceneSegmentationResult> SegmentAsync(
        string storyText,
        CuratorContext context,
        CancellationToken cancellationToken = default
    )
    {
        var chunks = TextChunker.Chunk(storyText, counter, context.InputBudget);
        var scenes = new List<DiscoveredScene>();

        foreach (var chunk in chunks)
        {
            var result = await structured.CompleteAsync<SceneSegmentationResult>(
                new StructuredRequest
                {
                    Model = context.Model,
                    Token = context.Token,
                    System = CuratorPrompts.System("You are dividing a story into scenes."),
                    User = CuratorPrompts.SceneSegmentation(chunk),
                    MaxTokens = 2500,
                    Temperature = 0.4,
                },
                cancellationToken
            );

            if (result.Succeeded)
            {
                scenes.AddRange(result.Value!.Scenes);
            }
        }

        // Re-index so the plan is ordered regardless of chunking.
        for (var index = 0; index < scenes.Count; index++)
        {
            scenes[index].Index = index;
        }

        return new SceneSegmentationResult { Scenes = scenes };
    }

    public async Task<ScenePanelPlan> PlanPanelsAsync(
        DiscoveredScene scene,
        IReadOnlyList<Character> characters,
        CuratorContext context,
        int sceneWords = 0,
        CancellationToken cancellationToken = default
    )
    {
        var characterList = string.Join(
            "\n",
            characters.Select(character =>
                $"- {character.Name} ({character.Role}): {character.Visual.PromptTags ?? character.Name}"
            )
        );

        var dialogueRule = context.DialogueAdaptation switch
        {
            DialogueAdaptation.Verbatim =>
                "keep the author's words exactly as written, trimming only to fit a bubble",
            DialogueAdaptation.ShortenLongLines =>
                "keep short lines verbatim; compress long speeches into their essential sentence",
            _ => "rewrite freely for comic pacing while keeping each character's voice",
        };

        var direction = context.Direction == ReadingDirection.RightToLeft
            ? "right to left"
            : "left to right";

        var sceneJson = System.Text.Json.JsonSerializer.Serialize(
            scene,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false }
        );

        var result = await structured.CompleteAsync<ScenePanelPlan>(
            new StructuredRequest
            {
                Model = context.Model,
                Token = context.Token,
                System = CuratorPrompts.System("You are planning panels for one scene."),
                User = CuratorPrompts.PanelBreakdown(
                    sceneJson,
                    characterList,
                    dialogueRule,
                    suggestedPanels: PanelsFor(sceneWords),
                    direction,
                    ContentModifiers.CuratorNote(context.Rating)
                ),
                MaxTokens = 2600,
                Temperature = 0.6,
            },
            cancellationToken
        );

        return result.Succeeded ? result.Value! : new ScenePanelPlan();
    }

    /// <summary>
    /// Divides the story into scenes and budgets pages per scene. Scene
    /// boundaries are where a comic cuts, and pages are scripted inside a scene
    /// so that everything within one reads as a continuous moment.
    /// </summary>
    public async Task<ScenePlanOutcome> PlanScenesAsync(
        string storyText,
        int totalPages,
        IReadOnlyList<string> environmentNames,
        CuratorContext context,
        CancellationToken cancellationToken = default
    )
    {
        var environmentList = environmentNames.Count == 0
            ? "(none recorded — invent consistent names and reuse them)"
            : string.Join("\n", environmentNames.Select(name => $"- {name}"));

        var result = await structured.CompleteAsync<ScenePlanResult>(
            new StructuredRequest
            {
                Model = context.Model,
                Token = context.Token,
                System = CuratorPrompts.System("You are dividing a story into scenes."),
                User = CuratorPrompts.ScenePlan(storyText, totalPages, environmentList),
                MaxTokens = 2500,
                Temperature = 0.5,
            },
            cancellationToken
        );

        var stats = StructuredStats.From(result);

        if (!result.Succeeded)
        {
            return new ScenePlanOutcome([], result.Error, stats);
        }

        var scenes = result.Value!.Scenes.OrderBy(scene => scene.Index).ToList();
        foreach (var scene in scenes)
        {
            scene.PageCount = Math.Max(1, scene.PageCount);
        }

        return new ScenePlanOutcome(scenes, null, stats);
    }

    /// <summary>
    /// Scripts the pages of one scene. Consistency is created here: the art style
    /// so the writing suits it, the value anchoring, the environment canon, the
    /// scene's own state, and the pages immediately before so this continues
    /// rather than restarts.
    /// </summary>
    public async Task<PageScriptOutcome> ScriptPagesAsync(
        int firstPage,
        int pageCount,
        ScriptedScene scene,
        IReadOnlyList<Character> characters,
        string environmentCanon,
        IReadOnlyList<ComicPage> previousPages,
        string styleSpec,
        string palette,
        CuratorContext context,
        CancellationToken cancellationToken = default
    )
    {
        var characterList = characters.Count == 0
            ? "(no cast recorded yet)"
            : string.Join(
                "\n",
                characters.Select(character =>
                    $"- {character.Name}: {character.Visual.PromptTags ?? "no visual tags"}"
                        + (string.IsNullOrWhiteSpace(character.Visual.OutfitTags)
                            ? string.Empty
                            : $" | wears {character.Visual.OutfitTags}")
                )
            );

        if (string.IsNullOrWhiteSpace(environmentCanon))
        {
            environmentCanon = scene.Environment ?? "(unnamed place)";
        }

        // Only the tail of the previous pages matters for flow.
        var previous = previousPages.Count == 0
            ? "(these are the first pages of the comic)"
            : string.Join(
                "\n",
                previousPages.Select(page =>
                    $"page {page.Index + 1}: {Trim(page.Script, 400)} \u2192 ends with: {LastBeat(page.Continuity)}"
                )
            );

        var sceneJson = System.Text.Json.JsonSerializer.Serialize(
            scene,
            new System.Text.Json.JsonSerializerOptions()
        );

        var continuity = previousPages.Count == 0 ? null : previousPages[^1].Continuity;

        var result = await structured.CompleteAsync<PageScriptResult>(
            new StructuredRequest
            {
                Model = context.Model,
                Token = context.Token,
                System = CuratorPrompts.System(
                    "You are scripting the pages of one manga scene, panel by panel."
                ),
                User = CuratorPrompts.PageScript(
                    firstPage,
                    firstPage + pageCount - 1,
                    sceneJson,
                    characterList,
                    environmentCanon,
                    previous,
                    continuity,
                    styleSpec,
                    palette,
                    ContentTagLibrary.CuratorGuidance(context.Rating)
                ),
                MaxTokens = 4000,
                Temperature = context.Rating == ContentRating.General ? 0.7 : 0.8,
            },
            cancellationToken
        );

        return result.Succeeded
            ? new PageScriptOutcome(
                result.Value!.Pages.OrderBy(page => page.Index).ToList(),
                null,
                StructuredStats.From(result)
            )
            : new PageScriptOutcome([], result.Error, StructuredStats.From(result));
    }

    /// <summary>
    /// Walks a scene's pages in batches. A truncated batch is re-asked for
    /// fewer pages — never regenerated at the same size, because the size is
    /// what overflowed the output budget. A batch that still fails is handed to
    /// onFailure so it is recorded rather than skipped, and the cursor moves
    /// past its pages so later scenes keep their planned ranges.
    /// </summary>
    public static async Task<ScriptBatchRun> ScriptBatchesAsync(
        int firstPage,
        int pageCount,
        int initialBatchSize,
        Func<PageScriptBatch, CancellationToken, Task<PageScriptOutcome>> request,
        Func<PageScriptBatch, PageScriptOutcome, Task> onSuccess,
        Func<PageScriptBatch, PageScriptOutcome, Task> onFailure,
        CancellationToken cancellationToken
    )
    {
        var batchSize = Math.Max(1, initialBatchSize);
        var remaining = Math.Max(0, pageCount);
        var batches = 0;
        var pagesRequested = 0;
        var pagesReturned = 0;
        var failedBatches = 0;
        var missingPages = 0;
        var stats = default(StructuredStats);

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var size = Math.Min(batchSize, remaining);
            var batch = new PageScriptBatch(firstPage + (pageCount - remaining), size);

            batches++;
            pagesRequested += size;

            var outcome = await request(batch, cancellationToken);
            stats += outcome.Stats;

            if (outcome.Succeeded)
            {
                await onSuccess(batch, outcome);
                var persisted = Math.Clamp(outcome.Pages.Count, 0, size);

                pagesReturned += persisted;
                missingPages += size - persisted;
                remaining -= size;
                continue;
            }

            if (outcome.Truncated && size > 1)
            {
                // The request was too big. Halve it and re-ask for the same
                // pages instead of resending a prompt that cannot fit.
                batchSize = Math.Max(1, size / 2);
                continue;
            }

            failedBatches++;
            missingPages += size;
            remaining -= size;
            await onFailure(batch, outcome);
        }

        return new ScriptBatchRun(
            batches,
            pagesRequested,
            pagesReturned,
            failedBatches,
            missingPages,
            batchSize,
            stats
        );
    }

    /// <summary>
    /// Reviews the running order for pages that do not follow from the one
    /// before, and returns only the opening lines that need rewriting.
    /// </summary>
    public async Task<Dictionary<int, string>> FixSeamsAsync(
        IReadOnlyList<ComicPage> pages,
        CuratorContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (pages.Count < 2)
        {
            return [];
        }

        var seams = string.Join(
            "\n",
            pages.Select(page =>
                $"page {page.Index + 1}: opens \"{FirstSentence(page.Script)}\" | "
                    + $"closes \"{LastSentence(page.Script)}\" | last beat: {LastBeat(page.Continuity)}"
            )
        );

        var result = await structured.CompleteAsync<SeamFixResult>(
            new StructuredRequest
            {
                Model = context.Model,
                Token = context.Token,
                System = CuratorPrompts.System("You are checking a running order for continuity."),
                User = CuratorPrompts.SeamFix(seams),
                MaxTokens = 2000,
                Temperature = 0.3,
            },
            cancellationToken
        );

        if (!result.Succeeded)
        {
            return [];
        }

        return result.Value!.Fixes
            .Where(fix => !string.IsNullOrWhiteSpace(fix.Opening))
            .GroupBy(fix => fix.Index)
            .ToDictionary(group => group.Key, group => group.First().Opening!);
    }

    private static string Trim(string? text, int limit) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Length <= limit
                ? text
                : text[..limit] + "…";

    private static string FirstSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var stop = text.IndexOf(". ", StringComparison.Ordinal);
        return stop > 0 ? text[..stop] : Trim(text, 200);
    }

    private static string LastSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var start = text.LastIndexOf(". ", StringComparison.Ordinal);
        return start > 0 ? text[(start + 2)..] : Trim(text, 200);
    }

    private static string LastBeat(string? continuity)
    {
        if (string.IsNullOrWhiteSpace(continuity))
        {
            return "(unknown)";
        }

        var index = continuity.IndexOf("LAST BEAT", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? Trim(continuity, 200) : Trim(continuity[(index + 9)..].Trim(), 200);
    }

    public async Task<CoverSuggestion?> SuggestCoverAsync(
        string title,
        string premise,
        IReadOnlyList<Character> characters,
        CuratorContext context,
        CancellationToken cancellationToken = default
    )
    {
        var characterList = string.Join(
            ", ",
            characters
                .OrderBy(character => character.Role)
                .Take(3)
                .Select(character =>
                    $"{character.Name}: {character.Visual.PromptTags ?? character.Name}"
                )
        );

        var result = await structured.CompleteAsync<CoverSuggestionDto>(
            new StructuredRequest
            {
                Model = context.Model,
                Token = context.Token,
                System = CuratorPrompts.System("You are designing a manga cover."),
                User = CuratorPrompts.Cover(title, premise, characterList),
                MaxTokens = 800,
                Temperature = 0.7,
            },
            cancellationToken
        );

        return result.Succeeded
            ? new CoverSuggestion(result.Value!.Prompt ?? string.Empty, result.Value.Negative ?? string.Empty)
            : null;
    }

    /// <summary>
    /// Panel count scales with how much prose the scene actually contains.
    /// Asking for a fixed dozen panels on a two-paragraph scene wastes a minute
    /// of generation on padding.
    /// </summary>
    public static int PanelsFor(int sceneWords) =>
        sceneWords <= 0 ? 8 : Math.Clamp((int)Math.Round(sceneWords / 110.0), 4, 16);

    private class CoverSuggestionDto
    {
        public string? Prompt { get; set; }

        public string? Negative { get; set; }
    }
}
