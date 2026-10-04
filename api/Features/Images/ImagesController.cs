using ComicStudio.Api.Auth;
using ComicStudio.Api.Data;
using ComicStudio.Api.NovelAi.Image;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Images;

/// <summary>Serves stored image bytes. Ownership is checked on every request.</summary>
[ApiController]
[Authorize]
[Route("api/images")]
public class ImagesController(AppDbContext db, ImageStore store) : ControllerBase
{
    [HttpGet("{assetId:guid}")]
    public async Task<IActionResult> Get(Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await db.ImageAssets.AsNoTracking().FirstOrDefaultAsync(
            item => item.Id == assetId && item.UserId == User.GetUserId(),
            cancellationToken
        );

        if (asset is null)
        {
            return NotFound();
        }

        var path = store.ResolvePath(asset);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        // Content-addressed files never change, so they can be cached hard.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return PhysicalFile(path, asset.MimeType);
    }
}
