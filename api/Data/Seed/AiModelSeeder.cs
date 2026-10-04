using ComicStudio.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Data.Seed;

/// <summary>
/// The NovelAI model registry. Kept as data rather than constants so upstream
/// changes (model releases, deprecations, the Kayra -> GLM endpoint migration)
/// are a seed edit, not a refactor.
/// </summary>
public static class AiModelSeeder
{
    private static ModelCapabilities Chat() =>
        new() { Transport = "openai-chat", Prefill = false, Modules = false, InstructSyntax = "{" };

    private static ModelCapabilities Completion(bool modules = false) =>
        new() { Transport = "legacy-completion", Prefill = true, Modules = modules };

    private static ModelCapabilities Image(
        int paramsVersion,
        bool characterPrompts = true,
        int maxCharacters = 6,
        bool preciseReference = true,
        bool vibeTransfer = true,
        bool inpainting = true,
        string? inpaintingFallback = null
    ) =>
        new()
        {
            Transport = "image",
            ParamsVersion = paramsVersion,
            CharacterPrompts = characterPrompts,
            MaxCharacters = maxCharacters,
            PreciseReference = preciseReference,
            VibeTransfer = vibeTransfer,
            Inpainting = inpainting,
            InpaintingFallback = inpaintingFallback,
            MaxPixels = 3_145_728,
            SizeStep = 64,
        };

    private static readonly AiModel[] Models =
    [
        new()
        {
            Id = "glm-4-6",
            Kind = AiModelKind.Text,
            DisplayName = "GLM-4.6",
            ProviderModelId = "glm-4-6",
            Family = "GLM",
            ContextTokens = 28672,
            RolloverTokens = 8192,
            MinTier = NovelAiTier.Tablet,
            IsDefault = true,
            SortOrder = 10,
            Capabilities = Chat(),
            Notes = "OpenAI-compatible chat endpoint. Novels, instruct and analysis passes.",
        },
        new()
        {
            Id = "xialong-v1",
            Kind = AiModelKind.Text,
            DisplayName = "Xialong",
            ProviderModelId = "xialong-v1",
            Family = "GLM",
            ContextTokens = 28672,
            RolloverTokens = 8192,
            MinTier = NovelAiTier.Opus,
            SortOrder = 20,
            Capabilities = Chat(),
            Notes = "GLM-4.6 finetune trained on NovelAI's storytelling data. Opus exclusive.",
        },
        new()
        {
            Id = "llama-3-erato-v1",
            Kind = AiModelKind.Text,
            DisplayName = "Erato",
            ProviderModelId = "llama-3-erato-v1",
            Family = "Llama 3 70B",
            ContextTokens = 8192,
            MinTier = NovelAiTier.Opus,
            SortOrder = 30,
            Capabilities = Completion(),
            Notes = "NovelAI finetune of Llama 3 70B.",
        },
        new()
        {
            Id = "kayra-v1",
            Kind = AiModelKind.Text,
            DisplayName = "Kayra",
            ProviderModelId = "kayra-v1",
            Family = "NovelAI-LM 13B",
            ContextTokens = 8192,
            MinTier = NovelAiTier.Tablet,
            SortOrder = 40,
            Capabilities = Completion(modules: true),
            TierOverrides = new Dictionary<string, int>
            {
                ["Tablet"] = 3072,
                ["Scroll"] = 6144,
                ["Opus"] = 8192,
            },
            Notes = "In-house model. Being deprecated for subscribers on the text API.",
        },
        new()
        {
            Id = "clio-v1",
            Kind = AiModelKind.Text,
            DisplayName = "Clio",
            ProviderModelId = "clio-v1",
            Family = "NovelAI-LM 3B",
            ContextTokens = 8192,
            MinTier = NovelAiTier.Tablet,
            SortOrder = 50,
            Capabilities = Completion(),
        },
        new()
        {
            Id = "nai-diffusion-5-full",
            Kind = AiModelKind.Image,
            DisplayName = "Diffusion V5 Full",
            ProviderModelId = "nai-diffusion-5-full",
            Family = "NovelAI Diffusion V5",
            MinTier = NovelAiTier.Tablet,
            SortOrder = 10,
            Capabilities = Image(paramsVersion: 4, maxCharacters: 22, vibeTransfer: false),
            Notes = "Best quality. Opus free-generation usage limits apply.",
        },
        new()
        {
            Id = "nai-diffusion-5-curated",
            Kind = AiModelKind.Image,
            DisplayName = "Diffusion V5 Curated",
            ProviderModelId = "nai-diffusion-5-curated",
            Family = "NovelAI Diffusion V5",
            MinTier = NovelAiTier.Tablet,
            IsDefault = true,
            SortOrder = 20,
            Capabilities = Image(
                paramsVersion: 4,
                maxCharacters: 22,
                vibeTransfer: false,
                inpainting: false,
                inpaintingFallback: "nai-diffusion-4-5-curated-inpainting"
            ),
            Notes = "Safety-tuned V5. Inpainting currently falls back to V4.5 Curated.",
        },
        new()
        {
            Id = "nai-diffusion-4-5-full",
            Kind = AiModelKind.Image,
            DisplayName = "Diffusion V4.5 Full",
            ProviderModelId = "nai-diffusion-4-5-full",
            Family = "NovelAI Diffusion V4.5",
            MinTier = NovelAiTier.Tablet,
            SortOrder = 30,
            Capabilities = Image(paramsVersion: 3),
            Notes = "Unlimited free generations on Opus at <=1024x1024 and <=28 steps.",
        },
        new()
        {
            Id = "nai-diffusion-4-5-curated",
            Kind = AiModelKind.Image,
            DisplayName = "Diffusion V4.5 Curated",
            ProviderModelId = "nai-diffusion-4-5-curated",
            Family = "NovelAI Diffusion V4.5",
            MinTier = NovelAiTier.Tablet,
            SortOrder = 40,
            Capabilities = Image(paramsVersion: 3),
        },
        new()
        {
            Id = "nai-diffusion-4-5-curated-inpainting",
            Kind = AiModelKind.Image,
            DisplayName = "Diffusion V4.5 Curated (Inpaint)",
            ProviderModelId = "nai-diffusion-4-5-curated-inpainting",
            Family = "NovelAI Diffusion V4.5",
            MinTier = NovelAiTier.Tablet,
            SortOrder = 50,
            Capabilities = Image(paramsVersion: 3, characterPrompts: false, preciseReference: false, vibeTransfer: false),
        },
        new()
        {
            Id = "nai-diffusion-4-5-full-inpainting",
            Kind = AiModelKind.Image,
            DisplayName = "Diffusion V4.5 Full (Inpaint)",
            ProviderModelId = "nai-diffusion-4-5-full-inpainting",
            Family = "NovelAI Diffusion V4.5",
            MinTier = NovelAiTier.Tablet,
            SortOrder = 60,
            Capabilities = Image(paramsVersion: 3, characterPrompts: false, preciseReference: false, vibeTransfer: false),
        },
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var existing = await db
            .AiModels.Include(m => m.Capabilities)
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var seed in Models)
        {
            if (existing.TryGetValue(seed.Id, out var current))
            {
                db.Entry(current).CurrentValues.SetValues(seed);
                current.Capabilities = seed.Capabilities;
            }
            else
            {
                db.AiModels.Add(seed);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
