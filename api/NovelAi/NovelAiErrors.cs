using System.Net;
using System.Text.Json;

namespace ComicStudio.Api.NovelAi;

/// <summary>
/// Turns NovelAI's status codes and error bodies into something a person can act
/// on. Every client funnels through here so the same failure reads the same way
/// wherever it surfaces.
/// </summary>
public static class NovelAiErrors
{
    /// <summary>
    /// NovelAI gates generation behind a paid subscription. The free trial is
    /// web-only: proving a human is present relies on a reCAPTCHA token minted by
    /// their site, which a third-party app cannot produce.
    /// </summary>
    public const string SubscriptionRequired =
        "NovelAI needs an active subscription to use the API. The free trial only works on "
        + "the NovelAI website, because its reCAPTCHA cannot be solved from another app. "
        + "Subscribe to Tablet, Scroll or Opus, then refresh your account in Settings.";

    public static string NotConnected =>
        "NovelAI is not connected. Add a persistent token in Settings.";

    public static string TokenRejected(string? detail) =>
        string.IsNullOrEmpty(detail)
            ? "NovelAI rejected that token."
            : $"NovelAI rejected that token: {detail}";

    /// <summary>True when NovelAI is asking for a trial reCAPTCHA.</summary>
    public static bool IsRecaptchaRequired(string? body) =>
        body is not null && body.Contains("recaptcha", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when NovelAI says the account has no usable subscription.</summary>
    public static bool IsSubscriptionRequired(int statusCode, string? body) =>
        statusCode == (int)HttpStatusCode.PaymentRequired || IsRecaptchaRequired(body);

    public static string Describe(HttpStatusCode status, string? detail)
    {
        if (IsSubscriptionRequired((int)status, detail))
        {
            return SubscriptionRequired;
        }

        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail.Trim()}";

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                TokenRejected(detail?.Trim()),
            HttpStatusCode.TooManyRequests => "NovelAI is rate limiting requests. Try again shortly.",
            HttpStatusCode.BadRequest => $"NovelAI rejected the request.{suffix}",
            HttpStatusCode.InternalServerError => $"NovelAI failed to process the request.{suffix}",
            _ => $"NovelAI returned {(int)status}.{suffix}",
        };
    }

    /// <summary>Pulls the <c>message</c> field out of NovelAI's error body.</summary>
    public static string? ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var name in new[] { "message", "detail", "error", "title" })
            {
                if (
                    document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text
                )
                {
                    return text.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to the raw body.
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }
}
