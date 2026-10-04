using ComicStudio.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Comics;

/// <summary>The manga page library. Data, not code: templates can be added later.</summary>
[ApiController]
[Authorize]
[Route("api/layout-templates")]
public class LayoutTemplatesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LayoutTemplateDto>>> List(
        CancellationToken cancellationToken
    )
    {
        var templates = await db
            .LayoutTemplates.AsNoTracking()
            .OrderBy(template => template.SortOrder)
            .ToListAsync(cancellationToken);

        return Ok(
            templates
                .Select(template => new LayoutTemplateDto(
                    template.Id,
                    template.Name,
                    template.Category,
                    template.PanelCount,
                    template.Gutter,
                    template.Margin,
                    template.BorderThickness,
                    template.Description,
                    CurationRunner.ParseSlots(template)
                ))
                .ToList()
        );
    }
}
