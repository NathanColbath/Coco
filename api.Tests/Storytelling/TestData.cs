using ComicStudio.Api.Domain;
using ComicStudio.Api.Storytelling;

namespace ComicStudio.Api.Tests.Storytelling;

internal static class TestData
{
    public static readonly ITokenCounter Counter = new HeuristicTokenCounter();

    public static Domain.Story Story(params string[] paragraphs)
    {
        var story = new Domain.Story
        {
            Title = "The Clockwork Garden",
            Genre = "Fantasy",
            Tags = "steampunk, mystery",
        };

        Guid? parentId = null;
        foreach (var paragraph in paragraphs)
        {
            var block = new StoryBlock
            {
                StoryId = story.Id,
                ParentId = parentId,
                Origin = StoryBlockOrigin.Ai,
                Text = paragraph,
                TokenCount = Counter.Count(paragraph),
            };
            story.Blocks.Add(block);
            parentId = block.Id;
        }

        return story;
    }

    public static ContextAssemblyRequest Request(
        Domain.Story story,
        string transport = "openai-chat",
        int contextTokens = 8192,
        int outputTokens = 200,
        string? instruction = null,
        params LorebookEntry[] lorebook
    ) =>
        new()
        {
            Story = story,
            ActivePath = story.Blocks,
            ModelId = "test-model",
            Transport = transport,
            ContextTokens = contextTokens,
            OutputTokens = outputTokens,
            Instruction = instruction,
            LorebookEntries = lorebook,
        };

    public static LorebookEntry Entry(
        string name,
        string content,
        string? keys = null,
        int insertionOrder = 100,
        int insertionPosition = 0,
        bool alwaysOn = false,
        bool keyRelative = false,
        LorebookTrimDirection trim = LorebookTrimDirection.DoNotTrim,
        bool enabled = true
    ) =>
        new()
        {
            DisplayName = name,
            Content = content,
            KeysRaw = keys ?? string.Empty,
            InsertionOrder = insertionOrder,
            InsertionPosition = insertionPosition,
            AlwaysOn = alwaysOn,
            KeyRelative = keyRelative,
            TrimDirection = trim,
            Enabled = enabled,
        };

    public static string SegmentText(AssembledContext context, string name) =>
        context.Trace.Segments.First(segment => segment.Name == name).Text;
}
