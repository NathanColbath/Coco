using ComicStudio.Api.Auth;
using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Features.Comics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Characters;

[ApiController]
[Authorize]
[Route("api/stories/{storyId:guid}/characters")]
public class CharactersController(
    AppDbContext db,
    ImageGenerationRunner images,
    ILogger<CharactersController> logger
) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CharacterDto>>> List(
        Guid storyId,
        CancellationToken cancellationToken
    )
    {
        if (!await OwnsStoryAsync(storyId, cancellationToken))
        {
            return NotFound();
        }

        var characters = await db
            .Characters.AsNoTracking()
            .Where(character => character.StoryId == storyId)
            .OrderBy(character => character.Role)
            .ThenBy(character => character.Name)
            .ToListAsync(cancellationToken);

        var sheets = await db
            .CharacterSheetImages.AsNoTracking()
            .Where(sheet => characters.Select(character => character.Id).Contains(sheet.CharacterId))
            .OrderByDescending(sheet => sheet.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(
            characters
                .Select(character => ToDto(
                    character,
                    sheets.Where(sheet => sheet.CharacterId == character.Id).ToList()
                ))
                .ToList()
        );
    }

    [HttpPost]
    public async Task<ActionResult<CharacterDto>> Create(
        Guid storyId,
        UpsertCharacterRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!await OwnsStoryAsync(storyId, cancellationToken))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem("A character needs a name.");
        }

        var character = new Character
        {
            StoryId = storyId,
            UserId = User.GetUserId(),
            Name = request.Name.Trim(),
        };

        Apply(character, request);
        db.Characters.Add(character);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(character, []));
    }

    [HttpPut("{characterId:guid}")]
    public async Task<ActionResult<CharacterDto>> Update(
        Guid storyId,
        Guid characterId,
        UpsertCharacterRequest request,
        CancellationToken cancellationToken
    )
    {
        var character = await FindAsync(storyId, characterId, cancellationToken);
        if (character is null)
        {
            return NotFound();
        }

        Apply(character, request);
        character.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var sheets = await db
            .CharacterSheetImages.AsNoTracking()
            .Where(sheet => sheet.CharacterId == character.Id)
            .ToListAsync(cancellationToken);

        return Ok(ToDto(character, sheets));
    }

    [HttpDelete("{characterId:guid}")]
    public async Task<IActionResult> Delete(
        Guid storyId,
        Guid characterId,
        CancellationToken cancellationToken
    )
    {
        var character = await FindAsync(storyId, characterId, cancellationToken);
        if (character is null)
        {
            return NotFound();
        }

        db.Characters.Remove(character);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // --- Sheets -------------------------------------------------------------

    [HttpGet("{characterId:guid}/sheets")]
    public async Task<ActionResult<IReadOnlyList<CharacterSheetDto>>> Sheets(
        Guid storyId,
        Guid characterId,
        CancellationToken cancellationToken
    )
    {
        if (await FindAsync(storyId, characterId, cancellationToken) is null)
        {
            return NotFound();
        }

        var sheets = await db
            .CharacterSheetImages.AsNoTracking()
            .Where(sheet => sheet.CharacterId == characterId)
            .OrderByDescending(sheet => sheet.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(sheets.Select(ToSheetDto).ToList());
    }

    /// <summary>
    /// Queues a reference sheet. Sheets are drawn at one of the three sizes
    /// Precise Reference accepts, so no post-processing is needed.
    /// </summary>
    [HttpPost("{characterId:guid}/sheets")]
    public async Task<ActionResult<GenerationQueuedDto>> GenerateSheet(
        Guid storyId,
        Guid characterId,
        GenerateSheetRequest request,
        CancellationToken cancellationToken
    )
    {
        var character = await FindAsync(storyId, characterId, cancellationToken);
        if (character is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(character.Visual.PromptTags))
        {
            return ValidationProblem(
                "Give the character visual tags first — they are what keeps them on model."
            );
        }

        var project = await db
            .ComicProjects.AsNoTracking()
            .Where(item => item.StoryId == storyId && item.UserId == User.GetUserId())
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var generation = new PanelGeneration
        {
            Kind = ImageJobKind.CharacterSheet,
            TargetId = character.Id,
            SheetKind = request.Kind,
            ProjectId = project?.Id ?? Guid.Empty,
            UserId = User.GetUserId(),
            ModelId = project?.Style.ModelId ?? string.Empty,
            IsHighResolution = true,
            Seed = request.Seed,
        };

        db.PanelGenerations.Add(generation);
        await db.SaveChangesAsync(cancellationToken);

        images.Start(generation.Id);
        logger.LogInformation(
            "Queued a {Kind} sheet for character {CharacterId}.",
            request.Kind,
            character.Id
        );

        return Ok(new GenerationQueuedDto(generation.Id, character.Id));
    }

    /// <summary>Locks a sheet so panels start referencing it.</summary>
    [HttpPost("{characterId:guid}/sheets/{sheetId:guid}/lock")]
    public async Task<IActionResult> LockSheet(
        Guid storyId,
        Guid characterId,
        Guid sheetId,
        [FromQuery] bool locked = true,
        CancellationToken cancellationToken = default
    )
    {
        var character = await FindAsync(storyId, characterId, cancellationToken);
        if (character is null)
        {
            return NotFound();
        }

        var sheet = await db.CharacterSheetImages.FirstOrDefaultAsync(
            item => item.Id == sheetId && item.CharacterId == characterId,
            cancellationToken
        );
        if (sheet is null)
        {
            return NotFound();
        }

        // One locked sheet per kind: the newest approval wins.
        var siblings = await db
            .CharacterSheetImages.Where(item =>
                item.CharacterId == characterId && item.Kind == sheet.Kind && item.Id != sheetId
            )
            .ToListAsync(cancellationToken);

        foreach (var sibling in siblings)
        {
            sibling.IsLocked = false;
        }

        sheet.IsLocked = locked;
        character.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpDelete("{characterId:guid}/sheets/{sheetId:guid}")]
    public async Task<IActionResult> DeleteSheet(
        Guid storyId,
        Guid characterId,
        Guid sheetId,
        CancellationToken cancellationToken
    )
    {
        var sheet = await db.CharacterSheetImages.FirstOrDefaultAsync(
            item => item.Id == sheetId && item.CharacterId == characterId,
            cancellationToken
        );
        if (sheet is null || !await OwnsStoryAsync(storyId, cancellationToken))
        {
            return NotFound();
        }

        db.CharacterSheetImages.Remove(sheet);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // --- Helpers ------------------------------------------------------------

    private Task<bool> OwnsStoryAsync(Guid storyId, CancellationToken cancellationToken) =>
        db.Stories.AnyAsync(
            story => story.Id == storyId && story.UserId == User.GetUserId(),
            cancellationToken
        );

    private Task<Character?> FindAsync(
        Guid storyId,
        Guid characterId,
        CancellationToken cancellationToken
    ) =>
        db.Characters.FirstOrDefaultAsync(
            character =>
                character.Id == characterId
                && character.StoryId == storyId
                && character.UserId == User.GetUserId(),
            cancellationToken
        );

    private static void Apply(Character character, UpsertCharacterRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            character.Name = request.Name.Trim();
        }

        character.Aliases = request.Aliases;
        character.Role = request.Role;
        character.Bio = request.Bio;
        character.Personality = request.Personality;
        character.SpeechStyle = request.SpeechStyle;

        if (request.Visual is not null)
        {
            character.Visual = request.Visual;
        }
    }

    private static CharacterDto ToDto(
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
            sheets.Select(ToSheetDto).ToList()
        );

    private static CharacterSheetDto ToSheetDto(CharacterSheetImage sheet) =>
        new(
            sheet.Id,
            sheet.Kind,
            sheet.ImageAssetId,
            sheet.IsLocked,
            sheet.Seed,
            sheet.ModelId,
            sheet.CreatedAt
        );
}
