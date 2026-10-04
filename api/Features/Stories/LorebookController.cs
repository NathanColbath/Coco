using ComicStudio.Api.Auth;
using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Stories;

[ApiController]
[Authorize]
[Route("api/stories/{storyId:guid}/lorebook")]
public class LorebookController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LorebookEntryDto>>> List(
        Guid storyId,
        CancellationToken cancellationToken
    )
    {
        if (!await OwnsStoryAsync(storyId, cancellationToken))
        {
            return NotFound();
        }

        var entries = await db
            .LorebookEntries.AsNoTracking()
            .Where(entry => entry.StoryId == storyId)
            .OrderByDescending(entry => entry.InsertionOrder)
            .ThenBy(entry => entry.DisplayName)
            .ToListAsync(cancellationToken);

        return Ok(entries.Select(LorebookEntryDto.From).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<LorebookEntryDto>> Create(
        Guid storyId,
        CreateLorebookEntryRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!await OwnsStoryAsync(storyId, cancellationToken))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ValidationProblem("Give the entry a name.");
        }

        var entry = new LorebookEntry { StoryId = storyId };
        Apply(entry, request);

        db.LorebookEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(LorebookEntryDto.From(entry));
    }

    [HttpPut("{entryId:guid}")]
    public async Task<ActionResult<LorebookEntryDto>> Update(
        Guid storyId,
        Guid entryId,
        CreateLorebookEntryRequest request,
        CancellationToken cancellationToken
    )
    {
        var entry = await db.LorebookEntries.FirstOrDefaultAsync(
            item => item.Id == entryId && item.StoryId == storyId,
            cancellationToken
        );
        if (entry is null || !await OwnsStoryAsync(storyId, cancellationToken))
        {
            return NotFound();
        }

        Apply(entry, request);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(LorebookEntryDto.From(entry));
    }

    [HttpDelete("{entryId:guid}")]
    public async Task<IActionResult> Delete(
        Guid storyId,
        Guid entryId,
        CancellationToken cancellationToken
    )
    {
        var entry = await db.LorebookEntries.FirstOrDefaultAsync(
            item => item.Id == entryId && item.StoryId == storyId,
            cancellationToken
        );
        if (entry is null || !await OwnsStoryAsync(storyId, cancellationToken))
        {
            return NotFound();
        }

        db.LorebookEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static void Apply(LorebookEntry entry, CreateLorebookEntryRequest request)
    {
        entry.DisplayName = request.DisplayName.Trim();
        entry.KeysRaw = request.Keys?.Trim() ?? string.Empty;
        entry.Content = request.Content ?? string.Empty;
        entry.Category = request.Category;
        entry.Enabled = request.Enabled;
        entry.AlwaysOn = request.AlwaysOn;
        entry.InsertionOrder = request.InsertionOrder;
        entry.InsertionPosition = request.InsertionPosition;
        entry.Prefix = request.Prefix ?? string.Empty;
        entry.Suffix = request.Suffix ?? string.Empty;
        entry.ReservedTokens = request.ReservedTokens;
        entry.TrimDirection = request.TrimDirection;
        entry.KeyRelative = request.KeyRelative;
    }

    private Task<bool> OwnsStoryAsync(Guid storyId, CancellationToken cancellationToken) =>
        db.Stories.AnyAsync(
            story => story.Id == storyId && story.UserId == User.GetUserId(),
            cancellationToken
        );
}
