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
/// NovelAI's OpenAI-compatible chat completions endpoint
/// (POST {base}/oa/v1/chat/completions), used by the GLM-4.6 family.
///
/// GLM-4.6 streams its chain of thought inline in the same content field as the
/// answer, so every chunk is passed through <see cref="ThinkingSplitter"/> and
/// reasoning is emitted as <see cref="TextDeltaKind.Reasoning"/>.
/// </summary>
public class ChatTextClient(
    HttpClient http,
    IConfiguration configuration,
    ILogger<ChatTextClient> logger
) : ITextClient
{
    public const string TransportName = "openai-chat";

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

        using var message = new HttpRequestMessage(HttpMethod.Post, "oa/v1/chat/completions")
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

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var splitter = new ThinkingSplitter();
        var done = false;

        while (!done && !reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data == "[DONE]")
            {
                done = true;
                continue;
            }

            string? delta = null;
            try
            {
                using var document = JsonDocument.Parse(data);
                var root = document.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (
                        choice.TryGetProperty("delta", out var deltaNode)
                        && deltaNode.TryGetProperty("content", out var content)
                    )
                    {
                        delta = content.GetString();
                    }
                    else if (
                        choice.TryGetProperty("message", out var messageNode)
                        && messageNode.TryGetProperty("content", out var messageContent)
                    )
                    {
                        delta = messageContent.GetString();
                    }
                    else if (choice.TryGetProperty("text", out var text))
                    {
                        delta = text.GetString();
                    }
                }

                if (delta is null && root.TryGetProperty("error", out var error))
                {
                    throw new NovelAiTextException(ReadError(error));
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipped an unparsable chat stream event.");
                continue;
            }

            if (!string.IsNullOrEmpty(delta))
            {
                foreach (var parsed in splitter.Push(delta))
                {
                    yield return parsed;
                }
            }
        }

        // End of stream, whether "[DONE]" arrived or the connection just closed:
        // emit whatever the splitter still held, so an unterminated think block
        // is classified as reasoning and nothing is lost.
        foreach (var parsed in splitter.Push(null, flush: true))
        {
            yield return parsed;
        }
    }

    private static string BuildPayload(TextGenerationRequest request, int maxOutputTokens)
    {
        var sampler = request.Sampler;
        var messages = new JsonArray();

        foreach (var message in request.Messages)
        {
            messages.Add(
                new JsonObject { ["role"] = message.Role, ["content"] = message.Content }
            );
        }

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

        parameters["model"] = request.ModelId;
        parameters["messages"] = messages;
        parameters["max_tokens"] = request.BypassOutputTokenClamp
            ? Math.Max(1, sampler.MaxTokens)
            : Math.Clamp(sampler.MaxTokens, 1, maxOutputTokens);
        parameters["temperature"] = sampler.Temperature;
        parameters["top_p"] = sampler.TopP;
        parameters["stream"] = true;

        if (sampler.TopK > 0)
        {
            parameters["top_k"] = sampler.TopK;
        }

        if (sampler.MinP > 0)
        {
            parameters["min_p"] = sampler.MinP;
        }

        if (sampler.PresencePenalty != 0)
        {
            parameters["presence_penalty"] = sampler.PresencePenalty;
        }

        if (sampler.FrequencyPenalty != 0)
        {
            parameters["frequency_penalty"] = sampler.FrequencyPenalty;
        }

        if (sampler.StopSequences.Count > 0)
        {
            parameters["stop"] = new JsonArray(
                sampler.StopSequences.Select(stop => (JsonNode)stop).ToArray()
            );
        }

        return parameters.ToJsonString();
    }

    private static string ReadError(JsonElement error) =>
        error.ValueKind switch
        {
            JsonValueKind.String => error.GetString() ?? "NovelAI returned an error.",
            JsonValueKind.Object when error.TryGetProperty("message", out var message) =>
                message.GetString() ?? "NovelAI returned an error.",
            _ => "NovelAI returned an error.",
        };

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
        if (status is HttpStatusCode.BadRequest)
        {
            // A 400 here is NovelAI asking for a trial reCAPTCHA, which cannot be
            // produced outside their website.
            var detail = NovelAiErrors.ExtractMessage(body);
            if (NovelAiErrors.IsRecaptchaRequired(body))
            {
                return NovelAiErrors.SubscriptionRequired;
            }

            return $"NovelAI rejected the request. {detail}".TrimEnd();
        }

        return NovelAiErrors.Describe(status, NovelAiErrors.ExtractMessage(body));
    }

    private static string Truncate(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "…";
    }
}
