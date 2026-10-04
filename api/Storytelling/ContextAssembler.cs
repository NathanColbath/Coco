using System.Text;
using ComicStudio.Api.Domain;

namespace ComicStudio.Api.Storytelling;

public class ContextAssemblyRequest
{
    public required Story Story { get; init; }

    /// <summary>The active branch, oldest block first.</summary>
    public required IReadOnlyList<StoryBlock> ActivePath { get; init; }

    public required string ModelId { get; init; }

    public required string Transport { get; init; }

    /// <summary>Tier-adjusted context window for the model.</summary>
    public required int ContextTokens { get; init; }

    public int RolloverTokens { get; init; }

    public int OutputTokens { get; init; } = 160;

    public string? Instruction { get; init; }

    public IReadOnlyList<LorebookEntry> LorebookEntries { get; init; } = [];
}

/// <summary>
/// Builds the exact text sent to a text model: system prompt, memory (with the
/// ATTG block), lorebook entries placed by insertion position, the story so far
/// trimmed to fit, the author's note at depth, and any per-generation steer.
///
/// Positioning works at line granularity, matching NovelAI: insertion position 0
/// is the top of the context, -1 is the very bottom, and the author's note sits
/// <c>depth</c> lines up from the bottom. Assembly is deterministic and returns a
/// trace of every segment, which is what makes the context viewer possible and
/// the behaviour testable.
/// </summary>
public class ContextAssembler(ITokenCounter counter)
{
    /// <summary>
    /// NovelAI's own default system prompt for the GLM-based models, kept close to
    /// the original because the model is tuned against it.
    /// </summary>
    public const string DefaultChatSystemPrompt =
        "You are a GLM-4.6-based LLM (originally by Zhipu AI) customized by NovelAI "
        + "specifically for storytelling, poetry, lyrics, running text adventures and "
        + "roleplaying. NovelAI's values include complete narrative freedom and user "
        + "privacy. You cannot use tools, nor can you browse the web, so inform the user "
        + "about this if tool use or web access is requested. You are stylistically "
        + "highly flexible to facilitate storytelling in whatever style a user desires.";

    private const string StoryLineKind = "storyline";
    private const string StorySegmentKind = "story";
    private const string SystemKind = "system";

    private const int SafetyMarginTokens = 64;
    private const int MinimumStoryTokens = 128;
    private const int MinAvailableTokens = 256;
    private const double EstimatedCharsPerToken = 4.0;

    public AssembledContext Assemble(ContextAssemblyRequest request)
    {
        var warnings = new List<string>();
        var settings = request.Story.Context;

        var window = Math.Max(0, request.ContextTokens) + Math.Max(0, request.RolloverTokens);
        var available = Math.Max(
            MinAvailableTokens,
            window - request.OutputTokens - SafetyMarginTokens
        );

        var completionTransport = IsCompletionTransport(request.Transport);
        var systemText = BuildSystemText(request.Story, completionTransport);
        var memoryText = BuildMemory(request.Story);
        var authorNoteText = settings.AuthorNote?.Trim() ?? string.Empty;
        var instructionText = request.Instruction?.Trim() ?? string.Empty;

        var fixedTokens =
            counter.Count(systemText)
            + counter.Count(memoryText)
            + counter.Count(authorNoteText)
            + counter.Count(instructionText);

        // --- Lorebook ------------------------------------------------------
        var lorebookBudget = Math.Max(0, available - fixedTokens - MinimumStoryTokens);
        var scanText = BuildScanText(request.ActivePath, settings.LorebookScanTokens);
        var activated = LorebookMatcher.Select(request.LorebookEntries, scanText);

        var placed = new List<PlacedEntry>();
        var remainingLorebook = lorebookBudget;
        foreach (var entry in activated)
        {
            var content = ApplyAffixes(entry);
            var tokens = counter.Count(content);

            if (tokens <= remainingLorebook)
            {
                placed.Add(new PlacedEntry(entry, content, tokens, Trimmed: false));
                remainingLorebook -= tokens;
                continue;
            }

            if (entry.TrimDirection == LorebookTrimDirection.DoNotTrim)
            {
                warnings.Add($"Lorebook entry “{entry.DisplayName}” did not fit and was skipped.");
                continue;
            }

            var trimmed = TrimToTokens(content, remainingLorebook, entry.TrimDirection);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                warnings.Add($"Lorebook entry “{entry.DisplayName}” did not fit and was skipped.");
                continue;
            }

            var trimmedTokens = counter.Count(trimmed);
            placed.Add(new PlacedEntry(entry, trimmed, trimmedTokens, Trimmed: true));
            remainingLorebook -= trimmedTokens;
            warnings.Add($"Lorebook entry “{entry.DisplayName}” was trimmed to fit.");
        }

        // --- Story ---------------------------------------------------------
        var lorebookTokens = placed.Sum(entry => entry.Tokens);
        var storyBudget = Math.Max(MinimumStoryTokens, available - fixedTokens - lorebookTokens);

        var story = BuildStoryText(request.ActivePath, storyBudget);
        if (story.Trimmed)
        {
            warnings.Add($"Story context was trimmed to the most recent ~{storyBudget:N0} tokens.");
        }

        // --- Compose -------------------------------------------------------
        var units = Compose(
            systemText,
            memoryText,
            story.Text,
            authorNoteText,
            instructionText,
            placed,
            settings.AuthorNoteDepth
        );

        var segments = Merge(units);
        var traced = segments
            .Select(segment => new ContextSegment(
                segment.Name,
                segment.Kind,
                segment.Text,
                counter.Count(segment.Text),
                segment.Note
            ))
            .ToArray();

        var used = traced.Sum(segment => segment.Tokens);

        var trace = new ContextTrace(
            request.ModelId,
            request.Transport,
            window,
            request.OutputTokens,
            available,
            used,
            fixedTokens,
            story.Trimmed,
            traced,
            warnings.ToArray()
        );

        var prompt = Join(units, includeSystemSegment: completionTransport);
        var messages = BuildMessages(completionTransport, systemText, prompt);

        return new AssembledContext(prompt, messages, trace, used);
    }

    /// <summary>The ATTG line, synopsis, and the author's memory field.</summary>
    private static string BuildMemory(Story story)
    {
        var builder = new StringBuilder();

        var attg = BuildAttg(story);
        if (attg.Length > 0)
        {
            builder.Append(attg);
        }

        if (!string.IsNullOrWhiteSpace(story.Synopsis))
        {
            builder.AppendLine().Append(story.Synopsis.Trim());
        }

        if (builder.Length > 0)
        {
            builder.AppendLine().Append("***");
        }

        var memory = story.Context.Memory?.Trim();
        if (!string.IsNullOrEmpty(memory))
        {
            builder.AppendLine().Append(memory);
        }

        return builder.ToString().Trim();
    }

    private static string BuildAttg(Story story)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(story.Title) && story.Title != "Untitled story")
        {
            parts.Add($"Title: {story.Title.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(story.Tags))
        {
            parts.Add($"Tags: {story.Tags.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(story.Genre))
        {
            parts.Add($"Genre: {story.Genre.Trim()}");
        }

        return parts.Count == 0 ? string.Empty : $"[ Author: ; {string.Join(" ; ", parts)} ]";
    }

    private static string BuildSystemText(Story story, bool completionTransport)
    {
        var configured = story.Context.SystemPrompt?.Trim();

        // Completion models have no system role, so an unset prompt means "none"
        // rather than the chat default.
        if (completionTransport)
        {
            return configured ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(configured) ? DefaultChatSystemPrompt : configured;
    }

    private string BuildScanText(IReadOnlyList<StoryBlock> path, int scanTokens)
    {
        if (path.Count == 0 || scanTokens <= 0)
        {
            return string.Empty;
        }

        var collected = new List<string>();
        var total = 0;

        for (var index = path.Count - 1; index >= 0; index--)
        {
            var text = BlockText(path[index]);
            collected.Add(text);
            total += counter.Count(text);

            if (total >= scanTokens)
            {
                break;
            }
        }

        collected.Reverse();
        return string.Join("\n", collected);
    }

    private (string Text, int Tokens, bool Trimmed) BuildStoryText(
        IReadOnlyList<StoryBlock> path,
        int budget
    )
    {
        if (path.Count == 0)
        {
            return (string.Empty, 0, false);
        }

        var blocks = path.ToList();
        var trimmed = false;

        while (blocks.Count > 0)
        {
            var text = JoinBlocks(blocks);
            var tokens = counter.Count(text);

            if (tokens <= budget)
            {
                return (text, tokens, trimmed);
            }

            if (blocks.Count == 1)
            {
                var truncated = TrimToTokens(text, budget, LorebookTrimDirection.TrimTop);
                return (truncated, counter.Count(truncated), true);
            }

            blocks.RemoveAt(0);
            trimmed = true;
        }

        return (string.Empty, 0, trimmed);
    }

    /// <summary>
    /// Emits the context as positioning units. Story text contributes one unit per
    /// line so insertion positions can land between lines, like NovelAI's.
    /// </summary>
    private static List<Unit> Compose(
        string systemText,
        string memoryText,
        string storyText,
        string authorNoteText,
        string instructionText,
        IReadOnlyList<PlacedEntry> lorebook,
        int authorNoteDepth
    )
    {
        var units = new List<Unit>();

        if (systemText.Length > 0)
        {
            units.Add(new Unit("System prompt", SystemKind, systemText));
        }

        if (memoryText.Length > 0)
        {
            units.Add(new Unit("Memory", "memory", memoryText));
        }

        var (storyHead, storyTail) = SplitForAuthorNote(
            storyText,
            authorNoteText,
            authorNoteDepth
        );

        AddStoryUnits(units, storyHead, "Story");

        if (authorNoteText.Length > 0)
        {
            units.Add(new Unit("Author's note", "authornote", authorNoteText));
        }

        AddStoryUnits(units, storyTail, "Story (recent)");

        foreach (var entry in lorebook)
        {
            Insert(units, entry);
        }

        if (instructionText.Length > 0)
        {
            units.Add(new Unit("Instruction", "instruction", instructionText));
        }

        return units;
    }

    private static void AddStoryUnits(List<Unit> units, string text, string name)
    {
        if (text.Length == 0)
        {
            return;
        }

        foreach (var line in text.Split('\n'))
        {
            units.Add(new Unit(name, StoryLineKind, line));
        }
    }

    private static (string Head, string Tail) SplitForAuthorNote(
        string storyText,
        string authorNoteText,
        int depth
    )
    {
        if (authorNoteText.Length == 0 || storyText.Length == 0)
        {
            return (storyText, string.Empty);
        }

        var lines = storyText.Split('\n');
        var index = Math.Clamp(lines.Length - Math.Max(1, depth), 0, lines.Length);

        var head = string.Join('\n', lines[..index]).TrimEnd();
        var tail = string.Join('\n', lines[index..]).TrimStart();
        return (head, tail);
    }

    private static void Insert(List<Unit> units, PlacedEntry entry)
    {
        var note = entry.Trimmed ? "trimmed to fit" : null;

        if (entry.Entry.KeyRelative)
        {
            var matchIndex = -1;
            for (var index = units.Count - 1; index >= 0; index--)
            {
                if (
                    units[index].Kind == StoryLineKind
                    && LorebookMatcher.LastMatchIndex(units[index].Text, entry.Entry) >= 0
                )
                {
                    matchIndex = index;
                    break;
                }
            }

            if (matchIndex >= 0)
            {
                units.Insert(
                    matchIndex + 1,
                    new Unit(entry.Entry.DisplayName, "lorebook", entry.Content, note)
                );
                return;
            }
        }

        var position = entry.Entry.InsertionPosition;
        var target = position >= 0
            ? Math.Clamp(position, 0, units.Count)
            : Math.Clamp(units.Count + position + 1, 0, units.Count);

        units.Insert(target, new Unit(entry.Entry.DisplayName, "lorebook", entry.Content, note));
    }

    /// <summary>Collapses consecutive story lines back into readable trace segments.</summary>
    private static List<Unit> Merge(List<Unit> units)
    {
        var merged = new List<Unit>();

        foreach (var unit in units)
        {
            if (
                unit.Kind == StoryLineKind
                && merged.Count > 0
                && merged[^1].Kind == StoryLineKind
                && merged[^1].Name == unit.Name
            )
            {
                var previous = merged[^1];
                merged[^1] = previous with { Text = previous.Text + "\n" + unit.Text };
                continue;
            }

            merged.Add(unit);
        }

        return merged
            .Select(unit =>
                unit.Kind == StoryLineKind ? unit with { Kind = StorySegmentKind } : unit
            )
            .ToList();
    }

    private static string ApplyAffixes(LorebookEntry entry)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(entry.Prefix))
        {
            builder.Append(entry.Prefix);
        }

        builder.Append(entry.Content);

        if (!string.IsNullOrEmpty(entry.Suffix))
        {
            builder.Append(entry.Suffix);
        }

        return builder.ToString();
    }

    private static string BlockText(StoryBlock block) =>
        block.Kind == StoryBlockKind.SceneBreak ? "***" : block.Text;

    private static string JoinBlocks(IEnumerable<StoryBlock> blocks)
    {
        var builder = new StringBuilder();
        foreach (var block in blocks)
        {
            var text = BlockText(block).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '\n')
            {
                builder.Append('\n');
            }

            builder.Append(text);
        }

        return builder.ToString();
    }

    private static string Join(IReadOnlyList<Unit> units, bool includeSystemSegment)
    {
        var builder = new StringBuilder();
        foreach (var unit in units)
        {
            // Empty story lines are meaningful (paragraph breaks) and must survive.
            if (unit.Text.Length == 0 && unit.Kind != StoryLineKind)
            {
                continue;
            }

            // For chat transports the system prompt travels as its own message.
            if (!includeSystemSegment && unit.Kind == SystemKind)
            {
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '\n')
            {
                builder.Append('\n');
            }

            builder.Append(unit.Text);
        }

        return builder.ToString();
    }

    private static IReadOnlyList<ContextMessage> BuildMessages(
        bool completionTransport,
        string systemText,
        string prompt
    )
    {
        if (completionTransport)
        {
            return [new ContextMessage("user", prompt)];
        }

        var messages = new List<ContextMessage>();
        if (systemText.Length > 0)
        {
            messages.Add(new ContextMessage("system", systemText));
        }

        messages.Add(new ContextMessage("user", prompt));
        return messages;
    }

    private static bool IsCompletionTransport(string transport) =>
        transport.StartsWith("legacy", StringComparison.OrdinalIgnoreCase);

    private string TrimToTokens(string text, int maxTokens, LorebookTrimDirection direction)
    {
        if (maxTokens <= 0)
        {
            return string.Empty;
        }

        var estimatedChars = (int)(maxTokens * EstimatedCharsPerToken);
        if (text.Length <= estimatedChars)
        {
            return text;
        }

        var trimStart = direction == LorebookTrimDirection.TrimTop;
        var slice = trimStart ? text[^estimatedChars..] : text[..estimatedChars];

        var boundary = trimStart
            ? Math.Max(slice.IndexOf('\n'), slice.IndexOf(' '))
            : Math.Max(slice.LastIndexOf('\n'), slice.LastIndexOf(' '));

        if (boundary > 0)
        {
            slice = trimStart ? slice[boundary..] : slice[..boundary];
        }

        var result = slice.Trim();

        // The character estimate is rough; shrink until it genuinely fits.
        var guard = 0;
        while (counter.Count(result) > maxTokens && result.Length > 0 && guard++ < 32)
        {
            var shrink = Math.Max(1, result.Length / 10);
            result = (trimStart ? result[shrink..] : result[..^shrink]).Trim();
        }

        return result;
    }

    private record PlacedEntry(LorebookEntry Entry, string Content, int Tokens, bool Trimmed);

    private record Unit(string Name, string Kind, string Text, string? Note = null);
}
