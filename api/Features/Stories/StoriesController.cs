using System.Text.Json;
using ComicStudio.Api.Auth;
using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Storytelling;
using ComicStudio.Api.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Stories;

[ApiController]
[Authorize]
[Route("api/stories")]
public class StoriesController(
    AppDbContext db,
    ContextAssembler assembler,
    ITokenCounter counter,
    TextModelResolver models,
    StoryGenerationRunner runner
) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StorySummaryDto>>> List(
        CancellationToken cancellationToken
    )
    {
        var userId = User.GetUserId();

        var stories = await db
            .Stories.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);

        var blockCounts = await db
            .StoryBlocks.AsNoTracking()
            .Where(b => stories.Select(s => s.Id).Contains(b.StoryId))
            .GroupBy(b => b.StoryId)
            .Select(group => new { StoryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.StoryId, item => item.Count, cancellationToken);

        var summaries = new List<StorySummaryDto>(stories.Count);
        foreach (var story in stories)
        {
            var blocks = await db
                .StoryBlocks.AsNoTracking()
                .Where(b => b.StoryId == story.Id)
                .ToListAsync(cancellationToken);

            var text = StoryPath.Text(StoryPath.Build(blocks, story.CursorBlockId));

            summaries.Add(
                new StorySummaryDto(
                    story.Id,
                    story.Title,
                    story.Genre,
                    story.Tags,
                    story.TextModelId,
                    StoryPath.WordCount(text),
                    blockCounts.GetValueOrDefault(story.Id),
                    story.CreatedAt,
                    story.UpdatedAt
                )
            );
        }

        return Ok(summaries);
    }

    [HttpPost]
    public async Task<ActionResult<StoryDetailDto>> Create(
        CreateStoryRequest request,
        CancellationToken cancellationToken
    )
    {
        var story = new Story
        {
            UserId = User.GetUserId(),
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Untitled story" : request.Title.Trim(),
            Synopsis = request.Synopsis,
            Genre = request.Genre,
            Tags = request.Tags,
            TextModelId = request.TextModelId,
        };

        db.Stories.Add(story);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(await BuildDetailAsync(story, cancellationToken));
    }

    [HttpGet("{storyId:guid}")]
    public async Task<ActionResult<StoryDetailDto>> Get(
        Guid storyId,
        CancellationToken cancellationToken
    )
    {
        var story = await FindStoryAsync(storyId, cancellationToken);
        return story is null ? NotFound() : Ok(await BuildDetailAsync(story, cancellationToken));
    }

    [HttpPut("{storyId:guid}")]
    public async Task<ActionResult<StoryDetailDto>> Update(
        Guid storyId,
        UpdateStoryRequest request,
        CancellationToken cancellationToken
    )
    {
        var story = await FindStoryAsync(storyId, cancellationToken);
        if (story is null)
        {
            return NotFound();
        }

        story.Title = string.IsNullOrWhiteSpace(request.Title) ? story.Title : request.Title.Trim();
        story.Synopsis = request.Synopsis;
        story.Genre = request.Genre;
        story.Tags = request.Tags;
        story.TextModelId = request.TextModelId;

        if (request.Context is not null)
        {
            story.Context = request.Context;
        }

        if (request.Sampler is not null)
        {
            story.Sampler = request.Sampler;
        }

        story.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(await BuildDetailAsync(story, cancellationToken));
    }

    [HttpDelete("{storyId:guid}")]
    public async Task<IActionResult> Delete(Guid storyId, CancellationToken cancellationToken)
    {
        var story = await FindStoryAsync(storyId, cancellationToken);
        if (story is null)
        {
            return NotFound();
        }

        var cleanup = ActivatorUtilities.GetServiceOrCreateInstance<ComicStudio.Api.Comics.ImageCleanupService>(
            HttpContext.RequestServices
        );
        await cleanup.DeleteStoryImagesAsync(storyId, cancellationToken);

        db.Stories.Remove(story);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // --- Blocks -------------------------------------------------------------

    [HttpPost("{storyId:guid}/blocks")]
    public async Task<ActionResult<StoryBlockDto>> AppendBlock(
        Guid storyId,
        AppendBlockRequest request,
        CancellationToken cancellationToken
    )
    {
        var story = await FindStoryAsync(storyId, cancellationToken);
        if (story is null)
        {
            return NotFound();
        }

        var text = request.Kind == StoryBlockKind.SceneBreak ? "***" : request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return ValidationProblem("A block needs some text.");
        }

        var block = new StoryBlock
        {
            StoryId = story.Id,
            ParentId = story.CursorBlockId,
            Kind = request.Kind,
            Origin = StoryBlockOrigin.User,
            Text = text,
            TokenCount = counter.Count(text),
        };

        db.StoryBlocks.Add(block);
        story.CursorBlockId = block.Id;
        story.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(StoryBlockDto.From(block));
    }

    [HttpPut("{storyId:guid}/blocks/{blockId:guid}")]
    public async Task<ActionResult<StoryBlockDto>> UpdateBlock(
        Guid storyId,
        Guid blockId,
        UpdateBlockRequest request,
        CancellationToken cancellationToken
    )
    {
        var block = await db.StoryBlocks.FirstOrDefaultAsync(
            b => b.Id == blockId && b.StoryId == storyId,
            cancellationToken
        );
        if (block is null)
        {
            return NotFound();
        }

        block.Text = request.Text?.Trim() ?? string.Empty;
        block.TokenCount = counter.Count(block.Text);

        var story = await FindStoryAsync(storyId, cancellationToken);
        if (story is not null)
        {
            story.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(StoryBlockDto.From(block));
    }

    /// <summary>Moves the cursor: undo, redo, or jump to an alternative branch.</summary>
    [HttpPost("{storyId:guid}/cursor")]
    public async Task<ActionResult<StoryDetailDto>> SetCursor(
        Guid storyId,
        SetCursorRequest request,
        CancellationToken cancellationToken
    )
    {
        var story = await FindStoryAsync(storyId, cancellationToken);
        if (story is null)
        {
            return NotFound();
        }

        if (request.BlockId is { } blockId)
        {
            var exists = await db.StoryBlocks.AnyAsync(
                b => b.Id == blockId && b.StoryId == storyId,
                cancellationToken
            );
            if (!exists)
            {
                return ValidationProblem("That block does not belong to this story.");
            }
        }

        story.CursorBlockId = request.BlockId;
        story.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(await BuildDetailAsync(story, cancellationToken));
    }

    // --- Generation ---------------------------------------------------------

    [HttpPost("{storyId:guid}/generate")]
    public async Task<ActionResult<GenerationAcceptedDto>> Generate(
        Guid storyId,
        GenerateRequest request,
        CancellationToken cancellationToken
    )
    {
        var story = await FindStoryAsync(storyId, cancellationToken);
        if (story is null)
        {
            return NotFound();
        }

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == User.GetUserId(),
            cancellationToken
        );
        if (user is null)
        {
            return NotFound();
        }

        var model = await models.ResolveAsync(
            request.ModelId ?? story.TextModelId,
            user.PreferredTextModelId,
            cancellationToken
        );

        var sampler = CloneSampler(story.Sampler);
        if (request.MaxTokens is > 0)
        {
            sampler.MaxTokens = Math.Clamp(request.MaxTokens.Value, 1, 2048);
        }

        var generation = new StoryGeneration
        {
            StoryId = story.Id,
            UserId = user.Id,
            ModelId = model.Id,
            Transport = model.Capabilities.Transport ?? string.Empty,
            Instruction = string.IsNullOrWhiteSpace(request.Instruction)
                ? null
                : request.Instruction.Trim(),
            ParametersJson = JsonSerializer.Serialize(sampler),
            Status = StoryGenerationStatus.Queued,
        };

        db.StoryGenerations.Add(generation);
        await db.SaveChangesAsync(cancellationToken);

        runner.Start(generation.Id);

        return Ok(
            new GenerationAcceptedDto(
                generation.Id,
                model.Id,
                model.Capabilities.Transport ?? string.Empty
            )
        );
    }

    [HttpPost("{storyId:guid}/generations/{generationId:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid storyId,
        Guid generationId,
        CancellationToken cancellationToken
    )
    {
        var exists = await db.StoryGenerations.AnyAsync(
            g => g.Id == generationId && g.StoryId == storyId && g.UserId == User.GetUserId(),
            cancellationToken
        );
        if (!exists)
        {
            return NotFound();
        }

        return runner.Cancel(generationId) ? Accepted() : Conflict();
    }

    [HttpGet("{storyId:guid}/generations")]
    public async Task<ActionResult<IReadOnlyList<GenerationDto>>> Generations(
        Guid storyId,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default
    )
    {
        var generations = await db
            .StoryGenerations.AsNoTracking()
            .Where(g => g.StoryId == storyId && g.UserId == User.GetUserId())
            .OrderByDescending(g => g.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);

        return Ok(generations.Select(ToDto).ToList());
    }

    /// <summary>Shows exactly what would be sent, without spending a generation.</summary>
    [HttpPost("{storyId:guid}/context-preview")]
    public async Task<ActionResult<ContextPreviewDto>> ContextPreview(
        Guid storyId,
        GenerateRequest request,
        CancellationToken cancellationToken
    )
    {
        var story = await FindStoryAsync(storyId, cancellationToken);
        if (story is null)
        {
            return NotFound();
        }

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == User.GetUserId(),
            cancellationToken
        );
        if (user is null)
        {
            return NotFound();
        }

        var model = await models.ResolveAsync(
            request.ModelId ?? story.TextModelId,
            user.PreferredTextModelId,
            cancellationToken
        );

        var transport = model.Capabilities.Transport ?? "openai-chat";
        var sampler = CloneSampler(story.Sampler);
        if (request.MaxTokens is > 0)
        {
            sampler.MaxTokens = Math.Clamp(request.MaxTokens.Value, 1, 2048);
        }

        var blocks = await LoadBlocksAsync(story.Id, cancellationToken);
        var lorebook = await LoadLorebookAsync(story.Id, cancellationToken);

        var context = assembler.Assemble(
            new ContextAssemblyRequest
            {
                Story = story,
                ActivePath = StoryPath.Build(blocks, story.CursorBlockId),
                ModelId = model.ProviderModelId,
                Transport = transport,
                ContextTokens = TextModelResolver.ContextTokensFor(model, user.NovelAiTier),
                RolloverTokens = model.RolloverTokens ?? 0,
                OutputTokens = sampler.MaxTokens,
                Instruction = request.Instruction,
                LorebookEntries = lorebook,
            }
        );

        return Ok(new ContextPreviewDto(context.Trace, context.Prompt));
    }

    // --- Helpers ------------------------------------------------------------

    private Task<Story?> FindStoryAsync(Guid storyId, CancellationToken cancellationToken) =>
        db.Stories.FirstOrDefaultAsync(
            s => s.Id == storyId && s.UserId == User.GetUserId(),
            cancellationToken
        );

    private async Task<StoryDetailDto> BuildDetailAsync(
        Story story,
        CancellationToken cancellationToken
    )
    {
        var blocks = await LoadBlocksAsync(story.Id, cancellationToken);
        var lorebook = await LoadLorebookAsync(story.Id, cancellationToken);
        var activeText = StoryPath.Text(StoryPath.Build(blocks, story.CursorBlockId));

        return new StoryDetailDto(
            story.Id,
            story.Title,
            story.Synopsis,
            story.Genre,
            story.Tags,
            story.TextModelId,
            story.Context,
            story.Sampler,
            story.CursorBlockId,
            blocks.Select(StoryBlockDto.From).ToList(),
            lorebook.Select(LorebookEntryDto.From).ToList(),
            activeText,
            StoryPath.WordCount(activeText),
            story.CreatedAt,
            story.UpdatedAt
        );
    }

    private Task<List<StoryBlock>> LoadBlocksAsync(Guid storyId, CancellationToken cancellationToken) =>
        db
            .StoryBlocks.AsNoTracking()
            .Where(b => b.StoryId == storyId)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

    private Task<List<LorebookEntry>> LoadLorebookAsync(
        Guid storyId,
        CancellationToken cancellationToken
    ) =>
        db
            .LorebookEntries.AsNoTracking()
            .Where(e => e.StoryId == storyId)
            .OrderByDescending(e => e.InsertionOrder)
            .ToListAsync(cancellationToken);

    private static TextSamplerSettings CloneSampler(TextSamplerSettings source) =>
        JsonSerializer.Deserialize<TextSamplerSettings>(JsonSerializer.Serialize(source))
        ?? new TextSamplerSettings();

    private static GenerationDto ToDto(StoryGeneration generation) =>
        new(
            generation.Id,
            generation.ModelId,
            generation.Transport,
            generation.Status,
            generation.Output,
            generation.Error,
            generation.PromptTokens,
            generation.OutputTokens,
            generation.ResultBlockId,
            generation.CreatedAt,
            generation.DurationMs,
            DeserializeTrace(generation.ContextTraceJson)
        );

    private static ContextTrace? DeserializeTrace(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ContextTrace>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
