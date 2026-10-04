namespace ComicStudio.Api.Storytelling;

public interface ITokenCounter
{
    int Count(string? text);
}

/// <summary>
/// Approximate token counting.
///
/// NovelAI's models use different tokenizers (Nerdstash for Kayra/Clio,
/// Llama 3 BPE for Erato, a GLM vocabulary for GLM-4.6/Xialong). Bundling all
/// three would mean shipping large vocabulary files, so this counter estimates
/// deliberately on the high side: over-counting trims a little extra context,
/// under-counting would silently truncate the model's window.
/// Replace with an exact tokenizer per model when one is available.
/// </summary>
public class HeuristicTokenCounter : ITokenCounter
{
    /// <summary>Average characters per token for English prose.</summary>
    private const double CharsPerToken = 4.0;

    /// <summary>Safety factor applied to every estimate.</summary>
    private const double SafetyFactor = 1.05;

    public int Count(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var tokens = 0.0;
        var wordLength = 0;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || IsPunctuation(character))
            {
                tokens += FlushWord(wordLength);
                wordLength = 0;

                if (IsPunctuation(character))
                {
                    tokens += 1;
                }
            }
            else
            {
                wordLength++;
            }
        }

        tokens += FlushWord(wordLength);
        return (int)Math.Ceiling(tokens * SafetyFactor);
    }

    private static double FlushWord(int length) =>
        length == 0 ? 0 : Math.Max(1, Math.Ceiling(length / CharsPerToken));

    private static bool IsPunctuation(char character) =>
        char.IsPunctuation(character) || char.IsSymbol(character);
}
