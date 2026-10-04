using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ComicStudio.Api.NovelAi;
using Microsoft.Extensions.Configuration;

namespace ComicStudio.Api.Text;

/// <summary>
/// NovelAI's legacy completion endpoint (POST {base}/ai/generate-stream) used by
/// Kayra, Erato and Clio. NovelAI is migrating subscribers off these models, so
/// the event parser is deliberately tolerant of several payload shapes rather
/// than assuming one. These models are not hybrid reasoners, so every chunk is
/// content; the inline-thinking splitter is only needed on the chat transport.
/// </summary>
public class LegacyCompletionTextClient(
    HttpClient http,
    IConfiguration configuration,
    ILogger<LegacyCompletionTextClient> logger
) : ITextClient
{
    public const string TransportName = "legacy-completion";

    public string Transport => TransportName;

    public async IAsyncEnumerable<TextDelta> StreamAsync(
        TextGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var payload = BuildPayload(
            request,
            TextTokenLimits.ResolveMaxOutputTokens(configuration)
        );

        using var message = new HttpRequestMessage(HttpMethod.Post, "ai/generate-stream")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.Token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await http.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadAsync(response, cancellationToken);
            throw new NovelAiTextException(
                Describe(response.StatusCode, body),
                (int)response.StatusCode
            );
        }

        var emitted = new StringBuilder();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var data = line.StartsWith("data:", StringComparison.Ordinal)
                ? line[5..].Trim()
                : line.Trim();

            if (data.Length == 0 || data == "[DONE]")
            {
                if (data == "[DONE]")
                {
                    yield break;
                }
                continue;
            }

            string? delta = null;
            try
            {
                using var document = JsonDocument.Parse(data);
                delta = ExtractDelta(document.RootElement, emitted);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipped an unparsable completion stream event.");
                continue;
            }

            if (!string.IsNullOrEmpty(delta))
            {
                emitted.Append(delta);
                yield return TextDelta.Content(delta);
            }
        }
    }

    /// <summary>Reads whichever field this generation of the API happens to use.</summary>
    private static string? ExtractDelta(JsonElement root, StringBuilder emitted)
    {
        foreach (var name in new[] { "token", "text", "delta" })
        {
            if (
                root.TryGetProperty(name, out var node)
                && node.ValueKind == JsonValueKind.String
            )
            {
                return node.GetString();
            }
        }

        if (
            root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
        )
        {
            var choice = choices[0];
            if (
                choice.TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("content", out var content)
            )
            {
                return content.GetString();
            }

            if (choice.TryGetProperty("text", out var text))
            {
                return text.GetString();
            }
        }

        // Some responses carry the whole generation; emit only the unseen tail.
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.String)
        {
            var full = output.GetString() ?? string.Empty;
            var seen = emitted.ToString();
            if (full.Length > seen.Length && full.StartsWith(seen, StringComparison.Ordinal))
            {
                return full[seen.Length..];
            }
        }

        if (root.TryGetProperty("error", out var error))
        {
            throw new NovelAiTextException(
                error.ValueKind == JsonValueKind.String
                    ? error.GetString() ?? "NovelAI returned an error."
                    : "NovelAI returned an error."
            );
        }

        return null;
    }

    private static string BuildPayload(TextGenerationRequest request, int maxOutputTokens)
    {
        var sampler = request.Sampler;

        var parameters = new JsonObject();

        // Extra fields first, so a probe can add knobs but cannot replace the
        // transport-critical fields set below.
        if (request.ExtraParameters is { Count: > 0 })
        {
            foreach (var pair in request.ExtraParameters)
            {
                parameters[pair.Key] = pair.Value?.DeepClone();
            }
        }

        parameters["use_string"] = true;
        parameters["temperature"] = sampler.Temperature;
        parameters["max_length"] = request.BypassOutputTokenClamp
            ? Math.Max(1, sampler.MaxTokens)
            : Math.Clamp(sampler.MaxTokens, 1, maxOutputTokens);
        parameters["min_length"] = Math.Clamp(sampler.MinTokens, 1, maxOutputTokens);

        AddStandardParameters(parameters, sampler);

        var payload = new JsonObject
        {
            ["input"] = request.Prompt,
            ["model"] = request.ModelId,
            ["parameters"] = parameters,
        };

        return payload.ToJsonString();
    }

    private static void AddStandardParameters(JsonObject parameters, Domain.TextSamplerSettings sampler)
    {
        parameters["top_k"] = sampler.TopK;
        parameters["top_p"] = sampler.TopP;
        parameters["top_a"] = sampler.TopA;
        parameters["typical_p"] = sampler.TypicalP;
        parameters["tail_free_sampling"] = sampler.TailFreeSampling;
        parameters["min_p"] = sampler.MinP;
        parameters["repetition_penalty"] = sampler.RepetitionPenalty;
        parameters["repetition_penalty_range"] = sampler.RepetitionPenaltyRange;
        parameters["repetition_penalty_slope"] = sampler.RepetitionPenaltySlope;
        parameters["repetition_penalty_frequency"] = sampler.FrequencyPenalty;
        parameters["repetition_penalty_presence"] = sampler.PresencePenalty;
        parameters["phrase_rep_pen"] = sampler.PhraseRepetitionPenalty;
        parameters["bad_words_ids"] = new JsonArray();
        parameters["stop_sequences"] = new JsonArray();
        parameters["order"] = new JsonArray(
            Enumerable.Range(0, 11).Select(index => (JsonNode)index).ToArray()
        );
    }

    private static async Task<string> SafeReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string Describe(HttpStatusCode status, string body)
    {
        // NovelAI answers 400 with a reCAPTCHA demand when the account has no
        // subscription, which is a different problem from a bad request.
        if (NovelAiErrors.IsRecaptchaRequired(body))
        {
            return NovelAiErrors.SubscriptionRequired;
        }

        return NovelAiErrors.Describe(status, NovelAiErrors.ExtractMessage(body));
    }
}
