using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<PageStatus>))]
public enum PageStatus
{
    Planned = 0,
    Generating = 1,
    Generated = 2,
    Rendered = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter<PanelShot>))]
public enum PanelShot
{
    Establishing = 0,
    Wide = 1,
    Medium = 2,
    CloseUp = 3,
    ExtremeCloseUp = 4,
    OverShoulder = 5,
    Insert = 6,
    Splash = 7,
    DutchAngle = 8,
}

[JsonConverter(typeof(JsonStringEnumConverter<DialogueKind>))]
public enum DialogueKind
{
    Speech = 0,
    Thought = 1,
    Narration = 2,
    Sfx = 3,
}

/// <summary>
/// One page of the comic. The page is the unit of generation: its prompt
/// describes every panel it contains, and the lettering is drawn by the model
/// rather than typeset afterwards.
/// </summary>
public class ComicPage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public ComicProject? Project { get; set; }

    public int Index { get; set; }

    public string LayoutTemplateId { get; set; } = "grid-2x2";

    public PageStatus Status { get; set; } = PageStatus.Planned;

    public string? Notes { get; set; }

    /// <summary>
    /// The curator's description of this page: what is drawn, panel by panel.
    /// This is the prompt, so detail here is what buys consistency.
    /// </summary>
    public string? Script { get; set; }

    /// <summary>
    /// The state carried out of this page — location, character condition, props,
    /// time — fed into every later page so the story reads as one continuous
    /// comic rather than a set of unrelated pictures.
    /// </summary>
    public string? Continuity { get; set; }

    /// <summary>The scene this page belongs to, for grouping and context.</summary>
    public string? SceneTitle { get; set; }

    public Guid? PageImageId { get; set; }

    /// <summary>Composed image with lettering, ready to read.</summary>
    public Guid? RenderImageId { get; set; }

    public string? PositivePrompt { get; set; }

    public string? NegativePrompt { get; set; }

    public string? PromptSnapshotJson { get; set; }

    public long? Seed { get; set; }

    /// <summary>The scene this page belongs to, once pages are grouped into scenes.</summary>
    public Guid? SceneId { get; set; }

    /// <summary>The environment drawn on this page.</summary>
    public Guid? EnvironmentId { get; set; }

    /// <summary>Set when the page ends by moving somewhere else.</summary>
    public Guid? MovesToEnvironmentId { get; set; }

    /// <summary>
    /// Structured panel descriptions, stored as JSON text. Optional: the script
    /// remains the source of truth when this is empty.
    /// </summary>
    public string? PanelPlanJson { get; set; }

    /// <summary>Registry id of the model that drew this page.</summary>
    public string? ModelUsed { get; set; }

    /// <summary>Per-page model override, when set.</summary>
    public string? ModelOverride { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<DialogueLine> Dialogue { get; set; } = [];
}

/// <summary>
/// A line that appears on the page. Its text is drawn into the image, so this
/// record is the source of truth for what the bubble must say.
/// </summary>
public class DialogueLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PageId { get; set; }

    public ComicPage? Page { get; set; }

    public int Index { get; set; }

    public DialogueKind Kind { get; set; } = DialogueKind.Speech;

    public Guid? SpeakerCharacterId { get; set; }

    public Character? Speaker { get; set; }

    /// <summary>Drawn verbatim. Kept short: text rendering degrades with length.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Where the bubble belongs on the page: upper right, lower left, and so on.
    /// Only a hint for the model now — nothing is typeset on top.
    /// </summary>
    public string? Position { get; set; }
}
