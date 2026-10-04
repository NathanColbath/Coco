using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.NovelAi.Image;

public record ImageModelProfile(
    AiModel Model,
    int ParamsVersion,
    bool SupportsCharacterPrompts,
    int MaxCharacters,
    bool SupportsPreciseReference,
    int MaxPixels,
    int SizeStep
);

/// <summary>Resolves an image model and the capability flags the composer needs.</summary>
public class ImageModelResolver(AppDbContext db)
{
    public async Task<AiModel> ResolveAsync(
        string? modelId,
        string? preferredModelId,
        CancellationToken cancellationToken = default
    )
    {
        var id = modelId ?? preferredModelId;

        var model = id is null
            ? null
            : await db.AiModels.FirstOrDefaultAsync(
                m => m.Id == id && m.Kind == AiModelKind.Image,
                cancellationToken
            );

        if (model is null)
        {
            model = await db
                .AiModels.Where(m => m.Kind == AiModelKind.Image && m.IsDefault && !m.IsDeprecated)
                .FirstOrDefaultAsync(cancellationToken);
        }

        model ??= await db
            .AiModels.Where(m => m.Kind == AiModelKind.Image && !m.IsDeprecated)
            .OrderBy(m => m.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        return model ?? throw new ImageGenerationException("No image models are registered.");
    }

    public static ImageModelProfile Profile(AiModel model)
    {
        var capabilities = model.Capabilities;

        return new ImageModelProfile(
            model,
            capabilities.ParamsVersion ?? 3,
            capabilities.CharacterPrompts ?? false,
            capabilities.MaxCharacters ?? 6,
            capabilities.PreciseReference ?? false,
            capabilities.MaxPixels ?? ResolutionPolicy.MaxPixelBudget,
            capabilities.SizeStep ?? ResolutionPolicy.SizeStep
        );
    }
}
