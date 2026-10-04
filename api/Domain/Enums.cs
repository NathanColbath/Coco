using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

/// <summary>NovelAI subscription tiers, ordered by capability.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NovelAiTier>))]
public enum NovelAiTier
{
    Unknown = -1,
    Paper = 0,
    Tablet = 1,
    Scroll = 2,
    Opus = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter<AiModelKind>))]
public enum AiModelKind
{
    Text = 0,
    Image = 1,
    Voice = 2,
}
