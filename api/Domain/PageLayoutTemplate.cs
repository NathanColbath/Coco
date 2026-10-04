using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

/// <summary>A panel slot in a page template, in normalised page coordinates.</summary>
public class LayoutPanelSlot
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 1;

    public double Height { get; set; } = 1;

    /// <summary>Optional hint about what this slot suits, e.g. "wide" or "tall".</summary>
    public string? Hint { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<LayoutCategory>))]
public enum LayoutCategory
{
    Grid = 0,
    Action = 1,
    Dialogue = 2,
    Splash = 3,
    Strip = 4,
}

/// <summary>
/// A page structure. Stored in the database so the library is data, and so users
/// can author their own without a code change.
/// </summary>
public class PageLayoutTemplate
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public LayoutCategory Category { get; set; } = LayoutCategory.Grid;

    /// <summary>Panels per page, in reading order.</summary>
    public int PanelCount { get; set; }

    /// <summary>Gutter between panels, as a fraction of page width.</summary>
    public double Gutter { get; set; } = 0.022;

    /// <summary>Outer margin, as a fraction of page width.</summary>
    public double Margin { get; set; } = 0.035;

    /// <summary>Panel border thickness, as a fraction of page width.</summary>
    public double BorderThickness { get; set; } = 0.004;

    public bool IsSystem { get; set; }

    public int SortOrder { get; set; }

    public string? Description { get; set; }

    /// <summary>Serialised <see cref="LayoutPanelSlot"/> list in reading order.</summary>
    public string SlotsJson { get; set; } = "[]";

    public List<LayoutPanelSlot> Slots { get; set; } = [];
}
