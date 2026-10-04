using ComicStudio.Api.Domain;
using ComicStudio.Api.Storytelling;

namespace ComicStudio.Api.Tests.Storytelling;

public class ContextAssemblerTests
{
    private static readonly ContextAssembler Assembler = new(new HeuristicTokenCounter());

    [Fact]
    public void Chat_transport_sends_the_system_prompt_as_its_own_message()
    {
        var story = TestData.Story("The gate opened.");

        var context = Assembler.Assemble(TestData.Request(story));

        Assert.Equal(2, context.Messages.Count);
        Assert.Equal("system", context.Messages[0].Role);
        Assert.Equal(ContextAssembler.DefaultChatSystemPrompt, context.Messages[0].Content);
        Assert.Equal("user", context.Messages[1].Role);
        Assert.DoesNotContain("GLM-4.6-based LLM", context.Prompt);
    }

    [Fact]
    public void Completion_transport_folds_the_system_prompt_into_the_prompt()
    {
        var story = TestData.Story("The gate opened.");
        story.Context.SystemPrompt = "Write in terse prose.";

        var context = Assembler.Assemble(
            TestData.Request(story, transport: "legacy-completion")
        );

        Assert.Single(context.Messages);
        Assert.Contains("Write in terse prose.", context.Prompt);
    }

    [Fact]
    public void Memory_leads_with_an_attg_line_built_from_story_metadata()
    {
        var story = TestData.Story("The gate opened.");

        var context = Assembler.Assemble(TestData.Request(story));
        var memory = TestData.SegmentText(context, "Memory");

        Assert.StartsWith("[ Author: ; Title: The Clockwork Garden ; Tags: steampunk, mystery ; Genre: Fantasy ]", memory);
    }

    [Fact]
    public void Memory_includes_synopsis_then_the_memory_field()
    {
        var story = TestData.Story("The gate opened.");
        story.Synopsis = "A gardener tends a mechanical world.";
        story.Context.Memory = "Ana is the protagonist.";

        var memory = TestData.SegmentText(Assembler.Assemble(TestData.Request(story)), "Memory");

        Assert.Contains("A gardener tends a mechanical world.", memory);
        Assert.Contains("***", memory);
        Assert.Contains("Ana is the protagonist.", memory);
        Assert.True(memory.IndexOf("***") < memory.IndexOf("Ana is the protagonist."));
    }

    [Fact]
    public void Author_note_is_spliced_in_lines_up_from_the_bottom()
    {
        var story = TestData.Story("Line one\nLine two\nLine three\nLine four");
        story.Context.AuthorNote = "[ Style: terse ]";
        story.Context.AuthorNoteDepth = 2;

        var context = Assembler.Assemble(TestData.Request(story));

        Assert.Equal("Line one\nLine two", TestData.SegmentText(context, "Story"));
        Assert.Equal("Line three\nLine four", TestData.SegmentText(context, "Story (recent)"));
        Assert.Equal("[ Style: terse ]", TestData.SegmentText(context, "Author's note"));

        // Order in the final prompt: story head, note, then the most recent lines.
        var noteIndex = context.Prompt.IndexOf("[ Style: terse ]", StringComparison.Ordinal);
        var recentIndex = context.Prompt.IndexOf("Line four", StringComparison.Ordinal);
        Assert.True(noteIndex > context.Prompt.IndexOf("Line two", StringComparison.Ordinal));
        Assert.True(noteIndex < recentIndex);
    }

    [Fact]
    public void Author_note_follows_memory_when_there_is_no_story_yet()
    {
        var story = TestData.Story();
        story.Context.AuthorNote = "[ Style: lush ]";

        var context = Assembler.Assemble(TestData.Request(story));

        Assert.Equal("[ Style: lush ]", TestData.SegmentText(context, "Author's note"));
        Assert.Contains("[ Style: lush ]", context.Prompt);
    }

    [Fact]
    public void Always_on_lorebook_entries_appear_at_the_top_by_default()
    {
        var story = TestData.Story("The gate opened.");
        var entry = TestData.Entry("World rules", "Magic costs memory.", alwaysOn: true);

        var context = Assembler.Assemble(TestData.Request(story, lorebook: entry));

        Assert.Equal("World rules", context.Trace.Segments[0].Name);
        Assert.StartsWith("Magic costs memory.", context.Prompt);
    }

    [Fact]
    public void Key_triggered_entries_only_activate_on_a_match()
    {
        var story = TestData.Story("Ana walked through the garden.");
        var entry = TestData.Entry("Ana", "Ana is the royal gardener.", keys: "Ana");

        var activated = Assembler.Assemble(TestData.Request(story, lorebook: entry));
        Assert.Contains("Ana is the royal gardener.", activated.Prompt);

        var quiet = TestData.Story("The machine hummed quietly.");
        var skipped = Assembler.Assemble(TestData.Request(quiet, lorebook: entry));
        Assert.DoesNotContain("Ana is the royal gardener.", skipped.Prompt);
    }

    [Fact]
    public void Negative_insertion_positions_place_entries_near_the_bottom()
    {
        var story = TestData.Story("The gate opened.");
        story.Context.AuthorNote = "[ Style: terse ]";
        var entry = TestData.Entry("Secret", "The gate is a lie.", alwaysOn: true, insertionPosition: -1);

        var context = Assembler.Assemble(TestData.Request(story, lorebook: entry));

        var names = context.Trace.Segments.Select(segment => segment.Name).ToList();
        Assert.Equal("Secret", names[^1]);
    }

    [Fact]
    public void Key_relative_entries_are_inserted_right_after_their_match()
    {
        var story = TestData.Story("First line.\nThe lantern glows.\nThird line.");
        var entry = TestData.Entry(
            "Lantern",
            "The lantern is a trapped sun.",
            keys: "lantern",
            keyRelative: true
        );

        var context = Assembler.Assemble(TestData.Request(story, lorebook: entry));
        var prompt = context.Prompt;

        Assert.True(
            prompt.IndexOf("The lantern glows.", StringComparison.Ordinal)
                < prompt.IndexOf("The lantern is a trapped sun.", StringComparison.Ordinal)
        );
        Assert.True(
            prompt.IndexOf("The lantern is a trapped sun.", StringComparison.Ordinal)
                < prompt.IndexOf("Third line.", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Story_context_is_trimmed_from_the_oldest_end_when_over_budget()
    {
        string Filler(string marker) =>
            marker + " " + string.Join(" ", Enumerable.Repeat("clockwork", 80));

        var story = TestData.Story(Filler("OLDEST"), Filler("MIDDLE"), Filler("NEWEST"));
        story.Context.SystemPrompt = "Be brief.";

        // ~398 usable tokens against ~765 of story: only the newest block survives.
        var context = Assembler.Assemble(
            TestData.Request(story, contextTokens: 512, outputTokens: 50)
        );

        Assert.True(context.Trace.Trimmed);
        Assert.DoesNotContain("OLDEST", context.Prompt);
        Assert.Contains("NEWEST", context.Prompt);
        Assert.Contains(context.Trace.Warnings, warning => warning.Contains("trimmed"));
    }

    [Fact]
    public void Entries_that_cannot_fit_are_skipped_with_a_warning()
    {
        var story = TestData.Story("Short.");
        var huge = TestData.Entry(
            "Encyclopedia",
            string.Concat(Enumerable.Repeat("Very long lore text about everything. ", 200)),
            alwaysOn: true,
            insertionOrder: 900,
            trim: LorebookTrimDirection.DoNotTrim
        );

        var context = Assembler.Assemble(
            TestData.Request(story, contextTokens: 300, outputTokens: 50, lorebook: huge)
        );

        Assert.DoesNotContain("Very long lore text", context.Prompt);
        Assert.Contains(context.Trace.Warnings, warning => warning.Contains("Encyclopedia"));
    }

    [Fact]
    public void Instruction_is_appended_last()
    {
        var story = TestData.Story("The gate opened.");

        var context = Assembler.Assemble(
            TestData.Request(story, instruction: "Continue in Ana's point of view.")
        );

        Assert.EndsWith("Continue in Ana's point of view.", context.Prompt);
        Assert.Equal("Instruction", context.Trace.Segments[^1].Name);
    }

    [Fact]
    public void Trace_accounts_for_every_segment_and_the_budget()
    {
        var story = TestData.Story("The gate opened.", "Something stirred beyond it.");
        story.Context.AuthorNote = "[ Style: tense ]";

        var context = Assembler.Assemble(TestData.Request(story, contextTokens: 4096));
        var trace = context.Trace;

        Assert.Equal(4096, trace.ContextWindowTokens);
        Assert.Equal(200, trace.OutputReserveTokens);
        Assert.True(trace.AvailableTokens < trace.ContextWindowTokens);
        Assert.Equal(context.PromptTokens, trace.UsedTokens);
        Assert.Equal(trace.Segments.Sum(segment => segment.Tokens), trace.UsedTokens);
        Assert.True(trace.UsedTokens <= trace.AvailableTokens);
    }
}
