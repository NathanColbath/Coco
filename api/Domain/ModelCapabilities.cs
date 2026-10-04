namespace ComicStudio.Api.Domain;

/// <summary>
/// Feature flags for a model. Stored as a JSON column and surfaced to the SPA
/// so prompt/progress logic can branch on capabilities without hardcoding
/// model ids.
/// </summary>
public class ModelCapabilities
{
    /// <summary>Wire protocol: "openai-chat", "legacy-completion" or "image".</summary>
    public string? Transport { get; set; }

    /// <summary>NovelAI's `params_version` for image requests (3 for V4.x, 4 for V5).</summary>
    public int? ParamsVersion { get; set; }

    public bool? CharacterPrompts { get; set; }

    public int? MaxCharacters { get; set; }

    /// <summary>Precise Reference (director references) support.</summary>
    public bool? PreciseReference { get; set; }

    /// <summary>Vibe Transfer support. Mutually exclusive with Precise Reference.</summary>
    public bool? VibeTransfer { get; set; }

    public bool? Inpainting { get; set; }

    /// <summary>Model used instead when inpainting is requested on this model.</summary>
    public string? InpaintingFallback { get; set; }

    /// <summary>Legacy completion models support a prefill appended to the last message.</summary>
    public bool? Prefill { get; set; }

    /// <summary>Legacy module support (e.g. Kayra's V2 modules).</summary>
    public bool? Modules { get; set; }

    /// <summary>Instruction prefix for instruct-capable text models, e.g. "{".</summary>
    public string? InstructSyntax { get; set; }

    public int? MaxPixels { get; set; }

    /// <summary>Width/height must be a multiple of this value.</summary>
    public int? SizeStep { get; set; }
}
