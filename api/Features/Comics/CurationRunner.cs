using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComicStudio.Api.Comics;
using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Hubs;
using ComicStudio.Api.Security;
using ComicStudio.Api.Storytelling;
using ComicStudio.Api.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Environment = ComicStudio.Api.Domain.Environment;

namespace ComicStudio.Api.Features.Comics;

/// <summary>
/// Runs the adaptation passes for a comic: discover the cast and the places,
/// plan scenes with page budgets, script each scene's pages as one continuous
/// moment, then smooth the seams between pages. Progress is pushed to the owner
/// over SignalR because a full pass takes a minute or two.
/// </summary>
public class CurationRunner(
    IServiceScopeFactory scopeFactory,
    IHubContext<GenerationHub> hub,
    IConfiguration configuration,
    ILogger<CurationRunner> logger
)
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> active = new();

    public void Start(Guid projectId) => _ = Task.Run(() => RunAsync(projectId));

    public bool Cancel(Guid projectId)
    {
        if (active.TryGetValue(projectId, out var source))
        {
            source.Cancel();
            return true;
        }

        return false;
    }

    private async Task RunAsync(Guid projectId)
    {
        using var cancellation = new CancellationTokenSource();
        active[projectId] = cancellation;
        var timer = new StageTimer();

        try
        {
            using var scope = scopeFactory.CreateScope();
            var provider = scope.ServiceProvider;
            var db = provider.GetRequiredService<AppDbContext>();
            var protector = provider.GetRequiredService<SecretProtector>();
            var curator = provider.GetRequiredService<Curator>();
            var textModels = provider.GetRequiredService<TextModelResolver>();

            var project = await db
                .ComicProjects.Include(item => item.Story)
                .FirstOrDefaultAsync(item => item.Id == projectId, cancellation.Token);
            if (project?.Story is null)
            {
                return;
            }

            var user = await db.Users.FirstOrDefaultAsync(
                item => item.Id == project.UserId,
                cancellation.Token
            );
            if (user is null)
            {
                return;
            }

            var token = protector.Unprotect(user.Id, user.NovelAiTokenCipher);
            if (string.IsNullOrEmpty(token))
            {
                await FailAsync(
                    db,
                    project,
                    "NovelAI is not connected. Add a persistent token in Settings."
                );
                return;
            }

            var model = await textModels.ResolveAsync(project.Style.ModelId, user.PreferredTextModelId, cancellation.Token);
            var context = new CuratorContext(
                token,
                model,
                TextModelResolver.ContextTokensFor(model, user.NovelAiTier),
                project.DialogueAdaptation,
                project.Direction,
                project.Rating
            );

            project.Status = ComicStatus.Curating;
            await db.SaveChangesAsync(cancellation.Token);
            await ProgressAsync(project, CurationStage.CastDiscovery, "Reading the story…", 0);

            var blocks = await db
                .StoryBlocks.AsNoTracking()
                .Where(block => block.StoryId == project.StoryId)
                .ToListAsync(cancellation.Token);
            var storyText = StoryPath.Text(StoryPath.Build(blocks, project.Story!.CursorBlockId));

            if (string.IsNullOrWhiteSpace(storyText))
            {
                await FailAsync(db, project, "The story is empty. Write something first.");
                return;
            }

            // --- Cast and places ---------------------------------------------
            // One pass answers both questions — cast and environments used to
            // send the same prose to the model twice before any page was
            // written. An unchanged story skips the pass entirely: re-scripting
            // should not pay discovery again when the prose is identical.
            var existing = await db
                .Characters.Where(character => character.StoryId == project.StoryId)
                .ToListAsync(cancellation.Token);
            var knownEnvironments = await db
                .Environments.Where(environment => environment.StoryId == project.StoryId)
                .ToListAsync(cancellation.Token);

            var storyHash = StoryHash(storyText);
            var previousHash = await FindDiscoveryHashAsync(
                db,
                project.Id,
                cancellation.Token
            );

            if (
                existing.Count > 0
                && knownEnvironments.Count > 0
                && string.Equals(previousHash, storyHash, StringComparison.Ordinal)
            )
            {
                logger.LogInformation(
                    "Story unchanged for project {ProjectId}; skipping discovery.",
                    project.Id
                );

                await ProgressAsync(
                    project,
                    CurationStage.CastDiscovery,
                    "Story unchanged — cast and places already known.",
                    20
                );
            }
            else
            {
                var discovery = await curator.DiscoverStoryAsync(
                    storyText,
                    existing.Count == 0
                        ? "(none yet)"
                        : string.Join(", ", existing.Select(character => character.Name)),
                    knownEnvironments.Count == 0
                        ? "(none yet)"
                        : string.Join(
                            ", ",
                            knownEnvironments.Select(environment => environment.Name)
                        ),
                    context,
                    cancellation.Token
                );

                var castAdded = await MergeCastAsync(
                    db,
                    project,
                    discovery.Characters,
                    cancellation.Token
                );

                var environmentsAdded = await MergeEnvironmentsAsync(
                    db,
                    project,
                    discovery.Environments,
                    cancellation.Token
                );

                await RecordAsync(
                    db,
                    project,
                    CurationStage.CastDiscovery,
                    model.Id,
                    new
                    {
                        storyHash,
                        characters = discovery.Characters.Count,
                        environments = discovery.Environments.Count,
                        contentChars = discovery.Stats.ContentChars,
                        reasoningChars = discovery.Stats.ReasoningChars,
                        attempts = discovery.Stats.Attempts,
                        truncated = discovery.Stats.Truncated,
                    },
                    timer,
                    // A truncated discovery lost part of the cast or places;
                    // recording it as failed also keeps its hash from being
                    // reused to skip discovery next time.
                    discovery.Stats.Truncated
                        ? "One or more discovery responses were truncated."
                        : null,
                    cancellation.Token
                );

                await ProgressAsync(
                    project,
                    CurationStage.CastDiscovery,
                    $"Found {castAdded} character(s) and {environmentsAdded} place(s).",
                    20
                );
            }

            var characters = await db
                .Characters.Where(character => character.StoryId == project.StoryId)
                .ToListAsync(cancellation.Token);

            // --- Scenes and page scripting ----------------------------------
            // Scenes drive the writing: pages inside one scene share a place and
            // a beat, and each batch is handed the ledger the pages before it
            // left behind so the book reads as one continuous comic.
            var scripted = await ScriptPagesAsync(
                db,
                curator,
                project,
                storyText,
                characters,
                context,
                timer,
                cancellation.Token
            );

            await RecordAsync(
                db,
                project,
                CurationStage.PanelBreakdown,
                model.Id,
                new
                {
                    pages = scripted.Pages,
                    batches = scripted.Batches,
                    pagesRequested = scripted.PagesRequested,
                    pagesReturned = scripted.PagesReturned,
                    failedBatches = scripted.FailedBatches,
                    missingPages = scripted.MissingPages,
                    contentChars = scripted.Stats.ContentChars,
                    reasoningChars = scripted.Stats.ReasoningChars,
                    attempts = scripted.Stats.Attempts,
                    truncated = scripted.Stats.Truncated,
                },
                timer,
                scripted.FailedBatches > 0 || scripted.MissingPages > 0
                    ? $"{scripted.MissingPages} page(s) missing after "
                        + $"{scripted.FailedBatches} failed batch(es)."
                    : null,
                cancellation.Token
            );

            // The pages that did script are kept, but a run with holes is not a
            // success: the user must see what is missing rather than a plan
            // with silent gaps.
            if (scripted.FailedBatches > 0 || scripted.MissingPages > 0)
            {
                await FailAsync(
                    db,
                    project,
                    $"Plan incomplete: {scripted.Pages} page(s) scripted, "
                        + $"{scripted.MissingPages} missing after "
                        + $"{scripted.FailedBatches} failed batch(es)."
                );
                return;
            }

            if (scripted.Pages == 0)
            {
                await FailAsync(db, project, "The curator returned no pages.");
                return;
            }

            project.Status = ComicStatus.Planned;
            project.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellation.Token);

            await ProgressAsync(
                project,
                CurationStage.Done,
                $"Plan ready: {scripted.Pages} page(s).",
                100
            );

            logger.LogInformation(
                "Curated project {ProjectId}: {Pages} page(s) in {Ms}ms.",
                project.Id,
                scripted.Pages,
                timer.ElapsedMs
            );
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await TrySetStatusAsync(projectId, ComicStatus.Draft);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Curation failed for project {ProjectId}.", projectId);
            await TrySetStatusAsync(projectId, ComicStatus.Draft);
        }
        finally
        {
            active.TryRemove(projectId, out _);
        }
    }

    private static async Task<int> MergeCastAsync(
        AppDbContext db,
        ComicProject project,
        IReadOnlyList<DiscoveredCharacter> discovered,
        CancellationToken cancellationToken
    )
    {
        if (discovered.Count == 0)
        {
            return 0;
        }

        var existing = await db
            .Characters.Where(character => character.StoryId == project.StoryId)
            .ToListAsync(cancellationToken);

        var added = 0;

        foreach (var candidate in discovered)
        {
            var match = existing.FirstOrDefault(character =>
                Matches(character, candidate.Name)
                || candidate.Aliases.Any(alias => Matches(character, alias))
            );

            if (match is not null)
            {
                // Fill in blanks without overwriting hand-authored canon.
                match.Visual.PromptTags ??= candidate.PromptTags;
                match.Visual.OutfitTags ??= candidate.OutfitTags;
                match.Bio ??= candidate.Bio;
                match.Personality ??= candidate.Personality;
                match.SpeechStyle ??= candidate.SpeechStyle;
                match.UpdatedAt = DateTimeOffset.UtcNow;
                continue;
            }

            var character = new Character
            {
                StoryId = project.StoryId,
                UserId = project.UserId,
                Name = candidate.Name.Trim(),
                Aliases = candidate.Aliases.Count > 0 ? string.Join('\n', candidate.Aliases) : null,
                Role = ParseRole(candidate.Role),
                Bio = candidate.Bio,
                Personality = candidate.Personality,
                SpeechStyle = candidate.SpeechStyle,
                Visual = new CharacterVisual
                {
                    PromptTags = candidate.PromptTags,
                    OutfitTags = candidate.OutfitTags,
                    HairColor = candidate.HairColor,
                    EyeColor = candidate.EyeColor,
                    Notes = candidate.Appearance,
                },
            };

            db.Characters.Add(character);
            existing.Add(character);
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private static async Task<int> MergeEnvironmentsAsync(
        AppDbContext db,
        ComicProject project,
        IReadOnlyList<DiscoveredEnvironment> discovered,
        CancellationToken cancellationToken
    )
    {
        if (discovered.Count == 0)
        {
            return 0;
        }

        var existing = await db
            .Environments.Where(environment => environment.StoryId == project.StoryId)
            .ToListAsync(cancellationToken);

        var added = 0;

        foreach (var candidate in discovered)
        {
            var match = existing.FirstOrDefault(environment =>
                Matches(environment, candidate.Name)
                || candidate.Aliases.Any(alias => Matches(environment, alias))
            );

            if (match is not null)
            {
                // Fill in blanks without overwriting hand-authored canon.
                match.VisualTags ??= candidate.VisualTags;
                match.Lighting ??= candidate.Lighting;
                match.PaletteNotes ??= candidate.PaletteNotes;
                match.Features ??= candidate.Features;
                match.Notes ??= candidate.Notes;
                match.FirstSeenPage ??= candidate.FirstSeenPage;
                match.Aliases = string.IsNullOrWhiteSpace(match.Aliases) && candidate.Aliases.Count > 0
                    ? string.Join('\n', candidate.Aliases)
                    : match.Aliases;
                match.UpdatedAt = DateTimeOffset.UtcNow;
                continue;
            }

            var environment = new Environment
            {
                StoryId = project.StoryId,
                UserId = project.UserId,
                Name = candidate.Name.Trim(),
                Aliases = candidate.Aliases.Count > 0 ? string.Join('\n', candidate.Aliases) : null,
                Kind = ParseEnvironmentKind(candidate.Kind),
                VisualTags = candidate.VisualTags,
                Lighting = candidate.Lighting,
                PaletteNotes = candidate.PaletteNotes,
                Features = candidate.Features,
                Notes = candidate.Notes,
                FirstSeenPage = candidate.FirstSeenPage,
            };

            db.Environments.Add(environment);
            existing.Add(environment);
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    private static bool Matches(Character character, string name) =>
        string.Equals(character.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)
        || (character.Aliases ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(alias => string.Equals(alias, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static CharacterRole ParseRole(string? role) =>
        Enum.TryParse<CharacterRole>(role, ignoreCase: true, out var parsed)
            ? parsed
            : CharacterRole.Supporting;

    private static bool Matches(Environment environment, string name) =>
        string.Equals(environment.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)
        || (environment.Aliases ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(alias => string.Equals(alias, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static EnvironmentKind ParseEnvironmentKind(string? kind) =>
        Enum.TryParse<EnvironmentKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : EnvironmentKind.Exterior;

    private static Environment? ResolveEnvironment(
        IReadOnlyList<Environment> environments,
        string? name
    ) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : environments.FirstOrDefault(environment => Matches(environment, name));

    /// <summary>
    /// The environment canon handed to the page scripter so a revisited place
    /// reads as the same place: name, visual tags, lighting, values and the
    /// objects that anchor it.
    /// </summary>
    private static string EnvironmentCanon(Environment? environment) =>
        environment is null
            ? string.Empty
            : string.Join(
                ", ",
                new[]
                {
                    environment.Name,
                    environment.VisualTags,
                    environment.Lighting,
                    environment.PaletteNotes,
                    environment.Features,
                }
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .Select(part => part!.Trim())
            );

    /// <summary>
    /// The scripting ledger for a run: written pages plus everything needed to
    /// report holes instead of pretending they are not there.
    /// </summary>
    private readonly record struct ScriptingOutcome(
        int Pages,
        int Batches,
        int PagesRequested,
        int PagesReturned,
        int FailedBatches,
        int MissingPages,
        StructuredStats Stats
    );

    /// <summary>
    /// Plans the comic into scenes, persists them with their page budgets, then
    /// scripts each scene's pages in deterministic batches sized to fit the
    /// model's output budget. Scenes are the unit of continuity: pages inside
    /// one flow into each other, and every batch is handed the pages before it
    /// so a split scene still reads as one continuous moment.
    /// </summary>
    private async Task<ScriptingOutcome> ScriptPagesAsync(
        AppDbContext db,
        Curator curator,
        ComicProject project,
        string storyText,
        IReadOnlyList<Character> characters,
        CuratorContext context,
        StageTimer timer,
        CancellationToken cancellationToken
    )
    {
        // Replace any previous plan; curation is idempotent by design.
        var existingPages = await db
            .ComicPages.Where(page => page.ProjectId == project.Id)
            .ToListAsync(cancellationToken);

        if (existingPages.Count > 0)
        {
            db.ComicPages.RemoveRange(existingPages);
            await db.SaveChangesAsync(cancellationToken);
        }

        var existingScenes = await db
            .Scenes.Where(scene => scene.ProjectId == project.Id)
            .ToListAsync(cancellationToken);

        if (existingScenes.Count > 0)
        {
            db.Scenes.RemoveRange(existingScenes);
            await db.SaveChangesAsync(cancellationToken);
        }

        var environments = await db
            .Environments.Where(environment => environment.StoryId == project.StoryId)
            .ToListAsync(cancellationToken);

        var words = StoryPath.WordCount(storyText);
        var totalPages = Math.Clamp((int)Math.Ceiling(words / 120.0), 4, 40);

        var planTimer = Stopwatch.StartNew();
        var plan = await curator.PlanScenesAsync(
            storyText,
            totalPages,
            environments.Select(environment => environment.Name).ToList(),
            context,
            cancellationToken
        );

        var planned = plan.Scenes;

        if (planned.Count == 0)
        {
            // A failed scene plan must not dead-end the curation: one scene
            // covering the whole book still scripts.
            logger.LogWarning(
                "Scene planning returned nothing; scripting project {ProjectId} as one scene.",
                project.Id
            );
            planned =
            [
                new ScriptedScene
                {
                    Index = 0,
                    Title = project.Title,
                    Summary = project.Premise,
                    PageCount = totalPages,
                },
            ];
        }

        await RecordAsync(
            db,
            project,
            CurationStage.SceneSegmentation,
            context.Model.Id,
            new
            {
                scenes = planned.Count,
                contentChars = plan.Stats.ContentChars,
                reasoningChars = plan.Stats.ReasoningChars,
                attempts = plan.Stats.Attempts,
                truncated = plan.Stats.Truncated,
            },
            timer,
            plan.Error,
            cancellationToken
        );

        logger.LogInformation(
            "Planned {Scenes} scene(s) over {Pages} page(s) in {Ms}ms.",
            planned.Count,
            totalPages,
            planTimer.ElapsedMilliseconds
        );

        var scenes = await PersistScenesAsync(db, project, planned, environments, cancellationToken);

        await ProgressAsync(
            project,
            CurationStage.SceneSegmentation,
            $"Planned {scenes.Count} scene(s) over {totalPages} page(s).",
            30
        );

        var styleSpec = $"{project.Style.Name}: {project.Style.BaseTags}";
        var palette = project.PaletteNotes ?? "(not set)";

        var written = new List<ComicPage>();
        string? continuity = null;
        var firstPage = 1;

        // The effective output ceiling is whichever is lower: NovelAI's chat
        // clamp or the value configured here after a transport fix raises it.
        var batchSize = Curator.ScriptBatchSize(
            configuration.GetValue(
                "Curator:EffectiveOutputTokens",
                Curator.ConservativeOutputCeiling
            )
        );

        var batches = 0;
        var pagesRequested = 0;
        var pagesReturned = 0;
        var failedBatches = 0;
        var missingPages = 0;
        var stats = default(StructuredStats);

        for (var index = 0; index < scenes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scene = scenes[index];
            var source = planned[index];
            var sceneTimer = Stopwatch.StartNew();
            var environmentCanon = EnvironmentCanon(
                ResolveEnvironment(environments, source.Environment)
            );

            async Task<PageScriptOutcome> RequestAsync(
                PageScriptBatch batch,
                CancellationToken token
            )
            {
                try
                {
                    return await curator.ScriptPagesAsync(
                        batch.FirstPage,
                        batch.Count,
                        source,
                        characters,
                        environmentCanon,
                        // Only the tail matters for flow, and it must include
                        // the pages this same scene already scripted.
                        written.TakeLast(2).ToList(),
                        styleSpec,
                        palette,
                        context,
                        token
                    );
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // One unscriptable batch must not sink the whole plan, but
                    // it must be recorded: the failure callback turns this into
                    // a failed CurationRun instead of a silent skip.
                    return new PageScriptOutcome([], exception.Message, default);
                }
            }

            async Task PersistAsync(PageScriptBatch batch, PageScriptOutcome outcome)
            {
                var pageIndex = batch.FirstPage - 1;
                var persisted = 0;

                foreach (var scriptedPage in outcome.Pages.Take(batch.Count))
                {
                    var environment = ResolveEnvironment(environments, scriptedPage.Environment);
                    var page = new ComicPage
                    {
                        ProjectId = project.Id,
                        Index = pageIndex,
                        SceneId = scene.Id,
                        EnvironmentId = environment?.Id,
                        MovesToEnvironmentId = ResolveEnvironment(environments, scriptedPage.MovesTo)?.Id,
                        SceneTitle = scriptedPage.SceneTitle,
                        // The contract calls this prompt, but a page is many panels
                        // now: the text is this page's script, and the panel plan is
                        // kept structured beside it.
                        Script = scriptedPage.Prompt,
                        PanelPlanJson = JsonSerializer.Serialize(scriptedPage.Panels),
                        // A page without its own ledger inherits the running one.
                        Continuity = string.IsNullOrWhiteSpace(scriptedPage.Continuity)
                            ? continuity
                            : scriptedPage.Continuity,
                        LayoutTemplateId = "grid-2x2",
                        Status = PageStatus.Planned,
                    };

                    AddDialogue(page, scriptedPage.Bubbles, characters);

                    db.ComicPages.Add(page);
                    written.Add(page);
                    persisted++;

                    if (!string.IsNullOrWhiteSpace(scriptedPage.Continuity))
                    {
                        continuity = scriptedPage.Continuity;
                    }

                    pageIndex++;
                }

                if (persisted > 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            async Task RecordFailureAsync(PageScriptBatch batch, PageScriptOutcome outcome)
            {
                var lastPage = batch.FirstPage + batch.Count - 1;

                logger.LogWarning(
                    "Scene {Scene} pages {First}-{Last} failed: {Error}",
                    scene.Index,
                    batch.FirstPage,
                    lastPage,
                    outcome.Error
                );

                await RecordAsync(
                    db,
                    project,
                    CurationStage.PanelBreakdown,
                    context.Model.Id,
                    new
                    {
                        scene = scene.Index,
                        sceneTitle = scene.Title,
                        firstPage = batch.FirstPage,
                        lastPage,
                        pagesRequested = batch.Count,
                        pagesReturned = 0,
                        contentChars = outcome.ContentChars,
                        reasoningChars = outcome.ReasoningChars,
                        attempts = outcome.Attempts,
                        truncated = outcome.Truncated,
                    },
                    timer,
                    $"Scene {scene.Index} \"{scene.Title}\" pages {batch.FirstPage}-{lastPage}: "
                        + $"{outcome.Error ?? "no pages returned"}",
                    cancellationToken
                );
            }

            var run = await Curator.ScriptBatchesAsync(
                firstPage,
                source.PageCount,
                batchSize,
                RequestAsync,
                PersistAsync,
                RecordFailureAsync,
                cancellationToken
            );

            // A truncation or failure means the model's effective output is
            // smaller than estimated; keep later batches at the learned size.
            batchSize = run.BatchSize;

            batches += run.Batches;
            pagesRequested += run.PagesRequested;
            pagesReturned += run.PagesReturned;
            failedBatches += run.FailedBatches;
            missingPages += run.MissingPages;
            stats += run.Stats;

            // The page cursor advances by the scene's budget, even when batches
            // failed, so later scenes keep their planned ranges.
            firstPage += source.PageCount;

            logger.LogInformation(
                "Scripted scene {Scene} \"{Title}\": {Pages} of {Budget} page(s) in {Ms}ms.",
                scene.Index,
                scene.Title,
                run.PagesReturned,
                source.PageCount,
                sceneTimer.ElapsedMilliseconds
            );

            await ProgressAsync(
                project,
                CurationStage.PanelBreakdown,
                failedBatches == 0
                    ? $"Scripted {written.Count} of {totalPages} page(s)."
                    : $"Scripted {written.Count} of {totalPages} page(s); "
                        + $"{failedBatches} batch(es) failed.",
                Math.Clamp(30 + (int)(50.0 * written.Count / totalPages), 30, 80)
            );
        }

        if (written.Count > 0)
        {
            var fixedSeams = await FixSeamsAsync(db, curator, project, context, cancellationToken);
            if (fixedSeams > 0)
            {
                await ProgressAsync(
                    project,
                    CurationStage.PanelBreakdown,
                    $"Smoothed {fixedSeams} page transition(s).",
                    85
                );
            }
        }

        return new ScriptingOutcome(
            written.Count,
            batches,
            pagesRequested,
            pagesReturned,
            failedBatches,
            missingPages,
            stats
        );
    }

    private static async Task<List<Scene>> PersistScenesAsync(
        AppDbContext db,
        ComicProject project,
        IReadOnlyList<ScriptedScene> planned,
        IReadOnlyList<Environment> environments,
        CancellationToken cancellationToken
    )
    {
        var scenes = new List<Scene>(planned.Count);
        var pageStart = 0;

        for (var index = 0; index < planned.Count; index++)
        {
            var source = planned[index];
            var scene = new Scene
            {
                ProjectId = project.Id,
                Index = index,
                Title = string.IsNullOrWhiteSpace(source.Title)
                    ? $"Scene {index + 1}"
                    : source.Title.Trim(),
                Summary = source.Summary,
                EnvironmentId = ResolveEnvironment(environments, source.Environment)?.Id,
                TimeOfDay = source.TimeOfDay,
                Mood = source.Mood,
                Purpose = source.Purpose,
                // The next scene starts exactly where this one's budget ends.
                PageStart = pageStart,
                PageCount = source.PageCount,
            };

            pageStart += source.PageCount;
            scenes.Add(scene);
            db.Scenes.Add(scene);
        }

        await db.SaveChangesAsync(cancellationToken);
        return scenes;
    }

    /// <summary>
    /// Rewrites the opening sentence of the pages the seam pass flagged so each
    /// one follows from the page before. Pages that were not flagged are not
    /// touched.
    /// </summary>
    private async Task<int> FixSeamsAsync(
        AppDbContext db,
        Curator curator,
        ComicProject project,
        CuratorContext context,
        CancellationToken cancellationToken
    )
    {
        var pages = await db
            .ComicPages.Where(page => page.ProjectId == project.Id)
            .OrderBy(page => page.Index)
            .ToListAsync(cancellationToken);

        if (pages.Count < 2)
        {
            return 0;
        }

        var timer = Stopwatch.StartNew();
        Dictionary<int, string> fixes;

        try
        {
            fixes = await curator.FixSeamsAsync(pages, context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The pages are already scripted; a failed polish pass must not cost
            // the whole plan.
            logger.LogWarning(exception, "Seam pass failed for project {ProjectId}.", project.Id);
            return 0;
        }

        var fixedCount = 0;

        foreach (var (index, opening) in fixes)
        {
            if (string.IsNullOrWhiteSpace(opening))
            {
                continue;
            }

            // The seam pass numbered pages as page.Index + 1, so map that back;
            // a model that echoes zero-based indices still finds its page.
            var page =
                pages.FirstOrDefault(candidate => candidate.Index + 1 == index)
                ?? pages.FirstOrDefault(candidate => candidate.Index == index);

            if (page is null)
            {
                continue;
            }

            page.Script = ReplaceOpening(page.Script, opening);
            fixedCount++;
        }

        if (fixedCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Fixed {Count} seam(s) for project {ProjectId} in {Ms}ms.",
            fixedCount,
            project.Id,
            timer.ElapsedMilliseconds
        );

        return fixedCount;
    }

    /// <summary>Swaps a page's first sentence for a rewritten opening.</summary>
    private static string ReplaceOpening(string? script, string opening)
    {
        var start = opening.Trim();
        if (start.Length > 0 && start[^1] is not ('.' or '!' or '?' or '…' or '"'))
        {
            start += ".";
        }

        if (string.IsNullOrWhiteSpace(script))
        {
            return start;
        }

        var stop = script.IndexOf(". ", StringComparison.Ordinal);
        return stop > 0 ? $"{start} {script[(stop + 2)..].TrimStart()}" : start;
    }

    private static void AddDialogue(
        ComicPage page,
        IReadOnlyList<ScriptedBubble> bubbles,
        IReadOnlyList<Character> characters
    )
    {
        var lineIndex = 0;

        foreach (var bubble in bubbles)
        {
            if (string.IsNullOrWhiteSpace(bubble.Text))
            {
                continue;
            }

            page.Dialogue.Add(
                new DialogueLine
                {
                    Index = lineIndex++,
                    Kind = ParseDialogueKind(bubble.Kind),
                    SpeakerCharacterId = bubble.Speaker is { } speaker
                        ? characters.FirstOrDefault(character => Matches(character, speaker))?.Id
                        : null,
                    // Drawn, not typeset: long lines come back unreadable.
                    Text = Shorten(bubble.Text, 60),
                    Position = bubble.Position,
                }
            );
        }
    }

    /// <summary>Keeps a drawn line inside what a bubble can hold legibly.</summary>
    private static string Shorten(string text, int limit)
    {
        var trimmed = text.Trim().Trim('"');

        if (trimmed.Length <= limit)
        {
            return trimmed;
        }

        // Cut on a word boundary so the line still reads as speech.
        var cut = trimmed.LastIndexOf(' ', Math.Min(limit, trimmed.Length - 1));
        return (cut > limit / 2 ? trimmed[..cut] : trimmed[..limit]).TrimEnd(' ', ',', ';') + "…";
    }

    public static List<LayoutPanelSlot> ParseSlots(PageLayoutTemplate? template)
    {
        if (template is null || string.IsNullOrWhiteSpace(template.SlotsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<LayoutPanelSlot>>(template.SlotsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DialogueKind ParseDialogueKind(string? kind) =>
        Enum.TryParse<DialogueKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : DialogueKind.Speech;

    private async Task ProgressAsync(
        ComicProject project,
        CurationStage stage,
        string message,
        int percent
    )
    {
        await hub
            .Clients.Group(GenerationHub.GroupFor(project.UserId))
            .SendAsync(
                ImageEvents.CurationProgress,
                new { projectId = project.Id, stage = stage.ToString(), message, percent },
                CancellationToken.None
            );
    }

    /// <summary>
    /// Times each recorded stage as the delta since the previous record. The
    /// stopwatch that used to be shared across the run measured total elapsed
    /// time, so every CurationRun row read as the whole curation ("every stage
    /// takes 150 seconds"). Recording the delta makes the rows usable.
    /// </summary>
    private sealed class StageTimer
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private long lastMs;

        public long ElapsedMs => stopwatch.ElapsedMilliseconds;

        /// <summary>Takes the delta and resets the marker.</summary>
        public long Mark()
        {
            var now = stopwatch.ElapsedMilliseconds;
            var elapsed = now - lastMs;
            lastMs = now;
            return elapsed;
        }
    }

    /// <summary>
    /// Stable hash of the exact prose discovery read. It is stored inside the
    /// discovery run's ResultJson (no schema change) so an unchanged story can
    /// skip rediscovery on the next pass.
    /// </summary>
    private static string StoryHash(string storyText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storyText)));

    /// <summary>
    /// The hash recorded by the most recent successful discovery pass, or null
    /// when this project has never been discovered. Ordered in memory because
    /// SQLite cannot ORDER BY a DateTimeOffset column.
    /// </summary>
    private static async Task<string?> FindDiscoveryHashAsync(
        AppDbContext db,
        Guid projectId,
        CancellationToken cancellationToken
    )
    {
        var runs = await db
            .CurationRuns.AsNoTracking()
            .Where(run =>
                run.ProjectId == projectId
                && run.Stage == CurationStage.CastDiscovery
                && run.Status == CurationStatus.Succeeded
            )
            .ToListAsync(cancellationToken);

        var latest = runs.OrderByDescending(run => run.CreatedAt).FirstOrDefault();

        if (latest?.ResultJson is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(latest.ResultJson);
            return document.RootElement.TryGetProperty("storyHash", out var hash)
                ? hash.GetString()
                : null;
        }
        catch (JsonException)
        {
            // A legacy or hand-edited row is not a hash.
            return null;
        }
    }

    private static async Task<CurationRun> RecordAsync(
        AppDbContext db,
        ComicProject project,
        CurationStage stage,
        string modelId,
        object result,
        StageTimer timer,
        string? error,
        CancellationToken cancellationToken
    )
    {
        var run = new CurationRun
        {
            ProjectId = project.Id,
            UserId = project.UserId,
            Stage = stage,
            Status = error is null ? CurationStatus.Succeeded : CurationStatus.Failed,
            ModelId = modelId,
            ResultJson = JsonSerializer.Serialize(result),
            Error = error,
            FinishedAt = DateTimeOffset.UtcNow,
            DurationMs = timer.Mark(),
        };

        db.CurationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    private async Task FailAsync(AppDbContext db, ComicProject project, string message)
    {
        project.Status = ComicStatus.Draft;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogWarning("Curation stopped for project {ProjectId}: {Message}", project.Id, message);

        await hub
            .Clients.Group(GenerationHub.GroupFor(project.UserId))
            .SendAsync(
                ImageEvents.CurationProgress,
                new
                {
                    projectId = project.Id,
                    stage = nameof(CurationStage.Done),
                    message,
                    percent = 0,
                    failed = true,
                },
                CancellationToken.None
            );
    }

    private async Task TrySetStatusAsync(Guid projectId, ComicStatus status)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await db.ComicProjects.FirstOrDefaultAsync(item => item.Id == projectId);
            if (project is null)
            {
                return;
            }

            project.Status = status;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not reset project {ProjectId} status.", projectId);
        }
    }
}
