using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ReadingDirection>))]
public enum ReadingDirection
{
    RightToLeft = 0,
    LeftToRight = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter<ColorMode>))]
public enum ColorMode
{
    Monochrome = 0,
    Color = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter<ComicStatus>))]
public enum ComicStatus
{
    Draft = 0,
    Curating = 1,
    Planned = 2,
    Generating = 3,
    Complete = 4,
}

/// <summary>
/// Which image model pages are drawn with. Pinning a model keeps a book stylistically
/// stable; the account preference is the default.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImageModelPolicy>))]
public enum ImageModelPolicy
{
    Auto = 0,
    AlwaysV5 = 1,
    AlwaysV4Point5 = 2,

    /// <summary>Use the model named by <see cref="ComicProject.ExplicitImageModelId"/>.</summary>
    Explicit = 3,
}

public class ComicProject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;

    public AppUser? User { get; set; }

    /// <summary>The prose this comic was adapted from.</summary>
    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public string Title { get; set; } = "Untitled comic";

    public string? Premise { get; set; }

    public ReadingDirection Direction { get; set; } = ReadingDirection.RightToLeft;

    public ColorMode ColorMode { get; set; } = ColorMode.Monochrome;

    /// <summary>
    /// How explicit the project may be. Applied to every prompt, so it must be
    /// set before drawing rather than after.
    /// </summary>
    public ContentRating Rating { get; set; } = ContentRating.General;

    public ComicStylePreset Style { get; set; } = new();

    /// <summary>Project-wide guidance for greyscale values, one note per line.</summary>
    public string? PaletteNotes { get; set; }

    /// <summary>Default environment description applied to scenes that name none.</summary>
    public string? EnvironmentDefaults { get; set; }

    /// <summary>Page canvas in pixels. Panels are composited onto this.</summary>
    public int PageWidth { get; set; } = 1600;

    public int PageHeight { get; set; } = 2263;

    public ComicStatus Status { get; set; } = ComicStatus.Draft;

    /// <summary>How prose dialogue is handled during curation.</summary>
    public DialogueAdaptation DialogueAdaptation { get; set; } = DialogueAdaptation.ShortenLongLines;

    /// <summary>Which image model pages are drawn with.</summary>
    public ImageModelPolicy ImageModelPolicy { get; set; } = ImageModelPolicy.Auto;

    /// <summary>Registry model id used when <see cref="ImageModelPolicy"/> is <see cref="ImageModelPolicy.Explicit"/>.</summary>
    public string? ExplicitImageModelId { get; set; }

    public Guid? CoverImageId { get; set; }

    /// <summary>
    /// An image attached to every page as a Style reference. This is the
    /// strongest cheap lever for making pages look like one book.
    /// </summary>
    public Guid? StyleAnchorImageId { get; set; }

    /// <summary>Planned with the old panel-based flow, before page scripting.</summary>
    public bool IsLegacy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ComicPage> Pages { get; set; } = [];

    public List<CurationRun> CurationRuns { get; set; } = [];

    public List<Scene> Scenes { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<DialogueAdaptation>))]
public enum DialogueAdaptation
{
    /// <summary>Keep the author's words exactly.</summary>
    Verbatim = 0,

    /// <summary>Keep short lines, compress long ones for the bubble.</summary>
    ShortenLongLines = 1,

    /// <summary>Let the curator rewrite freely for comic pacing.</summary>
    RewriteFreely = 2,
}

/// <summary>
/// The art direction for an entire comic. Every panel inherits these so the
/// book looks like one book.
/// </summary>
public class ComicStylePreset
{
    public string Name { get; set; } = "Manga — monochrome";

    /// <summary>Style tags prepended to every base caption.</summary>
    public string BaseTags { get; set; } = "manga, monochrome, greyscale, screentone, lineart, high contrast";

    /// <summary>Project-wide negatives, applied to every panel.</summary>
    /// <remarks>
    /// Panel borders, bubbles and text are deliberately excluded here: we draw
    /// our own lettering, so the model must not bake any in.
    /// </remarks>
    public string NegativeTags { get; set; } =
        "color, watermark, signature, jpeg artifacts, lowres, bad anatomy, extra digits, "
        + "multiple views, panel border, speech bubble, text, english text, comic page, 4koma";

    /// <summary>Registry model id; null means the account's preferred image model.</summary>
    public string? ModelId { get; set; }

    public string Sampler { get; set; } = "k_euler_ancestral";

    public string NoiseSchedule { get; set; } = "karras";

    public int Steps { get; set; } = 28;

    public double Scale { get; set; } = 5.0;

    public int UcPreset { get; set; } = 0;

    public bool QualityToggle { get; set; } = true;

    /// <summary>Optional style reference image, used as a Precise Reference.</summary>
    public Guid? StyleReferenceImageId { get; set; }

    public double StyleReferenceStrength { get; set; } = 0.6;

    public double StyleReferenceFidelity { get; set; } = 0.4;

    /// <summary>Keeps the panel look stable across a long book.</summary>
    public long? Seed { get; set; }
}
