namespace ComicStudio.Api.Domain;

/// <summary>
/// Registry of models available from NovelAI. Data-driven so that upstream
/// changes (new models, deprecations, endpoint migrations) are a seed update
/// rather than a code change.
/// </summary>
public class AiModel
{
    /// <summary>Local id, e.g. "glm-4-6" or "nai-diffusion-5-full".</summary>
    public string Id { get; set; } = string.Empty;

    public AiModelKind Kind { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The value sent to NovelAI on the wire.</summary>
    public string ProviderModelId { get; set; } = string.Empty;

    public string? Family { get; set; }

    /// <summary>Maximum context window in tokens (text models).</summary>
    public int? ContextTokens { get; set; }

    /// <summary>Extra tokens available before rollover trims back to <see cref="ContextTokens"/>.</summary>
    public int? RolloverTokens { get; set; }

    /// <summary>Minimum subscription tier required to use the model.</summary>
    public NovelAiTier MinTier { get; set; } = NovelAiTier.Tablet;

    public bool IsDeprecated { get; set; }

    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    public ModelCapabilities Capabilities { get; set; } = new();

    /// <summary>Per-tier overrides (e.g. Kayra's 3072/6144/8192 context windows).</summary>
    public Dictionary<string, int>? TierOverrides { get; set; }

    public string? Notes { get; set; }
}
