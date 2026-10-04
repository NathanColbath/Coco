using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Text;

/// <summary>Resolves a model's transport, and the model itself, from the registry.</summary>
public class TextModelResolver(AppDbContext db)
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
            : await db.AiModels.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (model is null)
        {
            model = await db
                .AiModels.Where(m => m.Kind == AiModelKind.Text && m.IsDefault)
                .FirstOrDefaultAsync(cancellationToken);
        }

        model ??= await db
            .AiModels.Where(m => m.Kind == AiModelKind.Text && !m.IsDeprecated)
            .OrderBy(m => m.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        return model
            ?? throw new InvalidOperationException("No text models are registered.");
    }

    /// <summary>Tier-aware context window, honouring per-tier overrides.</summary>
    public static int ContextTokensFor(AiModel model, NovelAiTier tier)
    {
        if (model.TierOverrides is { Count: > 0 })
        {
            var key = tier.ToString();
            if (model.TierOverrides.TryGetValue(key, out var overrideTokens))
            {
                return overrideTokens;
            }
        }

        return model.ContextTokens ?? 8192;
    }
}
