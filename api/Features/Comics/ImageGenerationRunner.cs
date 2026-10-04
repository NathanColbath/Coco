using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ComicStudio.Api.Comics;
using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Hubs;
using ComicStudio.Api.NovelAi;
using ComicStudio.Api.NovelAi.Image;
using ComicStudio.Api.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Comics;

/// <summary>
/// Runs image jobs off the request thread: panels, character sheets and covers
/// all queue through the same gate.
///
/// NovelAI deprecated concurrent generation, so requests are serialised: a batch
/// of pages is drawn one panel at a time, and progress is pushed to the owner.
/// </summary>
public class ImageGenerationRunner(
    IServiceScopeFactory scopeFactory,
    IHubContext<GenerationHub> hub,
    IConfiguration configuration,
    ILogger<ImageGenerationRunner> logger
)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> active = new();

    public void Start(Guid generationId) => _ = Task.Run(() => RunAsync(generationId));

    public bool Cancel(Guid generationId)
    {
        if (active.TryGetValue(generationId, out var source))
        {
            source.Cancel();
            return true;
        }

        return false;
    }

    private async Task RunAsync(Guid generationId)
    {
        using var cancellation = new CancellationTokenSource();
        active[generationId] = cancellation;
        var stopwatch = Stopwatch.StartNew();
        var acquired = false;

        try
        {
            await Gate.WaitAsync(cancellation.Token);
            acquired = true;

            using var scope = scopeFactory.CreateScope();
            var context = new RunContext(
                scope.ServiceProvider,
                hub,
                await scope
                    .ServiceProvider.GetRequiredService<AppDbContext>()
                    .PanelGenerations.FirstOrDefaultAsync(
                        item => item.Id == generationId,
                        cancellation.Token
                    ) ?? throw new InvalidOperationException("Generation not found."),
                stopwatch
            );

            if (!await context.InitialiseAsync(cancellation.Token))
            {
                return;
            }

            switch (context.Generation.Kind)
            {
                case ImageJobKind.CharacterSheet:
                    await RunSheetAsync(context, cancellation.Token);
                    break;
                case ImageJobKind.Cover:
                    await RunCoverAsync(context, cancellation.Token);
                    break;
                case ImageJobKind.Page:
                    await RunPageAsync(context, cancellation.Token);
                    break;
                default:
                    await context.FailAsync("Unsupported image job.", cancellation.Token);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await TryMarkAsync(generationId, PanelGenerationStatus.Cancelled, "Cancelled.", stopwatch);
            await NotifyFailureAsync(generationId, "Cancelled.");
        }
        catch (ImageGenerationException exception)
        {
            logger.LogWarning("Image job {GenerationId} failed: {Message}", generationId, exception.Message);
            await TryMarkAsync(generationId, PanelGenerationStatus.Failed, exception.Message, stopwatch);
            await NotifyFailureAsync(generationId, exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Image job {GenerationId} failed unexpectedly.", generationId);
            const string message = "Image generation failed unexpectedly. Check the API logs.";
            await TryMarkAsync(generationId, PanelGenerationStatus.Failed, message, stopwatch);
            await NotifyFailureAsync(generationId, message);
        }
        finally
        {
            if (acquired)
            {
                Gate.Release();
            }

            active.TryRemove(generationId, out _);
        }
    }

    // --- Pages --------------------------------------------------------------

    /// <summary>
    /// Draws a whole page in one generation. The page's script describes every
    /// panel, and any lettering is drawn by the model rather than typeset.
    /// </summary>
    private async Task RunPageAsync(RunContext context, CancellationToken cancellationToken)
    {
        var page = await context.Db
            .ComicPages.Include(item => item.Dialogue)
                .ThenInclude(line => line.Speaker)
            .FirstOrDefaultAsync(item => item.Id == context.Generation.TargetId, cancellationToken);

        if (page is null || context.Project is null)
        {
            await context.FailAsync("The page or project no longer exists.", cancellationToken);
            return;
        }

        var project = context.Project;

        var cast = await context.Db
            .Characters.Where(character => character.StoryId == project.StoryId)
            .ToListAsync(cancellationToken);

        var composed = context.PageComposer.Compose(
            page,
            project,
            cast,
            context.Generation.Seed,
            context.Generation.IsHighResolution
        );

        // Text rendering is what V5 was trained for, so under Auto a page with
        // lettering draws on V5 and a page without draws on V4.5. The project
        // policy can pin a family; a per-page override beats everything.
        var model = await context.ResolvePreferredAsync(
            PreferredPageModels(project, page, composed.HasText),
            project.Style.ModelId,
            context.User.PreferredImageModelId,
            cancellationToken
        );

        var profile = ImageModelResolver.Profile(model);
        var references = await LoadPageReferencesAsync(context, page, project, cancellationToken);

        // Record the resolved plan in the notes so the workspace can show
        // "will use V5, 4 references" before anything is spent.
        composed = composed with
        {
            Notes = context.PageComposer.BuildNotes(
                page,
                project,
                context.Generation.IsHighResolution,
                $"{model.DisplayName} ({model.Id})",
                references.Count
            ),
        };

        var request = context.BuildRequest(composed, profile, references, useCharacterPositions: false);
        await context.MarkRunningAsync(request, composed, cancellationToken);

        page.Status = PageStatus.Generating;
        await context.Db.SaveChangesAsync(cancellationToken);

        var result = await context.SendAsync(request, cancellationToken);
        var asset = await context.StoreAsync(result, ImageAssetKind.PageArt, cancellationToken);

        page.PageImageId = asset.Id;
        page.PositivePrompt = composed.Positive;
        page.NegativePrompt = composed.Negative;
        page.PromptSnapshotJson = JsonSerializer.Serialize(composed);
        page.Seed = composed.Seed;
        page.ModelUsed = model.Id;
        page.Status = PageStatus.Generated;
        project.UpdatedAt = DateTimeOffset.UtcNow;

        if (project.Status is ComicStatus.Planned or ComicStatus.Draft)
        {
            project.Status = ComicStatus.Generating;
        }

        await context.CompleteAsync(asset, cancellationToken);
        await context.Db.SaveChangesAsync(CancellationToken.None);

        await context.PublishCompletedAsync(
            new
            {
                generationId = context.Generation.Id,
                pageId = page.Id,
                imageId = asset.Id,
                kind = nameof(ImageJobKind.Page),
                model = model.Id,
                hasText = composed.HasText,
                width = asset.Width,
                height = asset.Height,
                costAnlas = context.Generation.CostAnlas,
                wasFree = context.Generation.WasFree,
                durationMs = context.Generation.DurationMs,
            }
        );
    }

    /// <summary>
    /// The registry ids this page should try, best first. The project policy
    /// picks the family (Auto follows the lettering), and a per-page override
    /// always wins when it is set.
    /// </summary>
    private static IReadOnlyList<string> PreferredPageModels(
        ComicProject project,
        ComicPage page,
        bool hasText
    )
    {
        string[] v5 = ["nai-diffusion-5-full", "nai-diffusion-5-curated"];
        string[] v4Point5 = ["nai-diffusion-4-5-full", "nai-diffusion-4-5-curated"];

        if (!string.IsNullOrWhiteSpace(page.ModelOverride))
        {
            return [page.ModelOverride.Trim()];
        }

        return project.ImageModelPolicy switch
        {
            ImageModelPolicy.AlwaysV5 => v5,
            ImageModelPolicy.AlwaysV4Point5 => v4Point5,
            ImageModelPolicy.Explicit when !string.IsNullOrWhiteSpace(project.ExplicitImageModelId) =>
                [project.ExplicitImageModelId.Trim()],
            _ => hasText ? v5 : v4Point5,
        };
    }

    /// <summary>
    /// References for the page, best first: at most two character sheets, the
    /// project's style anchor, the page's environment plate, then the previous
    /// page. Four in total; anything past that is dropped from the tail.
    /// </summary>
    private async Task<List<ReferenceImage>> LoadPageReferencesAsync(
        RunContext context,
        ComicPage page,
        ComicProject project,
        CancellationToken cancellationToken
    )
    {
        // NovelAI answers 500 to every request carrying a director reference: we
        // hold full page renders at 1472x2048, and it requires exactly
        // 1024x1536, 1536x1024 or 1472x1472 padded with black. Measured on real
        // runs, 46 of 46 generations without a reference succeeded and 19 of 19
        // with one failed. A page drawn from the script, cast tags and ledger
        // alone is worth far more than no page at all, so references are off by
        // default. Turn them back on with Generation:AttachReferences once
        // normalisation has been confirmed against the live API.
        if (!configuration.GetValue("Generation:AttachReferences", false))
        {
            return [];
        }

        const int maxReferences = 4;
        var candidates = new List<(string Label, ReferenceImage Image)>();
        var script = page.Script ?? string.Empty;

        var cast = await context.Db
            .Characters.Where(character => character.StoryId == project.StoryId)
            .ToListAsync(cancellationToken);

        var onPage = cast.Where(character =>
            script.Contains(character.Name, StringComparison.OrdinalIgnoreCase)
            || page.Dialogue.Any(line => line.SpeakerCharacterId == character.Id)
        );

        var characterReferences = 0;
        foreach (var character in onPage)
        {
            if (characterReferences >= 2)
            {
                break;
            }

            var sheet = await context.Db
                .CharacterSheetImages.Include(image => image.Image)
                .Where(item => item.CharacterId == character.Id && item.IsLocked)
                .OrderBy(item => item.Kind)
                .FirstOrDefaultAsync(cancellationToken);

            if (sheet?.Image is null)
            {
                continue;
            }

            var bytes = await context.ReadAssetAsync(sheet.Image, cancellationToken);
            if (bytes is not null)
            {
                candidates.Add((
                    $"character sheet for {character.Name}",
                    new ReferenceImage(bytes, ReferenceKind.Character, 0.8, 0.5)
                ));
                characterReferences++;
            }
        }

        // The anchor starts as the first drawn page, then sticks to the project
        // so later redraws cannot move it.
        if (project.StyleAnchorImageId is null)
        {
            var fallback = await context.Db
                .ComicPages.AsNoTracking()
                .Where(item => item.ProjectId == project.Id && item.PageImageId != null)
                .OrderBy(item => item.Index)
                .Select(item => item.PageImageId!.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (fallback != Guid.Empty)
            {
                project.StyleAnchorImageId = fallback;
                await context.Db.SaveChangesAsync(cancellationToken);
            }
        }

        if (project.StyleAnchorImageId is { } styleId)
        {
            var anchor = await context.Db.ImageAssets.FindAsync(new object[] { styleId }, cancellationToken);
            if (anchor is not null)
            {
                var bytes = await context.ReadAssetAsync(anchor, cancellationToken);
                if (bytes is not null)
                {
                    candidates.Add((
                        "style anchor",
                        new ReferenceImage(bytes, ReferenceKind.Style, 0.5, 0.3)
                    ));
                }
            }
        }

        if (page.EnvironmentId is { } environmentId)
        {
            var plateId = await context.Db
                .Environments.AsNoTracking()
                .Where(item => item.Id == environmentId)
                .Select(item => item.PlateImageId)
                .FirstOrDefaultAsync(cancellationToken);

            if (plateId is { } plate)
            {
                var asset = await context.Db.ImageAssets.FindAsync(new object[] { plate }, cancellationToken);
                if (asset is not null)
                {
                    var bytes = await context.ReadAssetAsync(asset, cancellationToken);
                    if (bytes is not null)
                    {
                        candidates.Add((
                            "environment plate",
                            new ReferenceImage(bytes, ReferenceKind.Style, 0.4, 0.3)
                        ));
                    }
                }
            }
        }

        var previous = await context.Db
            .ComicPages.AsNoTracking()
            .Where(item => item.ProjectId == project.Id
                && item.Index == page.Index - 1
                && item.PageImageId != null)
            .Select(item => item.PageImageId!.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (previous != Guid.Empty)
        {
            var asset = await context.Db.ImageAssets.FindAsync(new object[] { previous }, cancellationToken);
            if (asset is not null)
            {
                var bytes = await context.ReadAssetAsync(asset, cancellationToken);
                if (bytes is not null)
                {
                    candidates.Add((
                        "previous page",
                        new ReferenceImage(bytes, ReferenceKind.Style, 0.35, 0.2)
                    ));
                }
            }
        }

        if (candidates.Count > maxReferences)
        {
            var dropped = candidates.Skip(maxReferences).Select(item => item.Label).ToList();
            logger.LogInformation(
                "Page {PageId} keeps the first {Kept} of {Total} references; dropped: {Dropped}.",
                page.Id,
                maxReferences,
                candidates.Count,
                string.Join(", ", dropped)
            );
        }

        return candidates.Take(maxReferences).Select(item => item.Image).ToList();
    }

    // --- Character sheets ---------------------------------------------------

    private async Task RunSheetAsync(RunContext context, CancellationToken cancellationToken)
    {
        var character = await context.Db.Characters.FirstOrDefaultAsync(
            item => item.Id == context.Generation.TargetId,
            cancellationToken
        );

        if (character is null)
        {
            await context.FailAsync("The character no longer exists.", cancellationToken);
            return;
        }

        var project = context.Project;
        var style = project?.Style ?? new ComicStylePreset();
        var kind = context.Generation.SheetKind ?? CharacterSheetKind.Portrait;

        var model = await context.ResolveModelAsync(
            project?.Style.ModelId,
            context.User.PreferredImageModelId,
            cancellationToken
        );
        var profile = ImageModelResolver.Profile(model);

        // Sheets are not panels: they must show every view, and a clean
        // background is what makes them usable as a reference.
        var prompt = SheetPrompt(character, kind, style);
        var negative =
            "color, watermark, signature, jpeg artifacts, lowres, bad anatomy, extra digits, "
            + "multiple characters, extra people, text";

        var aspect = kind == CharacterSheetKind.FullBody ? 1024.0 / 1536.0 : 1.0;
        var print = ResolutionPolicy.ForAspect(aspect, ResolutionPolicy.MaxPixelBudget);

        var composed = new ComposedPage(
            prompt,
            negative,
            [
                new CharacterPrompt(
                    character.Visual.PromptTags ?? character.Name,
                    0.5,
                    0.5,
                    character.Visual.NegativeTags
                ),
            ],
            print.Width,
            print.Height,
            Math.Max(style.Steps, 28),
            style.Scale,
            style.Sampler,
            style.NoiseSchedule,
            style.UcPreset,
            style.QualityToggle,
            context.Generation.Seed ?? Random.Shared.NextInt64(0, 4_294_967_295),
            false,
            [$"character sheet reference at {print.Width}×{print.Height}"]
        );

        var request = context.BuildRequest(composed, profile, []);
        await context.MarkRunningAsync(request, composed, cancellationToken);

        var result = await context.SendAsync(request, cancellationToken);
        var asset = await context.StoreAsync(result, ImageAssetKind.CharacterSheet, cancellationToken);

        var sheet = new CharacterSheetImage
        {
            CharacterId = character.Id,
            ImageAssetId = asset.Id,
            Kind = kind,
            PromptUsed = prompt,
            Seed = composed.Seed,
            ModelId = model.ProviderModelId,
            IsLocked = false,
        };

        context.Db.CharacterSheetImages.Add(sheet);
        character.UpdatedAt = DateTimeOffset.UtcNow;
        await context.CompleteAsync(asset, cancellationToken);
        await context.Db.SaveChangesAsync(CancellationToken.None);

        await context.PublishCompletedAsync(
            new
            {
                generationId = context.Generation.Id,
                characterId = character.Id,
                sheetId = sheet.Id,
                imageId = asset.Id,
                kind = nameof(ImageJobKind.CharacterSheet),
                width = asset.Width,
                height = asset.Height,
                costAnlas = context.Generation.CostAnlas,
                wasFree = context.Generation.WasFree,
                durationMs = context.Generation.DurationMs,
            }
        );
    }

    private static string SheetPrompt(
        Character character,
        CharacterSheetKind kind,
        ComicStylePreset style
    )
    {
        var subject = character.Visual.PromptTags ?? character.Name;
        var outfit = character.Visual.OutfitTags ?? string.Empty;

        var view = kind switch
        {
            CharacterSheetKind.FullBody =>
                "full body, standing, facing viewer, neutral pose, simple background, reference sheet",
            CharacterSheetKind.ExpressionSheet =>
                "expression sheet, multiple expressions, face focus, simple background",
            _ => "portrait, upper body, facing viewer, neutral expression, simple background, detailed face",
        };

        return string.Join(
            ", ",
            new[] { style.BaseTags, view, subject, outfit }
                .Where(part => !string.IsNullOrWhiteSpace(part))
        );
    }

    // --- Covers -------------------------------------------------------------

    private async Task RunCoverAsync(RunContext context, CancellationToken cancellationToken)
    {
        var cover = await context.Db.CoverPlans.FirstOrDefaultAsync(
            item => item.Id == context.Generation.TargetId,
            cancellationToken
        );

        if (cover is null || context.Project is null)
        {
            await context.FailAsync("The cover plan no longer exists.", cancellationToken);
            return;
        }

        // Cover art is the one place quality is worth paying for.
        var model = await context.ResolveModelAsync(
            context.Project.Style.ModelId,
            context.User.PreferredImageModelId,
            cancellationToken,
            preferBest: true
        );
        var profile = ImageModelResolver.Profile(model);

        var characters = await context.Db
            .Characters.Where(character => character.StoryId == context.Project.StoryId)
            .ToListAsync(cancellationToken);

        var references = new List<ReferenceImage>();
        foreach (var character in characters.Take(2))
        {
            var sheet = await context.Db
                .CharacterSheetImages.Include(image => image.Image)
                .Where(item => item.CharacterId == character.Id && item.IsLocked)
                .FirstOrDefaultAsync(cancellationToken);

            if (sheet?.Image is null)
            {
                continue;
            }

            var bytes = await context.ReadAssetAsync(sheet.Image, cancellationToken);
            if (bytes is not null)
            {
                references.Add(new ReferenceImage(bytes, ReferenceKind.Character, 0.7, 0.4));
            }
        }

        var style = context.Project.Style;
        var baseCaption = string.Join(
            ", ",
            new[]
            {
                style.BaseTags,
                "cover art, key visual, dramatic composition",
                cover.Prompt,
                characters.Count > 0
                    ? string.Join(", ", characters.Take(3).Select(item => item.Visual.PromptTags ?? item.Name))
                    : null,
            }.Where(part => !string.IsNullOrWhiteSpace(part))
        );

        var negative = string.Join(
            ", ",
            new[] { style.NegativeTags, cover.NegativePrompt }
                .Where(part => !string.IsNullOrWhiteSpace(part))
        );

        baseCaption = ContentModifiers.ApplyToPositive(baseCaption, context.Project.Rating);
        negative = ContentModifiers.ApplyToNegative(negative, context.Project.Rating);

        var print = ResolutionPolicy.ForAspect(0.70, ResolutionPolicy.MaxPixelBudget);

        var composed = new ComposedPage(
            baseCaption,
            negative,
            [],
            print.Width,
            print.Height,
            Math.Max(style.Steps, 28),
            style.Scale,
            style.Sampler,
            style.NoiseSchedule,
            style.UcPreset,
            style.QualityToggle,
            context.Generation.Seed ?? Random.Shared.NextInt64(0, 4_294_967_295),
            false,
            [$"cover at {print.Width}×{print.Height}"]
        );

        var request = context.BuildRequest(composed, profile, references);
        await context.MarkRunningAsync(request, composed, cancellationToken);

        var result = await context.SendAsync(request, cancellationToken);
        var asset = await context.StoreAsync(result, ImageAssetKind.Cover, cancellationToken);

        cover.ImageAssetId = asset.Id;
        cover.Seed = composed.Seed;
        cover.ModelId = model.ProviderModelId;
        cover.UpdatedAt = DateTimeOffset.UtcNow;
        context.Project.CoverImageId = asset.Id;
        context.Project.UpdatedAt = DateTimeOffset.UtcNow;

        await context.CompleteAsync(asset, cancellationToken);
        await context.Db.SaveChangesAsync(CancellationToken.None);

        await context.PublishCompletedAsync(
            new
            {
                generationId = context.Generation.Id,
                coverId = cover.Id,
                imageId = asset.Id,
                kind = nameof(ImageJobKind.Cover),
                width = asset.Width,
                height = asset.Height,
                costAnlas = context.Generation.CostAnlas,
                wasFree = context.Generation.WasFree,
                durationMs = context.Generation.DurationMs,
            }
        );
    }

    // --- Shared plumbing ----------------------------------------------------

    private Task NotifyFailureAsync(Guid generationId, string message) =>
        PublishAsync(generationId, ImageEvents.Failed, message);

    private async Task PublishAsync(Guid generationId, string @event, string? message)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db
                .PanelGenerations.AsNoTracking()
                .Where(item => item.Id == generationId)
                .Select(item => new
                {
                    item.UserId,
                    item.Kind,
                    item.TargetId,
                })
                .FirstOrDefaultAsync();

            if (record is null)
            {
                return;
            }

            await hub
                .Clients.Group(GenerationHub.GroupFor(record.UserId))
                .SendAsync(
                    @event,
                    new
                    {
                        generationId,
                        targetId = record.TargetId,
                        kind = record.Kind.ToString(),
                        message,
                    },
                    CancellationToken.None
                );
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish {@Event} for {GenerationId}.", @event, generationId);
        }
    }

    private async Task TryMarkAsync(
        Guid generationId,
        PanelGenerationStatus status,
        string? error,
        Stopwatch stopwatch
    )
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var generation = await db.PanelGenerations.FirstOrDefaultAsync(
                item => item.Id == generationId
            );
            if (generation is null)
            {
                return;
            }

            generation.Status = status;
            generation.Error = error;
            generation.FinishedAt = DateTimeOffset.UtcNow;
            generation.DurationMs = stopwatch.ElapsedMilliseconds;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not record the outcome of {GenerationId}.", generationId);
        }
    }

    /// <summary>Everything a job needs, resolved once per run.</summary>
    private sealed class RunContext
    {
        private readonly IServiceProvider provider;
        private readonly IHubContext<GenerationHub> hub;
        private readonly Stopwatch stopwatch;
        private readonly INovelAiClient accounts;
        private readonly NovelAiImageClient client;

        public RunContext(
            IServiceProvider provider,
            IHubContext<GenerationHub> hub,
            PanelGeneration generation,
            Stopwatch stopwatch
        )
        {
            this.provider = provider;
            this.hub = hub;
            this.stopwatch = stopwatch;

            Db = provider.GetRequiredService<AppDbContext>();
            Resolver = provider.GetRequiredService<ImageModelResolver>();
            PageComposer = provider.GetRequiredService<PagePromptComposer>();
            Store = provider.GetRequiredService<ImageStore>();
            Generation = generation;
            accounts = provider.GetRequiredService<INovelAiClient>();
            client = provider.GetRequiredService<NovelAiImageClient>();
        }

        public AppDbContext Db { get; }

        public PagePromptComposer PageComposer { get; }

        public ImageModelResolver Resolver { get; }

        public ImageStore Store { get; }

        public PanelGeneration Generation { get; }

        public AppUser User { get; private set; } = null!;

        public ComicProject? Project { get; private set; }

        public string Token { get; private set; } = string.Empty;

        public async Task<bool> InitialiseAsync(CancellationToken cancellationToken)
        {
            User = await Db.Users.FirstOrDefaultAsync(
                item => item.Id == Generation.UserId,
                cancellationToken
            ) ?? throw new InvalidOperationException("User not found.");

            var protector = provider.GetRequiredService<SecretProtector>();
            Token = protector.Unprotect(User.Id, User.NovelAiTokenCipher) ?? string.Empty;

            if (string.IsNullOrEmpty(Token))
            {
                await FailAsync(
                    "NovelAI is not connected. Add a persistent token in Settings.",
                    cancellationToken
                );
                return false;
            }

            Project = await Db.ComicProjects
                .Include(item => item.Story)
                .FirstOrDefaultAsync(item => item.Id == Generation.ProjectId, cancellationToken);

            if (Project is null)
            {
                await FailAsync("The project no longer exists.", cancellationToken);
                return false;
            }

            return true;
        }

        /// <summary>Takes the first of the preferred ids that exists in the registry.</summary>
        public async Task<AiModel> ResolvePreferredAsync(
            IReadOnlyList<string> preferredIds,
            string? modelId,
            string? preferredModelId,
            CancellationToken cancellationToken
        )
        {
            foreach (var id in preferredIds)
            {
                var match = await Db
                    .AiModels.AsNoTracking()
                    .FirstOrDefaultAsync(
                        model => model.Id == id && !model.IsDeprecated,
                        cancellationToken
                    );

                if (match is not null)
                {
                    return match;
                }
            }

            return await Resolver.ResolveAsync(modelId, preferredModelId, cancellationToken);
        }

        public Task<AiModel> ResolveModelAsync(
            string? modelId,
            string? preferredModelId,
            CancellationToken cancellationToken,
            bool preferBest = false
        ) =>
            preferBest
                ? ResolveBestAsync(modelId, preferredModelId, cancellationToken)
                : Resolver.ResolveAsync(modelId, preferredModelId, cancellationToken);

        /// <summary>Cover art prefers the newest full model available.</summary>
        private async Task<AiModel> ResolveBestAsync(
            string? modelId,
            string? preferredModelId,
            CancellationToken cancellationToken
        )
        {
            var best = await Db
                .AiModels.Where(model =>
                    model.Kind == AiModelKind.Image
                    && !model.IsDeprecated
                    && model.Id == "nai-diffusion-5-full"
                )
                .FirstOrDefaultAsync(cancellationToken);

            return best ?? await Resolver.ResolveAsync(modelId, preferredModelId, cancellationToken);
        }

        public ImageGenerationRequest BuildRequest(
            ComposedPage composed,
            ImageModelProfile profile,
            IReadOnlyList<ReferenceImage> references,
            bool useCharacterPositions = true
        ) =>
            new()
            {
                ModelId = profile.Model.ProviderModelId,
                BaseCaption = composed.Positive,
                NegativeCaption = composed.Negative,
                Width = composed.Width,
                Height = composed.Height,
                Steps = composed.Steps,
                Scale = composed.Scale,
                Sampler = composed.Sampler,
                NoiseSchedule = composed.NoiseSchedule,
                UcPreset = composed.UcPreset,
                QualityToggle = composed.QualityToggle,
                Seed = composed.Seed,
                ParamsVersion = profile.ParamsVersion,
                UseCharacterPrompts = profile.SupportsCharacterPrompts && useCharacterPositions,
                UsePreciseReference = profile.SupportsPreciseReference,
                Characters = composed.Characters.Take(profile.MaxCharacters).ToList(),
                References = references.ToList(),
                Token = Token,
            };

        public async Task MarkRunningAsync(
            ImageGenerationRequest request,
            ComposedPage composed,
            CancellationToken cancellationToken
        )
        {
            Generation.Status = PanelGenerationStatus.Running;
            Generation.StartedAt = DateTimeOffset.UtcNow;
            Generation.ModelId = request.ModelId;
            Generation.Width = request.Width;
            Generation.Height = request.Height;
            Generation.Steps = request.Steps;
            Generation.Scale = request.Scale;
            Generation.Seed = request.Seed;
            Generation.RequestJson = ImageRequestBuilder.Build(request).ToJsonString();

            await Db.SaveChangesAsync(cancellationToken);

            await hub
                .Clients.Group(GenerationHub.GroupFor(Generation.UserId))
                .SendAsync(
                    ImageEvents.Started,
                    new
                    {
                        generationId = Generation.Id,
                        kind = Generation.Kind.ToString(),
                        targetId = Generation.TargetId,
                        model = request.ModelId,
                    },
                    cancellationToken
                );
        }

        public async Task<ImageGenerationResult> SendAsync(
            ImageGenerationRequest request,
            CancellationToken cancellationToken
        )
        {
            var before = await TryReadBalanceAsync(cancellationToken);
            var result = await client.GenerateAsync(request, cancellationToken);
            var after = await TryReadBalanceAsync(cancellationToken);

            Generation.CostAnlas =
                before is { } start && after is { } end ? Math.Max(0, start - end) : null;
            Generation.WasFree = Generation.CostAnlas == 0;

            return result;
        }

        public Task<ImageAsset> StoreAsync(
            ImageGenerationResult result,
            ImageAssetKind kind,
            CancellationToken cancellationToken
        ) =>
            Store.SaveAsync(
                result.Png,
                User.Id,
                kind,
                result.Width > 0 ? result.Width : Generation.Width,
                result.Height > 0 ? result.Height : Generation.Height,
                result.Metadata,
                Project?.Id,
                Project?.StoryId,
                cancellationToken
            );

        public async Task CompleteAsync(ImageAsset asset, CancellationToken cancellationToken)
        {
            Generation.Status = PanelGenerationStatus.Succeeded;
            Generation.ImageAssetId = asset.Id;
            Generation.FinishedAt = DateTimeOffset.UtcNow;
            Generation.DurationMs = stopwatch.ElapsedMilliseconds;
            await Db.SaveChangesAsync(cancellationToken);
        }

        public Task PublishCompletedAsync(object payload) =>
            hub
                .Clients.Group(GenerationHub.GroupFor(Generation.UserId))
                .SendAsync(ImageEvents.Completed, payload, CancellationToken.None);

        public async Task FailAsync(string message, CancellationToken cancellationToken)
        {
            Generation.Status = PanelGenerationStatus.Failed;
            Generation.Error = message;
            Generation.FinishedAt = DateTimeOffset.UtcNow;
            Generation.DurationMs = stopwatch.ElapsedMilliseconds;
            await Db.SaveChangesAsync(cancellationToken);
        }

        public async Task<byte[]?> ReadAssetAsync(
            ImageAsset asset,
            CancellationToken cancellationToken
        )
        {
            var path = Store.ResolvePath(asset);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
        }

        private async Task<int?> TryReadBalanceAsync(CancellationToken cancellationToken)
        {
            try
            {
                var probe = await accounts.GetAccountAsync(Token, cancellationToken);
                return probe.Status == NovelAiProbeStatus.Ok ? probe.Account?.AnlasBalance : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
