using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ComicStudio.Api.Text;

namespace ComicStudio.Api.Comics;

public record StructuredRequest
{
    /// <summary>
    /// Pre-resolved model. Required rather than looked up here: curator passes
    /// run concurrently and a DbContext is not thread-safe, so resolving inside
    /// this call would fail exactly when more than one scene is in flight.
    /// </summary>
    public required Domain.AiModel Model { get; init; }

    public required string System { get; init; }

    public required string User { get; init; }

    public required string Token { get; init; }

    public int MaxTokens { get; init; } = 3000;

    public double Temperature { get; init; } = 0.6;
}

public record StructuredResult<T>(T? Value, string Raw, string? Error, int Attempts)
{
    public bool Succeeded => Value is not null;

    /// <summary>
    /// True when the model ran out of output before the JSON closed. Distinct
    /// from a malformed answer: the remedy is a smaller request, never a repair
    /// round-trip, because resending the same oversized prompt truncates again.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>Answer characters. Reasoning is not part of the answer.</summary>
    public int ContentChars { get; init; }

    /// <summary>
    /// Reasoning characters. GLM-4.6 streams its chain of thought inline and it
    /// spends from the same output budget as the answer, so a short answer with
    /// a lot of reasoning is a budget problem, not a model problem.
    /// </summary>
    public int ReasoningChars { get; init; }
}

/// <summary>
/// Flat counters for one recorded stage. They end up in CurationRun.ResultJson
/// so a run can be diagnosed from the database without watching a terminal.
/// </summary>
public readonly record struct StructuredStats(
    int ContentChars,
    int ReasoningChars,
    int Attempts,
    bool Truncated
)
{
    public static StructuredStats From<T>(StructuredResult<T> result)
        where T : class =>
        new(result.ContentChars, result.ReasoningChars, result.Attempts, result.Truncated);

    public static StructuredStats operator +(StructuredStats left, StructuredStats right) =>
        new(
            left.ContentChars + right.ContentChars,
            left.ReasoningChars + right.ReasoningChars,
            left.Attempts + right.Attempts,
            left.Truncated || right.Truncated
        );
}

/// <summary>The outcome of one parse attempt, and which way it failed.</summary>
public readonly record struct ParseOutcome<T>(T? Value, string? Error, bool Truncated)
    where T : class;

/// <summary>
/// Asks a text model for JSON and then survives the answer.
///
/// NovelAI's OpenAI-compatible endpoint has no confirmed structured-output mode,
/// so this leans on a strict instruction and a tolerant extractor (code fences,
/// prose around the JSON, trailing commas). A complete but invalid answer gets
/// one repair round-trip that feeds the parser error back to the model; a
/// truncated answer gets no repair because the request itself was too big and
/// the caller has to ask for less.
/// </summary>
public class StructuredTextService(
    ITextClientResolver clients,
    CuratorThrottle throttle,
    ILogger<StructuredTextService> logger
)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// A single completion, split into answer and reasoning. Reasoning spends
    /// from the same output budget, so a mysteriously short answer is often a
    /// model that thought too much before writing.
    /// </summary>
    private readonly record struct RawCompletion(
        string Text,
        int ContentChars,
        int ReasoningChars
    );

    public async Task<StructuredResult<T>> CompleteAsync<T>(
        StructuredRequest request,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        var raw = await CompleteRawAsync(request, cancellationToken);
        var first = Parse<T>(raw.Text);

        if (first.Value is not null)
        {
            return new StructuredResult<T>(first.Value, raw.Text, null, 1)
            {
                ContentChars = raw.ContentChars,
                ReasoningChars = raw.ReasoningChars,
            };
        }

        // Truncation is not a formatting mistake: the answer outgrew the output
        // budget. Resending it — even colder — truncates again, so surface it
        // and let the caller shrink the request instead.
        if (first.Truncated)
        {
            logger.LogWarning(
                "Curator output truncated after {ContentChars} answer chars "
                    + "(+{ReasoningChars} reasoning); the request must shrink.",
                raw.ContentChars,
                raw.ReasoningChars
            );

            return new StructuredResult<T>(null, raw.Text, first.Error, 1)
            {
                Truncated = true,
                ContentChars = raw.ContentChars,
                ReasoningChars = raw.ReasoningChars,
            };
        }

        logger.LogInformation("Curator output was not valid JSON; asking for a repair.");

        var repairRequest = request with
        {
            User =
                request.User
                + "\n\n---\nYour previous answer could not be parsed as JSON.\n"
                + $"Parser error: {first.Error}\n"
                + "Reply with the corrected JSON only. No prose, no code fences.",
            Temperature = 0.2,
        };

        var repaired = await CompleteRawAsync(repairRequest, cancellationToken);
        var second = Parse<T>(repaired.Text);

        return second.Value is not null
            ? new StructuredResult<T>(second.Value, repaired.Text, null, 2)
            {
                ContentChars = repaired.ContentChars,
                ReasoningChars = repaired.ReasoningChars,
            }
            : new StructuredResult<T>(null, repaired.Text, second.Error ?? first.Error, 2)
            {
                Truncated = second.Truncated,
                ContentChars = repaired.ContentChars,
                ReasoningChars = repaired.ReasoningChars,
            };
    }

    private async Task<RawCompletion> CompleteRawAsync(
        StructuredRequest request,
        CancellationToken cancellationToken
    )
    {
        var model = request.Model;
        var client = clients.Resolve(model);

        var generation = new TextGenerationRequest
        {
            ModelId = model.ProviderModelId,
            Messages =
            [
                new TextMessage("system", request.System),
                new TextMessage("user", request.User),
            ],
            Prompt = request.User,
            Sampler = new Domain.TextSamplerSettings
            {
                Temperature = request.Temperature,
                MaxTokens = Math.Clamp(request.MaxTokens, 256, 4096),
                TopP = 0.95,
            },
            Token = request.Token,
        };

        var contentChars = 0;
        var reasoningChars = 0;
        var stopwatch = Stopwatch.StartNew();

        // NovelAI rate limits text generation per account, so calls are gated
        // and transient 429s are retried rather than failing the whole plan.
        var text = await throttle.RunAsync(
            async () =>
            {
                var attempt = new StringBuilder();
                var reasoning = 0;

                await foreach (var delta in client.StreamAsync(generation, cancellationToken))
                {
                    if (delta.Kind == TextDeltaKind.Content)
                    {
                        attempt.Append(delta.Text);
                    }
                    else
                    {
                        // Reasoning is not part of the answer and must never
                        // reach the JSON parser, but it is measured: it spends
                        // the same output budget the answer needs. Stays zero
                        // until the transport labels reasoning separately.
                        reasoning += delta.Text.Length;
                    }
                }

                // A retried attempt replaces the counters; a failed stream's
                // partial text must not be stitched onto the next attempt.
                contentChars = attempt.Length;
                reasoningChars = reasoning;
                return attempt.ToString();
            },
            cancellationToken
        );

        // Text generation dominates curation time, so every call is measured.
        logger.LogInformation(
            "Curator call on {Model}: {ContentChars} answer chars "
                + "(+{ReasoningChars} reasoning) in {Seconds:0.0}s.",
            model.Id,
            contentChars,
            reasoningChars,
            stopwatch.Elapsed.TotalSeconds
        );

        return new RawCompletion(text, contentChars, reasoningChars);
    }

    /// <summary>
    /// Parses one attempt. The tolerant extractor is kept exactly as it was:
    /// code fences, prose around the JSON and trailing commas all still parse.
    /// A null candidate means there was no balanced JSON value to try: either
    /// no JSON at all, or the output stopped before it closed.
    /// </summary>
    public static ParseOutcome<T> Parse<T>(string raw)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParseOutcome<T>(null, "the response was empty", false);
        }

        var candidate = ExtractJson(raw);

        if (candidate is null)
        {
            return LooksTruncated(raw)
                ? new ParseOutcome<T>(
                    null,
                    "the response was cut off before its JSON closed",
                    true
                )
                : new ParseOutcome<T>(
                    null,
                    "no JSON object or array was found in the response",
                    false
                );
        }

        try
        {
            return new ParseOutcome<T>(JsonSerializer.Deserialize<T>(candidate, Options), null, false);
        }
        catch (JsonException exception)
        {
            return new ParseOutcome<T>(null, exception.Message, false);
        }
    }

    /// <summary>
    /// True when the output stopped inside an open string, an open brace or
    /// bracket, or an unclosed code fence. Complete-but-wrong JSON is a
    /// different failure with a different remedy, so it is not counted here.
    /// </summary>
    public static bool LooksTruncated(string raw)
    {
        var text = raw.Trim();

        if (text.Length == 0)
        {
            return false;
        }

        var start = IndexOfJsonStart(text);

        if (start < 0)
        {
            // No JSON at all: only an unclosed fence suggests it was on its way.
            return HasUnclosedFence(text);
        }

        var open = text[start];
        var close = open == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == open)
            {
                depth++;
            }
            else if (character == close)
            {
                depth--;

                if (depth == 0)
                {
                    // A balanced value was found before the output ended.
                    return false;
                }
            }
        }

        // Reached the end with the value still open.
        return depth > 0 || inString;
    }

    private static bool HasUnclosedFence(string text)
    {
        var fences = 0;

        for (
            var index = text.IndexOf("```", StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf("```", index + 3, StringComparison.Ordinal)
        )
        {
            fences++;
        }

        return fences % 2 == 1;
    }

    public static string? ExtractJson(string raw)
    {
        var text = raw.Trim();

        var fence = Regex.Match(text, @"```(?:json)?\s*(?<body>[\s\S]*?)```", RegexOptions.IgnoreCase);
        if (fence.Success)
        {
            text = fence.Groups["body"].Value.Trim();
        }

        var start = IndexOfJsonStart(text);
        if (start < 0)
        {
            return null;
        }

        var open = text[start];
        var close = open == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == open)
            {
                depth++;
            }
            else if (character == close)
            {
                depth--;
                if (depth == 0)
                {
                    var slice = text[start..(index + 1)];
                    return RemoveTrailingCommas(slice);
                }
            }
        }

        return null;
    }

    private static int IndexOfJsonStart(string text)
    {
        var objectStart = text.IndexOf('{');
        var arrayStart = text.IndexOf('[');

        if (objectStart < 0)
        {
            return arrayStart;
        }

        if (arrayStart < 0)
        {
            return objectStart;
        }

        return Math.Min(objectStart, arrayStart);
    }

    /// <summary>Models like trailing commas; JSON does not.</summary>
    private static string RemoveTrailingCommas(string json) =>
        Regex.Replace(json, @",\s*(?=[}\]])", string.Empty);
}
