using System.Text.Json.Nodes;
using ComicStudio.Api.Domain;
using Microsoft.Extensions.Configuration;

namespace ComicStudio.Api.Text;

public record TextMessage(string Role, string Content);

/// <summary>
/// The single authority for how many output tokens a text transport may ask
/// for. Both <see cref="ChatTextClient"/> and
/// <see cref="LegacyCompletionTextClient"/> clamp through here, so they can no
/// longer disagree with each other or with the curator.
/// </summary>
public static class TextTokenLimits
{
    /// <summary>Configuration key that overrides <see cref="DefaultMaxOutputTokens"/>.</summary>
    public const string MaxOutputTokensConfigKey = "Curator:MaxOutputTokens";

    /// <summary>
    /// Ceiling applied to a requested max_tokens value when nothing is
    /// configured.
    ///
    /// The real ceiling is UNVERIFIED against the live NovelAI API. This value
    /// is the largest candidate the diagnostics text probe tests, and the probe
    /// reports which of 2048/4096/8192 the endpoint actually accepts. Only move
    /// this constant or the configuration after the probe has answered.
    /// </summary>
    public const int DefaultMaxOutputTokens = 4096;

    /// <summary>Reads the configured ceiling; never below one token.</summary>
    public static int ResolveMaxOutputTokens(IConfiguration configuration) =>
        Math.Max(1, configuration.GetValue(MaxOutputTokensConfigKey, DefaultMaxOutputTokens));
}

public class TextGenerationRequest
{
    public required string ModelId { get; init; }

    /// <summary>Message list for chat-style transports.</summary>
    public required IReadOnlyList<TextMessage> Messages { get; init; }

    /// <summary>Flat prompt for completion-style transports.</summary>
    public required string Prompt { get; init; }

    public required TextSamplerSettings Sampler { get; init; }

    /// <summary>Decrypted NovelAI persistent token. Never logged.</summary>
    public required string Token { get; init; }

    /// <summary>
    /// Extra fields merged into the transport payload before its standard
    /// fields are set. Used by the diagnostics probe to test server-side
    /// thinking controls; ordinary generation leaves it null.
    /// </summary>
    public JsonObject? ExtraParameters { get; init; }

    /// <summary>
    /// Diagnostics only: send <see cref="TextSamplerSettings.MaxTokens"/> without
    /// applying the configured clamp, so the token-ceiling probe can learn what
    /// the endpoint itself accepts. Never set this for user requests; an
    /// unverified oversized value can be rejected.
    /// </summary>
    public bool BypassOutputTokenClamp { get; init; }
}

/// <summary>Raised when NovelAI rejects a request; the message is user-facing.</summary>
public class NovelAiTextException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

/// <summary>
/// What a streamed chunk is: the answer, or the model thinking out loud.
///
/// GLM-4.6 is a hybrid reasoner and NovelAI streams its chain-of-thought inline,
/// so the two must be kept apart. Reasoning is not part of the answer, is paid
/// for out of the same token budget, and must never reach the JSON parser.
/// </summary>
public enum TextDeltaKind
{
    Content,
    Reasoning,
}

/// <summary>One streamed chunk of text.</summary>
public readonly record struct TextDelta(TextDeltaKind Kind, string Text)
{
    public static TextDelta Content(string text) => new(TextDeltaKind.Content, text);

    public static TextDelta Reasoning(string text) => new(TextDeltaKind.Reasoning, text);
}

/// <summary>A streaming text transport.</summary>
public interface ITextClient
{
    /// <summary>Matches the "transport" capability in the model registry.</summary>
    string Transport { get; }

    IAsyncEnumerable<TextDelta> StreamAsync(
        TextGenerationRequest request,
        CancellationToken cancellationToken
    );
}
