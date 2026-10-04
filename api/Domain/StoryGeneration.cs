using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<StoryGenerationStatus>))]
public enum StoryGenerationStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>
/// A single attempt at producing text, with the exact context that was sent so
/// any output can be explained (or reproduced) later.
/// </summary>
public class StoryGeneration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    public string Transport { get; set; } = string.Empty;

    public StoryGenerationStatus Status { get; set; } = StoryGenerationStatus.Queued;

    /// <summary>Optional steer appended to the context for this generation.</summary>
    public string? Instruction { get; set; }

    public string? Output { get; set; }

    public string? Error { get; set; }

    public int PromptTokens { get; set; }

    public int OutputTokens { get; set; }

    public string ParametersJson { get; set; } = "{}";

    public string? ContextTraceJson { get; set; }

    public Guid? ResultBlockId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public long DurationMs { get; set; }
}
