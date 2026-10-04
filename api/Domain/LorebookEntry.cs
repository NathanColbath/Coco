using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<LorebookTrimDirection>))]
public enum LorebookTrimDirection
{
    DoNotTrim = 0,
    TrimTop = 1,
    TrimBottom = 2,
}

/// <summary>
/// Supplemental context injected when its keys appear in recent story text
/// (or always, when <see cref="AlwaysOn"/> is set).
/// </summary>
public class LorebookEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Activation keys, one per line (as authored in the UI).</summary>
    public string KeysRaw { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? Category { get; set; }

    public bool Enabled { get; set; } = true;

    public bool AlwaysOn { get; set; }

    /// <summary>Higher orders reserve tokens and are placed first.</summary>
    public int InsertionOrder { get; set; } = 100;

    /// <summary>0 is the top of context; negative counts up from the bottom.</summary>
    public int InsertionPosition { get; set; }

    public string Prefix { get; set; } = string.Empty;

    public string Suffix { get; set; } = string.Empty;

    /// <summary>Tokens this entry may claim. 0.-1 is treated as a fraction of the budget.</summary>
    public decimal ReservedTokens { get; set; }

    public LorebookTrimDirection TrimDirection { get; set; } = LorebookTrimDirection.DoNotTrim;

    /// <summary>Insert relative to the last matching key instead of at a fixed position.</summary>
    public bool KeyRelative { get; set; }
}
