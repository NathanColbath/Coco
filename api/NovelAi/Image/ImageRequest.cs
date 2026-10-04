namespace ComicStudio.Api.NovelAi.Image;

/// <summary>A character's description and where they sit in the frame.</summary>
public record CharacterPrompt(
    string Tags,
    double CenterX,
    double CenterY,
    string? NegativeTags = null
);

public enum ReferenceKind
{
    /// <summary>Transfers the character, not the art style.</summary>
    Character,

    /// <summary>Transfers the art style only.</summary>
    Style,

    /// <summary>Transfers both (NovelAI calls this "character&style").</summary>
    CharacterAndStyle,
}

public record ReferenceImage(
    byte[] Bytes,
    ReferenceKind Kind = ReferenceKind.Character,
    double Strength = 0.8,
    double Fidelity = 0.5
);

public class ImageGenerationRequest
{
    public required string ModelId { get; init; }

    /// <summary>Scene, style and composition tags.</summary>
    public required string BaseCaption { get; init; }

    public required string NegativeCaption { get; init; }

    public int Width { get; init; } = 832;

    public int Height { get; init; } = 1216;

    public int Steps { get; init; } = 28;

    public double Scale { get; init; } = 5.0;

    public string Sampler { get; init; } = "k_euler_ancestral";

    public string NoiseSchedule { get; init; } = "karras";

    public int UcPreset { get; init; }

    public bool QualityToggle { get; init; } = true;

    public long Seed { get; init; }

    /// <summary>3 for V4.x, 4 for V5.</summary>
    public int ParamsVersion { get; init; } = 3;

    public bool UseCharacterPrompts { get; init; } = true;

    public bool UsePreciseReference { get; init; } = true;

    public List<CharacterPrompt> Characters { get; init; } = [];

    public List<ReferenceImage> References { get; init; } = [];

    /// <summary>Decrypted NovelAI token. Never logged.</summary>
    public required string Token { get; init; }
}

public class ImageGenerationResult
{
    public required byte[] Png { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>The PNG comment NovelAI embeds, used for reproducibility.</summary>
    public string? Metadata { get; init; }
}

public class ImageGenerationException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}
