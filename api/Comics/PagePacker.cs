using ComicStudio.Api.Storytelling;

namespace ComicStudio.Api.Comics;

public record PackedPage(string TemplateId, IReadOnlyList<PlannedPanel> Panels);

/// <summary>
/// Assigns panels to pages. Deliberately deterministic and free of model calls:
/// the curator decides what happens, this decides how it is laid out, so the
/// same plan always produces the same book.
/// </summary>
public static class PagePacker
{
    private const int PreferredPanelsPerPage = 4;

    public static List<PackedPage> Pack(IReadOnlyList<PlannedPanel> panels)
    {
        var pages = new List<PackedPage>();
        var queue = new Queue<PlannedPanel>(panels);

        while (queue.Count > 0)
        {
            if (IsSplash(queue.Peek()))
            {
                var splash = queue.Dequeue();
                pages.Add(new PackedPage("splash", [splash]));
                continue;
            }

            var remaining = queue.Count;
            var take = Math.Min(PreferredPanelsPerPage, remaining);

            // Never swallow a splash: cut the page short so it lands on its own.
            for (var offset = 1; offset < take; offset++)
            {
                if (IsSplash(queue.ElementAt(offset)))
                {
                    take = offset;
                    break;
                }
            }

            // Never strand a single panel on a page of its own unless it's a
            // deliberate beat; rebalance instead.
            if (remaining - take == 1 && take > 2)
            {
                take--;
            }

            var slice = new List<PlannedPanel>(take);
            for (var index = 0; index < take && queue.Count > 0; index++)
            {
                slice.Add(queue.Dequeue());
            }

            pages.Add(new PackedPage(TemplateFor(slice), slice));
        }

        return pages;
    }

    private static bool IsSplash(PlannedPanel panel) =>
        Math.Equals(ShotOf(panel), PanelShotAlias.Splash)
        || (
            IsHighImportance(panel)
            && ShotOf(panel) is PanelShotAlias.Wide or PanelShotAlias.Establishing
        );

    private static string TemplateFor(IReadOnlyList<PlannedPanel> panels)
    {
        var dialogueHeavy = panels.Count(panel => panel.Dialogue.Count > 0) >= 2;

        return panels.Count switch
        {
            1 => "splash",
            2 => IsHighImportance(panels[0]) ? "focus-top" : "strip-2",
            3 => dialogueHeavy ? "strip-3" : "action-3",
            4 => "grid-2x2",
            5 => "dialogue-5",
            6 => "grid-2x3",
            _ => "grid-2x3",
        };
    }

    private static bool IsHighImportance(PlannedPanel panel) =>
        string.Equals(panel.Importance, "high", StringComparison.OrdinalIgnoreCase);

    public enum PanelShotAlias
    {
        Establishing,
        Wide,
        Medium,
        CloseUp,
        ExtremeCloseUp,
        OverShoulder,
        Insert,
        Splash,
        DutchAngle,
    }

    public static PanelShotAlias ShotOf(PlannedPanel panel) =>
        Enum.TryParse<PanelShotAlias>(panel.Shot, ignoreCase: true, out var shot)
            ? shot
            : PanelShotAlias.Medium;
}

/// <summary>Splits long prose so each curator pass stays inside the model window.</summary>
public static class TextChunker
{
    public static List<string> Chunk(string text, ITokenCounter counter, int maxTokens)
    {
        var chunks = new List<string>();

        if (counter.Count(text) <= maxTokens)
        {
            return [text];
        }

        var paragraphs = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var current = new System.Text.StringBuilder();
        var currentTokens = 0;

        foreach (var paragraph in paragraphs)
        {
            var tokens = counter.Count(paragraph);

            if (currentTokens > 0 && currentTokens + tokens > maxTokens)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
                currentTokens = 0;
            }

            // A single paragraph longer than the budget gets hard-split.
            if (tokens > maxTokens)
            {
                var words = paragraph.Split(' ');
                var slice = new System.Text.StringBuilder();
                var sliceTokens = 0;

                foreach (var word in words)
                {
                    var wordTokens = counter.Count(word);
                    if (sliceTokens + wordTokens > maxTokens && slice.Length > 0)
                    {
                        chunks.Add(slice.ToString().Trim());
                        slice.Clear();
                        sliceTokens = 0;
                    }

                    slice.Append(word).Append(' ');
                    sliceTokens += wordTokens;
                }

                if (slice.Length > 0)
                {
                    current.Append(slice);
                    currentTokens = sliceTokens;
                }

                continue;
            }

            current.Append(paragraph).Append('\n');
            currentTokens += tokens;
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
        }

        return chunks;
    }
}
