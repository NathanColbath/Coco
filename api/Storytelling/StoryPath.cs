using ComicStudio.Api.Domain;

namespace ComicStudio.Api.Storytelling;

/// <summary>
/// Walks the block tree from the cursor back to the root, yielding the active
/// branch oldest-first. Alternatives created by retries stay in the table but
/// are not part of the path.
/// </summary>
public static class StoryPath
{
    private const int MaxDepth = 10_000;

    public static IReadOnlyList<StoryBlock> Build(IEnumerable<StoryBlock> blocks, Guid? cursorId)
    {
        if (cursorId is null)
        {
            return [];
        }

        var byId = blocks.ToDictionary(block => block.Id);
        var path = new List<StoryBlock>();
        var current = cursorId;
        var guard = 0;

        while (
            current is { } id
            && byId.TryGetValue(id, out var block)
            && guard++ < MaxDepth
        )
        {
            path.Add(block);
            current = block.ParentId;
        }

        path.Reverse();
        return path;
    }

    public static string Text(IReadOnlyList<StoryBlock> path)
    {
        var parts = path
            .Select(block => block.Kind == StoryBlockKind.SceneBreak ? "***" : block.Text.Trim())
            .Where(text => text.Length > 0);

        return string.Join("\n", parts);
    }

    public static int WordCount(string text) =>
        text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
}
