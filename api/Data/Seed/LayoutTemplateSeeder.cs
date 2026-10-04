using System.Text.Json;
using ComicStudio.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Data.Seed;

/// <summary>
/// The manga page library. Templates are authored in reading order with
/// right-to-left geometry; the renderer mirrors them for left-to-right comics.
/// Panels come from weighted rows so the structures stay readable here.
/// </summary>
public static class LayoutTemplateSeeder
{
    private const double Margin = 0.035;
    private const double Gutter = 0.022;

    private static readonly JsonSerializerOptions Json = new();

    private static double[] Columns(params double[] weights) => weights;

    private static readonly PageLayoutTemplate[] Templates =
    [
        Template("splash", "Splash page", LayoutCategory.Splash, "One full-page drawing.", 10,
            (1.0, Columns(1))),
        Template("strip-2", "Two strips", LayoutCategory.Strip, "Two stacked widescreen panels.", 20,
            (1.0, Columns(1)), (1.0, Columns(1))),
        Template("strip-3", "Three strips", LayoutCategory.Strip, "Classic three-tier page.", 30,
            (1.0, Columns(1)), (1.0, Columns(1)), (1.0, Columns(1))),
        Template("yonkoma", "Four strips", LayoutCategory.Strip, "Four equal tiers, newspaper style.", 40,
            (1.0, Columns(1)), (1.0, Columns(1)), (1.0, Columns(1)), (1.0, Columns(1))),
        Template("grid-2x2", "Two by two", LayoutCategory.Grid, "Four even panels.", 50,
            (1.0, Columns(1, 1)), (1.0, Columns(1, 1))),
        Template("grid-2x3", "Two by three", LayoutCategory.Grid, "Six even panels for a dense beat.", 60,
            (1.0, Columns(1, 1)), (1.0, Columns(1, 1)), (1.0, Columns(1, 1))),
        Template("grid-3x3", "Three by three", LayoutCategory.Grid, "Nine panels — montage or rapid cuts.", 70,
            (1.0, Columns(1, 1, 1)), (1.0, Columns(1, 1, 1)), (1.0, Columns(1, 1, 1))),
        Template("dialogue-5", "Dialogue five", LayoutCategory.Dialogue, "Two, one wide, two — conversation rhythm.", 80,
            (0.9, Columns(1, 1)), (0.7, Columns(1)), (0.9, Columns(1, 1))),
        Template("focus-top", "Focus top", LayoutCategory.Action, "Large panel over two beats.", 90,
            (1.8, Columns(1)), (1.0, Columns(1, 1))),
        Template("focus-bottom", "Focus bottom", LayoutCategory.Action, "Two beats building to a large payoff.", 100,
            (1.0, Columns(1, 1)), (1.8, Columns(1))),
        Template("action-3", "Action three", LayoutCategory.Action, "Big action panel above two reactions.", 110,
            (1.9, Columns(1)), (1.0, Columns(1, 1))),
        Template("wide-top-3", "Wide over two", LayoutCategory.Action, "Widescreen opener, two panels beneath.", 120,
            (1.3, Columns(1)), (1.0, Columns(1, 1))),
        Template("triptych", "Triptych", LayoutCategory.Grid, "Three vertical columns.", 130,
            (1.0, Columns(1, 1, 1))),
        Template("stack-right", "Stacked right", LayoutCategory.Dialogue, "Two stacked panels beside one tall panel.", 140,
            (1.0, Columns(1, 1)), (1.0, Columns(1))),
    ];

    private static PageLayoutTemplate Template(
        string id,
        string name,
        LayoutCategory category,
        string description,
        int sortOrder,
        params (double Weight, double[] Columns)[] rows
    )
    {
        var slots = new List<LayoutPanelSlot>();
        var contentWidth = 1 - (Margin * 2);
        var contentHeight = 1 - (Margin * 2);
        var totalWeight = rows.Sum(row => row.Weight);
        var totalGutter = Gutter * (rows.Length - 1);
        var rowHeightUnit = (contentHeight - totalGutter) / totalWeight;

        var y = Margin;

        foreach (var (weight, columns) in rows)
        {
            var rowHeight = rowHeightUnit * weight;
            var columnGutter = Gutter * (columns.Length - 1);
            var columnUnit = (contentWidth - columnGutter) / columns.Sum();

            // The first column sits at the right edge: manga reads right to left.
            var right = 1 - Margin;

            foreach (var columnWeight in columns)
            {
                var width = columnUnit * columnWeight;
                slots.Add(
                    new LayoutPanelSlot
                    {
                        X = right - width,
                        Y = y,
                        Width = width,
                        Height = rowHeight,
                    }
                );
                right -= width + Gutter;
            }

            y += rowHeight + Gutter;
        }

        return new PageLayoutTemplate
        {
            Id = id,
            Name = name,
            Category = category,
            Description = description,
            SortOrder = sortOrder,
            PanelCount = slots.Count,
            Gutter = Gutter,
            Margin = Margin,
            BorderThickness = 0.004,
            IsSystem = true,
            SlotsJson = JsonSerializer.Serialize(slots, Json),
        };
    }

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var existing = await db.LayoutTemplates.ToDictionaryAsync(
            template => template.Id,
            cancellationToken
        );

        foreach (var seed in Templates)
        {
            if (existing.TryGetValue(seed.Id, out var current))
            {
                current.Name = seed.Name;
                current.Category = seed.Category;
                current.Description = seed.Description;
                current.SortOrder = seed.SortOrder;
                current.PanelCount = seed.PanelCount;
                current.Gutter = seed.Gutter;
                current.Margin = seed.Margin;
                current.BorderThickness = seed.BorderThickness;
                current.SlotsJson = seed.SlotsJson;
            }
            else
            {
                db.LayoutTemplates.Add(seed);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
