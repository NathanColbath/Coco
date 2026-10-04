using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ComicStudio.Api.Domain;

namespace ComicStudio.Api.NovelAi;

/// <summary>
/// Thin, defensive wrapper over NovelAI's REST API. Responses are parsed from
/// JsonDocument rather than rigid DTOs because the documented shapes lag the
/// live service; unknown fields are ignored, known ones are mapped explicitly.
/// </summary>
public class NovelAiClient(
    HttpClient http,
    IConfiguration configuration,
    ILogger<NovelAiClient> logger
) : INovelAiClient
{
    /// <summary>
    /// Account lookups run against the image host by default, because the
    /// documentation steers third-party API users away from the primary API.
    /// </summary>
    private readonly string accountBaseUrl =
        configuration["NovelAi:AccountBaseUrl"] ?? NovelAiEndpoints.Image;
    public async Task<NovelAiProbeResult> GetAccountAsync(
        string token,
        CancellationToken cancellationToken = default
    )
    {
        // NovelAI answers 400 "Invalid Authorization header content." when the
        // header carries whitespace, which is what a token copied across a line
        // break looks like. Persistent tokens never contain whitespace, so the
        // paste artifact is removed rather than rejected.
        var normalized = new string(
            token.Where(character => !char.IsWhiteSpace(character)).ToArray()
        );

        if (normalized.Length == 0)
        {
            return new NovelAiProbeResult(
                NovelAiProbeStatus.InvalidResponse,
                Message: "No token was provided."
            );
        }

        if (normalized.Length != token.Length)
        {
            logger.LogInformation(
                "Removed whitespace from a pasted NovelAI token ({Removed} character(s)).",
                token.Length - normalized.Length
            );
        }

        // The image host is the documented home for this lookup; the primary API
        // is tried only if the image host cannot answer, so a future move in
        // either direction keeps working.
        NovelAiProbeResult? lastFailure = null;

        foreach (var host in CandidateHosts())
        {
            var result = await ProbeAccountAsync(host, normalized, cancellationToken);

            // A definitive answer ends the search: success or rejection. Only a
            // missing endpoint, a server error or an unreachable host suggests
            // the other host is worth trying — retrying a rejected token or a
            // malformed request elsewhere would just produce a second failure.
            var definitive =
                result.Status is NovelAiProbeStatus.Ok or NovelAiProbeStatus.Unauthorized
                || (
                    result.StatusCode is { } code
                    && code is >= 400 and < 500
                    && code != 404
                );

            if (definitive)
            {
                return result;
            }

            lastFailure = result;

            logger.LogInformation(
                "Account lookup against {Host} failed ({Status}); trying the next host.",
                host,
                result.Status
            );
        }

        return lastFailure
            ?? new NovelAiProbeResult(
                NovelAiProbeStatus.Unreachable,
                Message: "Could not reach NovelAI. Check your connection and try again."
            );
    }

    private IEnumerable<string> CandidateHosts()
    {
        yield return NovelAiEndpoints.Normalize(accountBaseUrl);

        var fallback = NovelAiEndpoints.Normalize(accountBaseUrl)
                .Equals(NovelAiEndpoints.Primary + "/", StringComparison.OrdinalIgnoreCase)
            ? NovelAiEndpoints.Image
            : NovelAiEndpoints.Primary;

        if (!NovelAiEndpoints.Normalize(fallback).Equals(
                NovelAiEndpoints.Normalize(accountBaseUrl),
                StringComparison.OrdinalIgnoreCase
            ))
        {
            yield return NovelAiEndpoints.Normalize(fallback);
        }
    }

    private async Task<NovelAiProbeResult> ProbeAccountAsync(
        string baseAddress,
        string token,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseAddress + "user/subscription")
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "NovelAI is unreachable.");
            return new NovelAiProbeResult(
                NovelAiProbeStatus.Unreachable,
                Message: "Could not reach NovelAI. Check your connection and try again."
            );
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NovelAiProbeResult(
                NovelAiProbeStatus.Unreachable,
                Message: "The request to NovelAI timed out."
            );
        }

        using (response)
        {
            var body = await ReadBodyAsync(response, cancellationToken);
            var detail = ExtractMessage(body);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new NovelAiProbeResult(
                    NovelAiProbeStatus.Unauthorized,
                    Message: string.IsNullOrEmpty(detail)
                        ? "NovelAI rejected that token."
                        : $"NovelAI rejected that token: {detail}",
                    StatusCode: (int)response.StatusCode
                );
            }

            if (!response.IsSuccessStatusCode)
            {
                return new NovelAiProbeResult(
                    NovelAiProbeStatus.InvalidResponse,
                    Message: NovelAiErrors.Describe(response.StatusCode, detail),
                    StatusCode: (int)response.StatusCode
                );
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                return new NovelAiProbeResult(
                    NovelAiProbeStatus.Ok,
                    ParseAccount(document.RootElement)
                );
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Could not parse the NovelAI subscription response.");
                return new NovelAiProbeResult(
                    NovelAiProbeStatus.InvalidResponse,
                    Message: "NovelAI returned an unexpected response."
                );
            }
        }
    }

    private static async Task<string> ReadBodyAsync(
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

    /// <summary>
    /// Pulls NovelAI's own explanation out of the response: its errors carry a
    /// <c>message</c> field, e.g. "User not found." or "Invalid Authorization
    /// header content." Discarding it was making failures undiagnosable.
    /// </summary>
    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            foreach (var name in new[] { "message", "detail", "error", "title" })
            {
                if (
                    root.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text
                )
                {
                    return text.Trim();
                }
            }

            return string.Empty;
        }
        catch (JsonException)
        {
            var trimmed = body.Trim();
            return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
        }
    }

    private static NovelAiAccount ParseAccount(JsonElement root)
    {
        var tier = ParseTier(root);

        // `active` is not reliable on its own: a subscription in its grace period
        // reports inactive while still granting full access, and some responses
        // omit the field entirely.
        var gracePeriod = ReadBool(root, "isGracePeriod", "is_grace_period") ?? false;
        var known = tier is not (NovelAiTier.Paper or NovelAiTier.Unknown);

        var active = ReadBool(root, "active") ?? known;
        if (gracePeriod && known)
        {
            active = true;
        }

        var expiresAt = ReadUnixTime(root, "expiresAt", "expires_at");

        int? anlas = ReadInt(root, "anlas", "anlasBalance");

        if (anlas is null && root.TryGetProperty("perks", out var perks))
        {
            anlas = ReadInt(perks, "anlas", "anlasBalance");
        }

        int? trainingSteps = null;
        if (
            root.TryGetProperty("trainingStepsLeft", out var steps)
            && steps.ValueKind == JsonValueKind.Object
        )
        {
            var fixedSteps = ReadInt(steps, "fixedTrainingStepsLeft", "fixed_training_steps_left") ?? 0;
            var purchased = ReadInt(steps, "purchasedTrainingSteps", "purchased_training_steps") ?? 0;
            trainingSteps = fixedSteps + purchased;
            anlas ??= trainingSteps;
        }

        return new NovelAiAccount(tier, active, expiresAt, anlas, trainingSteps);
    }

    private static NovelAiTier ParseTier(JsonElement root)
    {
        foreach (var name in new[] { "tier_name", "tierName", "tier" })
        {
            if (
                root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && Enum.TryParse<NovelAiTier>(value.GetString(), ignoreCase: true, out var named)
                && named != NovelAiTier.Unknown
            )
            {
                return named;
            }
        }

        var numeric = ReadInt(root, "tier");
        return numeric switch
        {
            0 => NovelAiTier.Paper,
            1 => NovelAiTier.Tablet,
            2 => NovelAiTier.Scroll,
            >= 3 => NovelAiTier.Opus,
            _ => NovelAiTier.Unknown,
        };
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static bool? ReadBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return value.GetBoolean();
                }
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.Number when value.TryGetInt32(out var number):
                    return number;
                case JsonValueKind.String when int.TryParse(value.GetString(), out var parsed):
                    return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadUnixTime(JsonElement element, params string[] names)
    {
        var raw = ReadInt(element, names);
        if (raw is null or <= 0)
        {
            return null;
        }

        // NovelAI has returned both seconds and milliseconds over time.
        return raw > 100_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(raw.Value)
            : DateTimeOffset.FromUnixTimeSeconds(raw.Value);
    }
}
