using ComicStudio.Api.Auth;
using ComicStudio.Api.Comics;
using ComicStudio.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Comics;

/// <summary>
/// Shows what deleting a comic would destroy before anything is removed. The
/// delete itself stays on <see cref="ComicsController"/>; a second DELETE
/// route here would be an ambiguous match at runtime.
/// </summary>
[ApiController]
[Authorize]
[Route("api/comics")]
public class DeletionController(AppDbContext db, IServiceProvider services) : ControllerBase
{
    [HttpGet("{projectId:guid}/deletion-preview")]
    public async Task<ActionResult<DeletionPreviewDto>> Preview(
        Guid projectId,
        CancellationToken cancellationToken
    )
    {
        var owned = await db.ComicProjects.AsNoTracking().AnyAsync(
            project => project.Id == projectId && project.UserId == User.GetUserId(),
            cancellationToken
        );
        if (!owned)
        {
            return NotFound();
        }

        var cleanup = ActivatorUtilities.GetServiceOrCreateInstance<ImageCleanupService>(services);
        return Ok(await cleanup.PreviewComicAsync(projectId, cancellationToken));
    }
}
