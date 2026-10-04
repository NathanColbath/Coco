namespace ComicStudio.Api.Storytelling;

/// <summary>One labelled piece of the assembled context, in final order.</summary>
public record ContextSegment(
    string Name,
    string Kind,
    string Text,
    int Tokens,
    string? Note = null
);

/// <summary>
/// Everything that went into a request and why. Persisted with each generation
/// so the context viewer can explain exactly what the model saw.
/// </summary>
public record ContextTrace(
    string ModelId,
    string Transport,
    int ContextWindowTokens,
    int OutputReserveTokens,
    int AvailableTokens,
    int UsedTokens,
    int ReservedTokens,
    bool Trimmed,
    ContextSegment[] Segments,
    string[] Warnings
)
{
    public int RemainingTokens => Math.Max(0, AvailableTokens - UsedTokens);
}

public record ContextMessage(string Role, string Content);

public record AssembledContext(
    string Prompt,
    IReadOnlyList<ContextMessage> Messages,
    ContextTrace Trace,
    int PromptTokens
);
