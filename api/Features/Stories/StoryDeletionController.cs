using ComicStudio.Api.Auth;
using ComicStudio.Api.Comics;
using ComicStudio.Api.Data;
using ComicStudio.Api.Features.Comics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Stories;

/// <summary>
/// What deleting a story destroys. Its comics disappear through the
/// ComicProject -> Story cascade, so each one is listed explicitly rather
/// than hidden behind a single count.
/// </summary>
public record StoryDeletionPreviewDto(
    int Characters,
    int Blocks,
    int LorebookEntries,
    int Comics,
    int Pages,
    int Images,
    long Bytes,
    IReadOnlyList<DeleteImpactDto> Cascades
);

[ApiController]
[Authorize]
[Route("api/stories")]
public class StoryDeletionController(AppDbContext db, IServiceProvider services) : ControllerBase
{
    [HttpGet("{storyId:guid}/deletion-preview")]
    public async Task<ActionResult<StoryDeletionPreviewDto>> Preview(
        Guid storyId,
        CancellationToken cancellationToken
    )
    {
        var owned = await db.Stories.AsNoTracking().AnyAsync(
            story => story.Id == storyId && story.UserId == User.GetUserId(),
            cancellationToken
        );
        if (!owned)
        {
            return NotFound();
        }

        var cleanup = ActivatorUtilities.GetServiceOrCreateInstance<ImageCleanupService>(services);
        var preview = await cleanup.PreviewStoryAsync(storyId, cancellationToken);

        // The service reports the story's own counts as cascades; lift them
        // into the story-specific fields so the client does not have to.
        var byKind = preview.Cascades.ToLookup(
            cascade => cascade.Kind,
            StringComparer.OrdinalIgnoreCase
        );

        return Ok(
            new StoryDeletionPreviewDto(
                CountOf(byKind, DeletionCascadeKinds.Characters),
                CountOf(byKind, DeletionCascadeKinds.Blocks),
                CountOf(byKind, DeletionCascadeKinds.Lorebook),
                byKind[DeletionCascadeKinds.Comic].Count(),
                preview.Pages,
                preview.Images,
                preview.Bytes,
                preview.Cascades
            )
        );
    }

    private static int CountOf(ILookup<string, DeleteImpactDto> cascades, string kind) =>
        cascades[kind].Sum(cascade => cascade.Pages);
}
