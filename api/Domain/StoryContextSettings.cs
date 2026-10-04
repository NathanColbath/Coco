namespace ComicStudio.Api.Domain;

/// <summary>
/// How the story context is assembled. Stored as a JSON column so it can grow
/// with NovelAI's context options without a migration each time.
/// </summary>
public class StoryContextSettings
{
    /// <summary>Overrides the transport's default system prompt when set.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Always-present context, including the ATTG line.</summary>
    public string? Memory { get; set; }

    /// <summary>Short, strongly-weighted steer placed near the end of context.</summary>
    public string? AuthorNote { get; set; }

    /// <summary>Messages from the end of context to insert the author's note at.</summary>
    public int AuthorNoteDepth { get; set; } = 3;

    /// <summary>Tokens held back from the story text for lorebook entries.</summary>
    public int ReservedTokens { get; set; }

    /// <summary>How many trailing tokens of story text are searched for lorebook keys.</summary>
    public int LorebookScanTokens { get; set; } = 1200;
}

/// <summary>
/// Sampling parameters. Only the subset relevant to the chosen transport is
/// sent; the rest are ignored rather than rejected.
/// </summary>
public class TextSamplerSettings
{
    public double Temperature { get; set; } = 0.85;

    public int MaxTokens { get; set; } = 160;

    public int MinTokens { get; set; } = 1;

    public double TopP { get; set; } = 0.95;

    public int TopK { get; set; }

    public double TopA { get; set; }

    public double TypicalP { get; set; }

    public double TailFreeSampling { get; set; }

    public double MinP { get; set; }

    public double RepetitionPenalty { get; set; } = 1.0;

    public int RepetitionPenaltyRange { get; set; }

    public double RepetitionPenaltySlope { get; set; }

    public double PresencePenalty { get; set; }

    public double FrequencyPenalty { get; set; }

    public string PhraseRepetitionPenalty { get; set; } = "off";

    public List<string> StopSequences { get; set; } = [];
}
