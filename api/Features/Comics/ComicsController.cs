using ComicStudio.Api.Auth;
using ComicStudio.Api.Comics;
using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using ComicStudio.Api.NovelAi.Image;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Comics;

[ApiController]
[Authorize]
[Route("api/comics")]
public class ComicsController(
    AppDbContext db,
    CurationRunner curation,
    ImageGenerationRunner images
) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ComicSummaryDto>>> List(
        CancellationToken cancellationToken
    )
    {
        var userId = User.GetUserId();

        var projects = await db
            .ComicProjects.AsNoTracking()
            .Include(project => project.Story)
            .Where(project => project.UserId == userId)
            .OrderByDescending(project => project.UpdatedAt)
            .ToListAsync(cancellationToken);

        var summaries = new List<ComicSummaryDto>(projects.Count);

        foreach (var project in projects)
        {
            var pageCount = await db
                .ComicPages.AsNoTracking()
                .CountAsync(page => page.ProjectId == project.Id, cancellationToken);

            var generated = await db
                .ComicPages.AsNoTracking()
                .CountAsync(
                    page => page.ProjectId == project.Id && page.Status == PageStatus.Generated,
                    cancellationToken
                );

            summaries.Add(
                new ComicSummaryDto(
                    project.Id,
                    project.Title,
                    project.StoryId,
                    project.Story?.Title ?? string.Empty,
                    project.Status,
                    pageCount,
                    generated,
                    project.CoverImageId,
                    project.UpdatedAt
                )
            );
        }

        return Ok(summaries);
    }

    [HttpPost]
    public async Task<ActionResult<ComicDetailDto>> Create(
        CreateComicRequest request,
        CancellationToken cancellationToken
    )
    {
        var userId = User.GetUserId();

        var story = await db.Stories.FirstOrDefaultAsync(
            item => item.Id == request.StoryId && item.UserId == userId,
            cancellationToken
        );
        if (story is null)
        {
            return NotFound();
        }

        var project = new ComicProject
        {
            UserId = userId,
            StoryId = story.Id,
            Title = string.IsNullOrWhiteSpace(request.Title) ? story.Title : request.Title.Trim(),
            Premise = string.IsNullOrWhiteSpace(request.Premise) ? story.Synopsis : request.Premise,
        };

        db.ComicProjects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(await BuildDetailAsync(project, cancellationToken));
    }

    [HttpGet("{projectId:guid}")]
    public async Task<ActionResult<ComicDetailDto>> Get(
        Guid projectId,
        CancellationToken cancellationToken
    )
    {
        var project = await FindAsync(projectId, cancellationToken);
        return project is null ? NotFound() : Ok(await BuildDetailAsync(project, cancellationToken));
    }

    [HttpPut("{projectId:guid}")]
    public async Task<ActionResult<ComicDetailDto>> Update(
        Guid projectId,
        UpdateComicRequest request,
        CancellationToken cancellationToken
    )
    {
        var project = await FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        project.Title = string.IsNullOrWhiteSpace(request.Title) ? project.Title : request.Title.Trim();
        project.Premise = request.Premise;
        project.Direction = request.Direction;
        project.ColorMode = request.ColorMode;
        project.Rating = request.Rating;
        project.DialogueAdaptation = request.DialogueAdaptation;
        project.PageWidth = Math.Clamp(request.PageWidth, 800, 4000);
        project.PageHeight = Math.Clamp(request.PageHeight, 800, 6000);

        if (request.Style is not null)
        {
            project.Style = request.Style;
        }

        project.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(await BuildDetailAsync(project, cancellationToken));
    }

    [HttpDelete("{projectId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var cleanup = ActivatorUtilities.GetServiceOrCreateInstance<ImageCleanupService>(
            HttpContext.RequestServices
        );
        await cleanup.DeleteComicImagesAsync(projectId, cancellationToken);

        db.ComicProjects.Remove(project);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // --- Curation -----------------------------------------------------------

    [HttpPost("{projectId:guid}/curate")]
    public async Task<IActionResult> Curate(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (project.Status == ComicStatus.Curating)
        {
            return Conflict(new ProblemDetails { Detail = "Curation is already running." });
        }

        curation.Start(project.Id);
        return Accepted();
    }

    [HttpPost("{projectId:guid}/curate/cancel")]
    public async Task<IActionResult> CancelCuration(
        Guid projectId,
        CancellationToken cancellationToken
    )
    {
        if (await FindAsync(projectId, cancellationToken) is null)
        {
            return NotFound();
        }

        return curation.Cancel(projectId) ? Accepted() : Conflict();
    }

    [HttpGet("{projectId:guid}/curation-runs")]
    public async Task<ActionResult> CurationRuns(
        Guid projectId,
        CancellationToken cancellationToken
    )
    {
        if (await FindAsync(projectId, cancellationToken) is null)
        {
            return NotFound();
        }

        var runs = await db
            .CurationRuns.AsNoTracking()
            .Where(run => run.ProjectId == projectId)
            .OrderByDescending(run => run.CreatedAt)
            .Take(20)
            .Select(run => new
            {
                run.Id,
                run.Stage,
                run.Status,
                run.ModelId,
                run.Error,
                run.DurationMs,
                run.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return Ok(runs);
    }

    // --- Pages --------------------------------------------------------------

    /// <summary>Hand-edit a page's script, ledger or lettering, then recompose.</summary>
    [HttpPut("{projectId:guid}/pages/{pageId:guid}")]
    public async Task<ActionResult<PageDto>> UpdatePage(
        Guid projectId,
        Guid pageId,
        UpdatePageRequest request,
        CancellationToken cancellationToken
    )
    {
        var project = await FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var page = await db
            .ComicPages.Include(item => item.Dialogue)
            .FirstOrDefaultAsync(
                item => item.Id == pageId && item.ProjectId == projectId,
                cancellationToken
            );
        if (page is null)
        {
            return NotFound();
        }

        page.Script = request.Script;
        page.Continuity = request.Continuity;
        page.SceneTitle = request.SceneTitle;
        page.ModelOverride = string.IsNullOrWhiteSpace(request.ModelOverride)
            ? null
            : request.ModelOverride.Trim();

        if (request.Dialogue is not null)
        {
            db.DialogueLines.RemoveRange(page.Dialogue);
            page.Dialogue.Clear();

            for (var index = 0; index < request.Dialogue.Count; index++)
            {
                var line = request.Dialogue[index];
                page.Dialogue.Add(
                    new DialogueLine
                    {
                        Index = index,
                        Kind = line.Kind,
                        SpeakerCharacterId = line.SpeakerCharacterId,
                        Text = line.Text,
                        Position = line.Position,
                    }
                );
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var cast = await db
            .Characters.AsNoTracking()
            .Where(character => character.StoryId == project.StoryId)
            .ToListAsync(cancellationToken);

        await RecomputePageAsync(project, page, cast, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var environmentNames = await LoadEnvironmentNamesAsync(project.StoryId, cancellationToken);
        return Ok(ToPageDto(page, project, 0, cast, environmentNames));
    }

    // --- Generation ---------------------------------------------------------

    /// <summary>Draws one page as a single image.</summary>
    [HttpPost("{projectId:guid}/pages/{pageId:guid}/generate")]
    public async Task<ActionResult<GenerationQueuedDto>> GeneratePage(
        Guid projectId,
        Guid pageId,
        GeneratePageRequest request,
        CancellationToken cancellationToken
    )
    {
        var project = await FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var page = await db.ComicPages.FirstOrDefaultAsync(
            item => item.Id == pageId && item.ProjectId == projectId,
            cancellationToken
        );
        if (page is null)
        {
            return NotFound();
        }

        var generation = await QueuePageAsync(project, page, request, cancellationToken);
        return Ok(new GenerationQueuedDto(generation.Id, page.Id));
    }

    [HttpGet("{projectId:guid}/pages/{pageId:guid}/generations")]
    public async Task<ActionResult<IReadOnlyList<PageAttemptDto>>> PageAttempts(
        Guid projectId,
        Guid pageId,
        CancellationToken cancellationToken
    )
    {
        if (await FindAsync(projectId, cancellationToken) is null)
        {
            return NotFound();
        }

        var attempts = await db
            .PanelGenerations.AsNoTracking()
            .Where(item =>
                item.ProjectId == projectId
                && item.Kind == ImageJobKind.Page
                && item.TargetId == pageId
            )
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new PageAttemptDto(
                item.Id,
                item.Status,
                item.ModelId,
                item.Width,
                item.Height,
                item.Steps,
                item.Seed,
                item.CostAnlas,
                item.WasFree,
                item.ImageAssetId,
                item.Error,
                item.DurationMs,
                item.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return Ok(attempts);
    }

    [HttpPost("{projectId:guid}/generate")]
    public async Task<ActionResult<BatchGenerateAcceptedDto>> GenerateBatch(
        Guid projectId,
        BatchGenerateRequest request,
        CancellationToken cancellationToken
    )
    {
        var project = await FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        // A page is one generation, so the batch queues pages.
        var pages = await db
            .ComicPages.Where(page => page.ProjectId == projectId)
            .OrderBy(page => page.Index)
            .ToListAsync(cancellationToken);

        if (!request.IncludeDrawn)
        {
            pages = pages.Where(page => page.Status != PageStatus.Generated).ToList();
        }

        var queued = 0;
        foreach (var page in pages)
        {
            await QueuePageAsync(
                project,
                page,
                new GeneratePageRequest(request.HighResolution),
                cancellationToken
            );
            queued++;
        }

        return Ok(new BatchGenerateAcceptedDto(queued, 0));
    }

    [HttpPost("{projectId:guid}/generations/{generationId:guid}/cancel")]
    public async Task<IActionResult> CancelGeneration(
        Guid projectId,
        Guid generationId,
        CancellationToken cancellationToken
    )
    {
        var exists = await db.PanelGenerations.AnyAsync(
            item =>
                item.Id == generationId
                && item.ProjectId == projectId
                && item.UserId == User.GetUserId(),
            cancellationToken
        );

        if (!exists)
        {
            return NotFound();
        }

        return images.Cancel(generationId) ? Accepted() : Conflict();
    }

    // --- Cover --------------------------------------------------------------

    [HttpPut("{projectId:guid}/cover")]
    public async Task<ActionResult<CoverDto>> UpdateCover(
        Guid projectId,
        UpdateCoverRequest request,
        CancellationToken cancellationToken
    )
    {
        var project = await FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var cover = await EnsureCoverAsync(project, cancellationToken);
        cover.Prompt = request.Prompt ?? cover.Prompt;
        cover.NegativePrompt = request.Negative ?? cover.NegativePrompt;
        cover.TitleText = request.TitleText;
        cover.TitleFont = request.TitleFont;
        cover.TitleX = request.TitleX;
        cover.TitleY = request.TitleY;
        cover.TitleMaxWidth = request.TitleMaxWidth;
        cover.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToCoverDto(cover));
    }

    [HttpPost("{projectId:guid}/cover/plan")]
    public async Task<ActionResult<CoverDto>> PlanCover(
        Guid projectId,
        CancellationToken cancellationToken
    )
    {
        var project = await FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        await db.Entry(project).Reference(item => item.Story).LoadAsync(cancellationToken);
        var cover = await EnsureCoverAsync(project, cancellationToken);

        var curator = HttpContext.RequestServices.GetRequiredService<Curator>();
        var protector = HttpContext.RequestServices.GetRequiredService<Security.SecretProtector>();
        var textModels = HttpContext.RequestServices.GetRequiredService<Text.TextModelResolver>();
        var user = await db.Users.FirstOrDefaultAsync(
            item => item.Id == User.GetUserId(),
            cancellationToken
        );

        var token = protector.Unprotect(project.UserId, user?.NovelAiTokenCipher);
        if (user is null || string.IsNullOrEmpty(token))
        {
            return BadRequest(new ProblemDetails { Detail = "NovelAI is not connected." });
        }

        var model = await textModels.ResolveAsync(null, user.PreferredTextModelId, cancellationToken);
        var characters = await db
            .Characters.Where(character => character.StoryId == project.StoryId)
            .ToListAsync(cancellationToken);

        var suggestion = await curator.SuggestCoverAsync(
            project.Title,
            project.Premise ?? project.Story?.Synopsis ?? string.Empty,
            characters,
            new CuratorContext(
                token,
                model,
                Text.TextModelResolver.ContextTokensFor(model, user.NovelAiTier),
                project.DialogueAdaptation,
                project.Direction,
                project.Rating
            ),
            cancellationToken
        );

        if (suggestion is not null)
        {
            cover.Prompt = suggestion.Prompt;
            cover.NegativePrompt = suggestion.Negative;
        }

        cover.TitleText = string.IsNullOrWhiteSpace(cover.TitleText) ? project.Title : cover.TitleText;
        cover.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToCoverDto(cover));
    }

    [HttpPost("{projectId:guid}/cover/generate")]
    public async Task<ActionResult<GenerationQueuedDto>> GenerateCover(
        Guid projectId,
        CancellationToken cancellationToken
    )
    {
        var project = await FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var cover = await EnsureCoverAsync(project, cancellationToken);
        var generation = new PanelGeneration
        {
            Kind = ImageJobKind.Cover,
            TargetId = cover.Id,
            ProjectId = project.Id,
            UserId = User.GetUserId(),
            ModelId = project.Style.ModelId ?? string.Empty,
            IsHighResolution = true,
            Seed = cover.Seed,
        };

        db.PanelGenerations.Add(generation);
        await db.SaveChangesAsync(cancellationToken);

        images.Start(generation.Id);
        return Ok(new GenerationQueuedDto(generation.Id, Guid.Empty));
    }

    // --- Helpers ------------------------------------------------------------

    private Task<ComicProject?> FindAsync(Guid projectId, CancellationToken cancellationToken) =>
        db.ComicProjects.FirstOrDefaultAsync(
            project => project.Id == projectId && project.UserId == User.GetUserId(),
            cancellationToken
        );

    private async Task<Dictionary<Guid, string>> LoadEnvironmentNamesAsync(
        Guid storyId,
        CancellationToken cancellationToken
    ) =>
        await db
            .Environments.AsNoTracking()
            .Where(environment => environment.StoryId == storyId)
            .Select(environment => new { environment.Id, environment.Name })
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

    private Task<ComicPage?> LoadPageAsync(
        Guid projectId,
        Guid pageId,
        CancellationToken cancellationToken
    ) =>
        db
            .ComicPages.Include(page => page.Dialogue)
                .ThenInclude(line => line.Speaker)
            .FirstOrDefaultAsync(
                page => page.Id == pageId && page.ProjectId == projectId,
                cancellationToken
            );

    private async Task<PanelGeneration> QueuePageAsync(
        ComicProject project,
        ComicPage page,
        GeneratePageRequest request,
        CancellationToken cancellationToken
    )
    {
        var generation = new PanelGeneration
        {
            Kind = ImageJobKind.Page,
            TargetId = page.Id,
            ProjectId = project.Id,
            UserId = User.GetUserId(),
            ModelId = project.Style.ModelId ?? string.Empty,
            IsHighResolution = request.HighResolution,
            Seed = request.Seed,
        };

        db.PanelGenerations.Add(generation);
        await db.SaveChangesAsync(cancellationToken);

        images.Start(generation.Id);
        return generation;
    }

    private async Task<CoverPlan> EnsureCoverAsync(
        ComicProject project,
        CancellationToken cancellationToken
    )
    {
        var cover = await db.CoverPlans.FirstOrDefaultAsync(
            plan => plan.ProjectId == project.Id,
            cancellationToken
        );

        if (cover is not null)
        {
            return cover;
        }

        cover = new CoverPlan { ProjectId = project.Id, TitleText = project.Title };
        db.CoverPlans.Add(cover);
        await db.SaveChangesAsync(cancellationToken);
        return cover;
    }

    private async Task<ComicDetailDto> BuildDetailAsync(
        ComicProject project,
        CancellationToken cancellationToken
    )
    {
        await db.Entry(project).Reference(item => item.Story).LoadAsync(cancellationToken);

        var pages = await db
            .ComicPages.AsNoTracking()
            .Where(page => page.ProjectId == project.Id)
            .OrderBy(page => page.Index)
            .Include(page => page.Dialogue)
                .ThenInclude(line => line.Speaker)
            .ToListAsync(cancellationToken);

        var templates = await db.LayoutTemplates.AsNoTracking().ToDictionaryAsync(
            template => template.Id,
            cancellationToken
        );

        // Panel and page jobs share one table, so each lookup filters by kind.
        var pageAttemptCounts = await db
            .PanelGenerations.AsNoTracking()
            .Where(item => item.ProjectId == project.Id && item.Kind == ImageJobKind.Page)
            .GroupBy(item => item.TargetId)
            .Select(group => new { PageId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.PageId, item => item.Count, cancellationToken);

        var cast = await db
            .Characters.AsNoTracking()
            .Where(character => character.StoryId == project.StoryId)
            .OrderBy(character => character.Role)
            .ThenBy(character => character.Name)
            .ToListAsync(cancellationToken);

        var environmentNames = await LoadEnvironmentNamesAsync(project.StoryId, cancellationToken);

        var sheets = await db
            .CharacterSheetImages.AsNoTracking()
            .Where(sheet => cast.Select(character => character.Id).Contains(sheet.CharacterId))
            .OrderByDescending(sheet => sheet.CreatedAt)
            .ToListAsync(cancellationToken);

        var cover = await db.CoverPlans.AsNoTracking().FirstOrDefaultAsync(
            plan => plan.ProjectId == project.Id,
            cancellationToken
        );

        return new ComicDetailDto(
            project.Id,
            project.Title,
            project.Premise,
            project.StoryId,
            project.Story?.Title ?? string.Empty,
            project.Direction,
            project.ColorMode,
            project.Rating,
            project.Status,
            project.Style,
            project.PageWidth,
            project.PageHeight,
            project.DialogueAdaptation,
            project.StyleAnchorImageId,
            project.IsLegacy,
            project.CoverImageId,
            project.UpdatedAt,
            pages
                .Select(page =>
                {
                    return ToPageDto(
                        page,
                        project,
                        pageAttemptCounts.GetValueOrDefault(page.Id),
                        cast,
                        environmentNames
                    );
                })
                .ToList(),
            cast
                .Select(character => ToCharacterDto(
                    character,
                    sheets.Where(sheet => sheet.CharacterId == character.Id).ToList()
                ))
                .ToList(),
            cover is null ? null : ToCoverDto(cover)
        );
    }

    private static PageDto ToPageDto(
        ComicPage page,
        ComicProject project,
        int attemptCount,
        IReadOnlyList<Character> cast,
        IReadOnlyDictionary<Guid, string> environmentNames
    )
    {
        var size = PagePromptComposer.ResolutionFor(project, allowPaidQuality: true);

        return new PageDto(
            page.Id,
            page.Index,
            page.LayoutTemplateId,
            page.LayoutTemplateId,
            page.Status,
            page.SceneTitle,
            page.Script,
            page.Continuity,
            page.PositivePrompt,
            page.NegativePrompt,
            page.Seed,
            page.PageImageId,
            page.RenderImageId,
            attemptCount,
            size.Width,
            size.Height,
            page.Dialogue
                .OrderBy(line => line.Index)
                .Select(line => new DialogueLineDto(
                    line.Id,
                    line.Index,
                    line.Kind,
                    line.SpeakerCharacterId,
                    cast.FirstOrDefault(character => character.Id == line.SpeakerCharacterId)?.Name,
                    line.Text,
                    line.Position
                ))
                .ToList(),
            page.EnvironmentId is { } environmentId
                && environmentNames.TryGetValue(environmentId, out var environmentName)
                ? environmentName
                : null,
            page.ModelUsed,
            page.ModelOverride
        );
    }

    /// <summary>Recomposes a page's prompt after its script or lettering changes.</summary>
    private async Task RecomputePageAsync(
        ComicProject project,
        ComicPage page,
        IReadOnlyList<Character> cast,
        CancellationToken cancellationToken
    )
    {
        var composer = HttpContext.RequestServices.GetRequiredService<PagePromptComposer>();
        var composed = composer.Compose(page, project, cast);

        page.PositivePrompt = composed.Positive;
        page.NegativePrompt = composed.Negative;
        page.PromptSnapshotJson = System.Text.Json.JsonSerializer.Serialize(composed);
        page.Seed = composed.Seed;

        await Task.CompletedTask;
    }

    private static CharacterDto ToCharacterDto(
        Character character,
        IReadOnlyList<CharacterSheetImage> sheets
    ) =>
        new(
            character.Id,
            character.Name,
            (character.Aliases ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            character.Role,
            character.Bio,
            character.Personality,
            character.SpeechStyle,
            character.Visual,
            character.IsLocked,
            sheets.Any(sheet => sheet.IsLocked),
            sheets
                .Select(sheet => new CharacterSheetDto(
                    sheet.Id,
                    sheet.Kind,
                    sheet.ImageAssetId,
                    sheet.IsLocked,
                    sheet.Seed,
                    sheet.ModelId,
                    sheet.CreatedAt
                ))
                .ToList()
        );

    private static CoverDto ToCoverDto(CoverPlan cover) =>
        new(
            cover.Id,
            cover.Prompt,
            cover.NegativePrompt,
            cover.ImageAssetId,
            cover.RenderedImageId,
            cover.TitleText,
            cover.TitleFont,
            cover.TitleX,
            cover.TitleY,
            cover.TitleMaxWidth
        );
}
