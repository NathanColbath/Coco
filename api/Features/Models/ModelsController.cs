using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Models;

public record AiModelDto(
    string Id,
    AiModelKind Kind,
    string DisplayName,
    string Family,
    int? ContextTokens,
    int? RolloverTokens,
    NovelAiTier MinTier,
    bool IsDefault,
    bool IsDeprecated,
    ModelCapabilities Capabilities,
    Dictionary<string, int>? TierOverrides,
    string? Notes
);

[ApiController]
[Route("api/models")]
public class ModelsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AiModelDto>>> List(
        [FromQuery] AiModelKind? kind,
        CancellationToken cancellationToken
    )
    {
        var query = db.AiModels.AsNoTracking().Where(m => !m.IsDeprecated);

        if (kind is not null)
        {
            query = query.Where(m => m.Kind == kind);
        }

        var models = await query
            .OrderBy(m => m.Kind)
            .ThenBy(m => m.SortOrder)
            .Select(m => new AiModelDto(
                m.Id,
                m.Kind,
                m.DisplayName,
                m.Family ?? string.Empty,
                m.ContextTokens,
                m.RolloverTokens,
                m.MinTier,
                m.IsDefault,
                m.IsDeprecated,
                m.Capabilities,
                m.TierOverrides,
                m.Notes
            ))
            .ToListAsync(cancellationToken);

        return Ok(models);
    }
}
