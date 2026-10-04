using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ComicStudio.Api.Auth;
using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Security;
using ComicStudio.Api.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Diagnostics;

/// <summary>What one short probe request through a transport produced.</summary>
/// <param name="Transport">Transport that ran the probe.</param>
/// <param name="Succeeded">True when answer text (not just reasoning) arrived.</param>
/// <param name="Message">Failure or explanation, when there is one.</param>
/// <param name="DurationMs">Wall-clock time for the probe.</param>
/// <param name="RawSample">
/// The first characters of the stream in arrival order, exactly as received.
/// Reasoning markers show up here even though the transport separates them.
/// </param>
/// <param name="ReasoningCharacters">Total characters emitted as reasoning.</param>
/// <param name="ContentCharacters">Total characters emitted as answer content.</param>
/// <param name="ReasoningSample">First characters attributed to reasoning, if any.</param>
/// <param name="ContentSample">First characters attributed to content, if any.</param>
public record TransportProbeDto(
    string Transport,
    bool Succeeded,
    string? Message,
    long DurationMs,
    string? RawSample = null,
    int ReasoningCharacters = 0,
    int ContentCharacters = 0,
    string? ReasoningSample = null,
    string? ContentSample = null
);

/// <summary>
/// Whether NovelAI accepted a requested output-token ceiling.
///
/// Acceptance does NOT mean the value was honoured: the endpoint may accept a
/// value and silently clamp it server-side. Only a rejection is conclusive.
/// </summary>
public record TokenCeilingProbeDto(
    string Transport,
    int Requested,
    bool Accepted,
    string? Message,
    long DurationMs
);

/// <summary>
/// Whether a server-side thinking-control strategy was accepted, and whether
/// reasoning actually disappeared from the answer.
/// </summary>
/// <param name="Name">Strategy identifier, shaped like the request field it sets.</param>
/// <param name="Accepted">True when the request was not rejected by the API.</param>
/// <param name="SuppressesReasoning">
/// True when the request succeeded, content arrived and no reasoning did. Null
/// when the probe could not tell (rejected, timed out, or no text arrived).
/// </param>
public record ThinkingStrategyProbeDto(
    string Name,
    bool Accepted,
    bool? SuppressesReasoning,
    int ReasoningCharacters,
    int ContentCharacters,
    string? Message,
    long DurationMs
);

public record TextsDiagnosticsDto(
    bool TokenUsable,
    string? Message,
    IReadOnlyList<string> RemoteModelIds,
    IReadOnlyList<TransportProbeDto> Probes,
    IReadOnlyList<TokenCeilingProbeDto>? TokenCeilings = null,
    IReadOnlyList<ThinkingStrategyProbeDto>? ThinkingStrategies = null
);

public record AccountProbeDto(string Host, int Status, string? Raw);

public record AccountDiagnosticsDto(
    IReadOnlyList<AccountProbeDto> Hosts,
    NovelAi.NovelAiAccount? Interpreted
);

/// <summary>
/// Answers "will text generation actually work with my token?" — the models
/// endpoint proves the token, and the probes exercise each configured transport
/// for real. Text generation is free on a subscription, so probes cost nothing.
/// </summary>
[ApiController]
[Authorize]
[Route("api/novelai/diagnostics")]
public class DiagnosticsController(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ITextClientResolver clients,
    SecretProtector protector,
    ILogger<DiagnosticsController> logger
) : ControllerBase
{
    private const string ProbePrompt = "Write one short sentence about a garden.";
    private const int ProbeMaxTokens = 2048;
    private const int ProbeTimeoutSeconds = 45;
    private const int ProbeCharacterCap = 6000;
    private const int RawSampleCharacters = 600;
    private const int SampleCharacters = 200;

    /// <summary>
    /// One server-side way to ask a reasoning model not to think. The list is
    /// data rather than code so a new strategy its one line in
    /// <see cref="ThinkingControlStrategies"/>.
    /// </summary>
    /// <param name="ExtraParameters">Fields merged into the transport payload.</param>
    /// <param name="UserSuffix">Text appended to the user message.</param>
    /// <param name="AssistantPrefill">
    /// Content for a final assistant message. A pre-closed think block keeps the
    /// continuation in content mode on servers that honour it.
    /// </param>
    private sealed record ThinkingControlStrategy(
        string Name,
        JsonObject? ExtraParameters,
        string? UserSuffix,
        string? AssistantPrefill
    );

    private static readonly IReadOnlyList<ThinkingControlStrategy> ThinkingControlStrategies =
    [
        new(
            "chat_template_kwargs.enable_thinking=false",
            new JsonObject
            {
                ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
            },
            UserSuffix: null,
            AssistantPrefill: null
        ),
        new(
            "thinking.type=disabled",
            new JsonObject { ["thinking"] = new JsonObject { ["type"] = "disabled" } },
            UserSuffix: null,
            AssistantPrefill: null
        ),
        new(
            "prompt /nothink + assistant prefill",
            ExtraParameters: null,
            UserSuffix: " /nothink",
            AssistantPrefill: "\n"
        ),
    ];

    private static readonly int[] TokenCeilingCandidates = [2048, 4096, 8192];

    /// <summary>
    /// Shows NovelAI's raw subscription response from every known host, next to
    /// our reading of it. Field names here have moved before, and a mis-parsed
    /// tier silently looks like "no subscription", so the raw body is the only
    /// way to tell the difference.
    /// </summary>
    [HttpGet("account")]
    public async Task<ActionResult<AccountDiagnosticsDto>> Account(
        CancellationToken cancellationToken = default
    )
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(
            u => u.Id == User.GetUserId(),
            cancellationToken
        );
        if (user is null)
        {
            return NotFound();
        }

        var token = protector.Unprotect(user.Id, user.NovelAiTokenCipher);
        if (string.IsNullOrEmpty(token))
        {
            return BadRequest(new ProblemDetails { Detail = "NovelAI is not connected." });
        }

        var http = httpClientFactory.CreateClient(ComicStudio.Api.Text.TextHttpClient.Name);
        var probes = new List<AccountProbeDto>();

        foreach (
            var host in new[]
            {
                NovelAi.NovelAiEndpoints.Image,
                NovelAi.NovelAiEndpoints.Primary,
            }
        )
        {
            probes.Add(await ProbeAccountAsync(http, host, token, cancellationToken));
        }

        // Re-interpret through the same code path the connect flow uses.
        var probe = await db
            .Users.Where(u => u.Id == user.Id)
            .Select(u => new
            {
                u.NovelAiTier,
                u.NovelAiSubscriptionActive,
                u.AnlasBalance,
            })
            .FirstAsync(cancellationToken);

        return Ok(
            new AccountDiagnosticsDto(
                probes,
                new NovelAi.NovelAiAccount(
                    probe.NovelAiTier,
                    probe.NovelAiSubscriptionActive,
                    null,
                    probe.AnlasBalance,
                    null
                )
            )
        );
    }

    private static async Task<AccountProbeDto> ProbeAccountAsync(
        HttpClient http,
        string host,
        string token,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(NovelAi.NovelAiEndpoints.Normalize(host) + "user/subscription")
            );
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                token
            );

            using var response = await http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return new AccountProbeDto(host, (int)response.StatusCode, body);
        }
        catch (Exception exception)
        {
            return new AccountProbeDto(host, 0, exception.Message);
        }
    }

    [HttpGet("text")]
    public async Task<ActionResult<TextsDiagnosticsDto>> Text(
        [FromQuery] bool probe = false,
        CancellationToken cancellationToken = default
    )
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(
            u => u.Id == User.GetUserId(),
            cancellationToken
        );
        if (user is null)
        {
            return NotFound();
        }

        var token = protector.Unprotect(user.Id, user.NovelAiTokenCipher);
        if (string.IsNullOrEmpty(token))
        {
            return Ok(
                new TextsDiagnosticsDto(
                    false,
                    "NovelAI is not connected. Add a persistent token in Settings.",
                    [],
                    [],
                    [],
                    []
                )
            );
        }

        var (modelIds, message, usable) = await FetchModelsAsync(token, cancellationToken);

        var probes = new List<TransportProbeDto>();
        var ceilings = new List<TokenCeilingProbeDto>();
        var strategies = new List<ThinkingStrategyProbeDto>();

        if (probe && usable)
        {
            var transports = await db
                .AiModels.AsNoTracking()
                .Where(m => m.Kind == AiModelKind.Text && !m.IsDeprecated)
                .ToListAsync(cancellationToken);

            AiModel? chatModel = null;

            foreach (var group in transports.GroupBy(m => m.Capabilities.Transport ?? "unknown"))
            {
                var model = group.First();
                if (
                    chatModel is null
                    && string.Equals(
                        model.Capabilities.Transport,
                        ChatTextClient.TransportName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    chatModel = model;
                }

                probes.Add(await ProbeAsync(model, token, cancellationToken));
            }

            // The ceiling probe covers every transport; the chat transport is
            // preferred because that is where GLM-4.6 and max_tokens live.
            var ceilingModel = chatModel ?? transports.FirstOrDefault();
            if (ceilingModel is not null)
            {
                ceilings.AddRange(
                    await ProbeTokenCeilingsAsync(ceilingModel, token, cancellationToken)
                );
            }

            // Thinking controls are chat payload shapes, so only run them when a
            // chat model exists.
            if (chatModel is not null)
            {
                strategies.AddRange(
                    await ProbeThinkingStrategiesAsync(chatModel, token, cancellationToken)
                );
            }
        }

        return Ok(new TextsDiagnosticsDto(usable, message, modelIds, probes, ceilings, strategies));
    }

    private async Task<(IReadOnlyList<string> Ids, string? Message, bool Usable)> FetchModelsAsync(
        string token,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var http = httpClientFactory.CreateClient(TextHttpClient.Name);
            using var request = new HttpRequestMessage(HttpMethod.Get, "oa/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var detail = ExtractMessage(body);

                return (
                    [],
                    string.IsNullOrEmpty(detail)
                        ? $"NovelAI returned {(int)response.StatusCode} for the model list."
                        : $"NovelAI returned {(int)response.StatusCode}: {detail}",
                    false
                );
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken
            );

            var ids = new List<string>();
            if (
                document.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
            )
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id) && id.GetString() is { } value)
                    {
                        ids.Add(value);
                    }
                }
            }

            return (ids, null, true);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not reach NovelAI's text API.");
            return ([], "Could not reach NovelAI's text API.", false);
        }
    }

    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (
                document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
            )
            {
                return message.GetString()?.Trim() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through.
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }

    /// <summary>
    /// Streams one short sentence and reports the raw bytes as well as the
    /// reasoning/content split, so a human can confirm whether inline
    /// "&lt;think&gt;" markers are really being sent.
    /// </summary>
    private async Task<TransportProbeDto> ProbeAsync(
        AiModel model,
        string token,
        CancellationToken cancellationToken
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var transport = model.Capabilities.Transport ?? "unknown";

        try
        {
            var client = clients.Resolve(model);
            var request = new TextGenerationRequest
            {
                ModelId = model.ProviderModelId,
                Messages = [new TextMessage("user", ProbePrompt)],
                Prompt = ProbePrompt,
                Sampler = new TextSamplerSettings { MaxTokens = ProbeMaxTokens, Temperature = 0.8 },
                Token = token,
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));

            var raw = new StringBuilder();
            var reasoning = new StringBuilder();
            var content = new StringBuilder();

            await foreach (var delta in client.StreamAsync(request, timeout.Token))
            {
                raw.Append(delta.Text);
                if (delta.Kind == TextDeltaKind.Reasoning)
                {
                    reasoning.Append(delta.Text);
                }
                else
                {
                    content.Append(delta.Text);
                }

                if (raw.Length >= ProbeCharacterCap)
                {
                    break;
                }
            }

            var message = content.Length > 0
                ? null
                : reasoning.Length > 0
                    ? "Only reasoning arrived; the answer never started."
                    : "The model returned no text.";

            return new TransportProbeDto(
                client.Transport,
                content.Length > 0,
                message,
                stopwatch.ElapsedMilliseconds,
                Sample(raw, RawSampleCharacters),
                reasoning.Length,
                content.Length,
                Sample(reasoning, SampleCharacters),
                Sample(content, SampleCharacters)
            );
        }
        catch (NovelAiTextException exception)
        {
            return new TransportProbeDto(
                transport,
                false,
                exception.Message,
                stopwatch.ElapsedMilliseconds
            );
        }
        catch (OperationCanceledException)
        {
            return new TransportProbeDto(
                transport,
                false,
                "The probe timed out.",
                stopwatch.ElapsedMilliseconds
            );
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Transport probe failed.");
            return new TransportProbeDto(
                transport,
                false,
                exception.Message,
                stopwatch.ElapsedMilliseconds
            );
        }
    }

    /// <summary>
    /// Sends one tiny request per candidate ceiling. Only a rejection is
    /// conclusive; an accepted value may still be clamped server-side.
    /// </summary>
    private async Task<List<TokenCeilingProbeDto>> ProbeTokenCeilingsAsync(
        AiModel model,
        string token,
        CancellationToken cancellationToken
    )
    {
        var results = new List<TokenCeilingProbeDto>();
        var transport = model.Capabilities.Transport ?? "unknown";

        foreach (var candidate in TokenCeilingCandidates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var client = clients.Resolve(model);
                var request = new TextGenerationRequest
                {
                    ModelId = model.ProviderModelId,
                    Messages = [new TextMessage("user", "Say hi.")],
                    Prompt = "Say hi.",
                    Sampler = new TextSamplerSettings
                    {
                        MaxTokens = candidate,
                        MinTokens = 1,
                        Temperature = 0.8,
                    },
                    Token = token,
                    // The configured clamp would rewrite 8192 before it is sent,
                    // which would make this probe lie.
                    BypassOutputTokenClamp = true,
                };

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                timeout.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));

                var produced = false;
                await foreach (var delta in client.StreamAsync(request, timeout.Token))
                {
                    produced = !string.IsNullOrEmpty(delta.Text);
                    if (produced)
                    {
                        break;
                    }
                }

                results.Add(
                    new TokenCeilingProbeDto(
                        client.Transport,
                        candidate,
                        true,
                        produced
                            ? "Accepted; the endpoint may still clamp it server-side."
                            : "Accepted, but no token arrived.",
                        stopwatch.ElapsedMilliseconds
                    )
                );
            }
            catch (NovelAiTextException exception)
            {
                results.Add(
                    new TokenCeilingProbeDto(
                        transport,
                        candidate,
                        false,
                        exception.Message,
                        stopwatch.ElapsedMilliseconds
                    )
                );
            }
            catch (OperationCanceledException)
            {
                results.Add(
                    new TokenCeilingProbeDto(
                        transport,
                        candidate,
                        false,
                        "The probe timed out before the request completed.",
                        stopwatch.ElapsedMilliseconds
                    )
                );
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Token ceiling probe failed.");
                results.Add(
                    new TokenCeilingProbeDto(
                        transport,
                        candidate,
                        false,
                        exception.Message,
                        stopwatch.ElapsedMilliseconds
                    )
                );
            }
        }

        return results;
    }

    /// <summary>
    /// Runs each thinking-control strategy on its own request and reports
    /// whether the API accepted it and whether reasoning disappeared. Rejections
    /// are results too, so nothing here throws.
    /// </summary>
    private async Task<List<ThinkingStrategyProbeDto>> ProbeThinkingStrategiesAsync(
        AiModel model,
        string token,
        CancellationToken cancellationToken
    )
    {
        var results = new List<ThinkingStrategyProbeDto>();

        foreach (var strategy in ThinkingControlStrategies)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var client = clients.Resolve(model);
                var prompt = ProbePrompt + (strategy.UserSuffix ?? string.Empty);
                var messages = new List<TextMessage> { new("user", prompt) };

                if (strategy.AssistantPrefill is not null)
                {
                    messages.Add(new TextMessage("assistant", strategy.AssistantPrefill));
                }

                var request = new TextGenerationRequest
                {
                    ModelId = model.ProviderModelId,
                    Messages = messages,
                    Prompt = prompt,
                    Sampler = new TextSamplerSettings
                    {
                        MaxTokens = ProbeMaxTokens,
                        MinTokens = 1,
                        Temperature = 0.8,
                    },
                    Token = token,
                    ExtraParameters = strategy.ExtraParameters,
                };

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                timeout.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));

                var reasoningCharacters = 0;
                var contentCharacters = 0;

                await foreach (var delta in client.StreamAsync(request, timeout.Token))
                {
                    if (delta.Kind == TextDeltaKind.Reasoning)
                    {
                        reasoningCharacters += delta.Text.Length;
                    }
                    else
                    {
                        contentCharacters += delta.Text.Length;
                    }

                    // Enough to tell whether thinking starts; no need to wait
                    // out a full generation.
                    if (contentCharacters >= SampleCharacters || reasoningCharacters >= 2000)
                    {
                        break;
                    }
                }

                bool? suppresses = contentCharacters > 0 ? reasoningCharacters == 0 : null;
                var message = suppresses switch
                {
                    true => "Accepted and no reasoning observed.",
                    false => "Accepted, but the model still reasoned.",
                    null => "Accepted, but no answer text arrived.",
                };

                results.Add(
                    new ThinkingStrategyProbeDto(
                        strategy.Name,
                        true,
                        suppresses,
                        reasoningCharacters,
                        contentCharacters,
                        message,
                        stopwatch.ElapsedMilliseconds
                    )
                );
            }
            catch (NovelAiTextException exception)
            {
                results.Add(
                    new ThinkingStrategyProbeDto(
                        strategy.Name,
                        false,
                        null,
                        0,
                        0,
                        exception.Message,
                        stopwatch.ElapsedMilliseconds
                    )
                );
            }
            catch (OperationCanceledException)
            {
                results.Add(
                    new ThinkingStrategyProbeDto(
                        strategy.Name,
                        false,
                        null,
                        0,
                        0,
                        "The probe timed out.",
                        stopwatch.ElapsedMilliseconds
                    )
                );
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Thinking control probe failed.");
                results.Add(
                    new ThinkingStrategyProbeDto(
                        strategy.Name,
                        false,
                        null,
                        0,
                        0,
                        exception.Message,
                        stopwatch.ElapsedMilliseconds
                    )
                );
            }
        }

        return results;
    }

    private static string? Sample(StringBuilder builder, int length)
    {
        if (builder.Length == 0)
        {
            return null;
        }

        return builder.Length <= length ? builder.ToString() : builder.ToString(0, length);
    }
}
