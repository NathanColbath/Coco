using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Hubs;
using ComicStudio.Api.Security;
using ComicStudio.Api.Storytelling;
using ComicStudio.Api.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ComicStudio.Api.Features.Stories;

/// <summary>
/// Runs text generations off the request thread and streams tokens to the owner
/// over SignalR. A durable queue arrives with the image pipeline; text
/// generations are short-lived and single-flight per user.
/// </summary>
public class StoryGenerationRunner(
    IServiceScopeFactory scopeFactory,
    IHubContext<GenerationHub> hub,
    ILogger<StoryGenerationRunner> logger
)
{
    private const int FlushIntervalMs = 90;
    private const int FlushCharacters = 48;

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> active = new();

    public void Start(Guid generationId) => _ = Task.Run(() => RunAsync(generationId));

    public bool Cancel(Guid generationId)
    {
        if (active.TryGetValue(generationId, out var source))
        {
            source.Cancel();
            return true;
        }

        return false;
    }

    private async Task RunAsync(Guid generationId)
    {
        using var cancellation = new CancellationTokenSource();
        active[generationId] = cancellation;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var scope = scopeFactory.CreateScope();
            var provider = scope.ServiceProvider;
            var db = provider.GetRequiredService<AppDbContext>();
            var assembler = provider.GetRequiredService<ContextAssembler>();
            var counter = provider.GetRequiredService<ITokenCounter>();
            var protector = provider.GetRequiredService<SecretProtector>();
            var clients = provider.GetRequiredService<ITextClientResolver>();
            var models = provider.GetRequiredService<TextModelResolver>();

            var generation = await db.StoryGenerations.FirstOrDefaultAsync(
                g => g.Id == generationId,
                cancellation.Token
            );
            if (generation is null)
            {
                return;
            }

            var story = await db.Stories.FirstOrDefaultAsync(
                s => s.Id == generation.StoryId,
                cancellation.Token
            );
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.Id == generation.UserId,
                cancellation.Token
            );

            if (story is null || user is null)
            {
                await FailAndNotifyAsync(
                    db,
                    generation,
                    "The story or account no longer exists.",
                    stopwatch
                );
                return;
            }

            var token = protector.Unprotect(user.Id, user.NovelAiTokenCipher);
            if (string.IsNullOrEmpty(token))
            {
                await FailAndNotifyAsync(
                    db,
                    generation,
                    "NovelAI is not connected. Add a persistent token in Settings.",
                    stopwatch
                );
                return;
            }

            var model = await models.ResolveAsync(
                story.TextModelId,
                user.PreferredTextModelId,
                cancellation.Token
            );
            var client = clients.Resolve(model);

            var blocks = await db
                .StoryBlocks.Where(b => b.StoryId == story.Id)
                .ToListAsync(cancellation.Token);
            var lorebook = await db
                .LorebookEntries.Where(e => e.StoryId == story.Id)
                .ToListAsync(cancellation.Token);

            var sampler = DeserializeSampler(generation.ParametersJson) ?? story.Sampler;

            var context = assembler.Assemble(
                new ContextAssemblyRequest
                {
                    Story = story,
                    ActivePath = StoryPath.Build(blocks, story.CursorBlockId),
                    ModelId = model.ProviderModelId,
                    Transport = client.Transport,
                    ContextTokens = TextModelResolver.ContextTokensFor(model, user.NovelAiTier),
                    RolloverTokens = model.RolloverTokens ?? 0,
                    OutputTokens = sampler.MaxTokens,
                    Instruction = generation.Instruction,
                    LorebookEntries = lorebook,
                }
            );

            generation.Status = StoryGenerationStatus.Running;
            generation.StartedAt = DateTimeOffset.UtcNow;
            generation.Transport = client.Transport;
            generation.PromptTokens = context.PromptTokens;
            generation.ContextTraceJson = JsonSerializer.Serialize(context.Trace);
            await db.SaveChangesAsync(cancellation.Token);

            await hub
                .Clients.Group(GenerationHub.GroupFor(generation.UserId))
                .SendAsync(
                    GenerationEvents.Started,
                    new { generationId, model = model.DisplayName },
                    cancellation.Token
                );

            var request = new TextGenerationRequest
            {
                ModelId = model.ProviderModelId,
                Messages = context.Messages.Select(m => new TextMessage(m.Role, m.Content)).ToArray(),
                Prompt = context.Prompt,
                Sampler = sampler,
                Token = token,
            };

            var output = new StringBuilder();
            var buffer = new StringBuilder();
            var lastFlush = Stopwatch.StartNew();
            var reasoningCharacters = 0;

            await foreach (
                var delta in client.StreamAsync(request, cancellation.Token).WithCancellation(cancellation.Token)
            )
            {
                // Hybrid models think out loud on the same stream; the prose the
                // user asked for is only the content half.
                if (delta.Kind == TextDeltaKind.Reasoning)
                {
                    reasoningCharacters += delta.Text.Length;
                    continue;
                }

                output.Append(delta.Text);
                buffer.Append(delta.Text);

                if (
                    buffer.Length >= FlushCharacters
                    || lastFlush.ElapsedMilliseconds >= FlushIntervalMs
                )
                {
                    await PublishTokens(
                        generation.UserId,
                        generationId,
                        buffer.ToString(),
                        cancellation.Token
                    );
                    buffer.Clear();
                    lastFlush.Restart();
                }
            }

            if (buffer.Length > 0)
            {
                await PublishTokens(
                    generation.UserId,
                    generationId,
                    buffer.ToString(),
                    cancellation.Token
                );
            }

            if (reasoningCharacters > 0)
            {
                logger.LogInformation(
                    "Generation {GenerationId} separated {ReasoningCharacters} characters of reasoning from the answer.",
                    generationId,
                    reasoningCharacters
                );
            }

            var text = output.ToString().Trim();
            var now = DateTimeOffset.UtcNow;

            if (text.Length == 0)
            {
                await FailAndNotifyAsync(
                    db,
                    generation,
                    "NovelAI returned an empty completion.",
                    stopwatch
                );
                return;
            }

            var block = new StoryBlock
            {
                StoryId = story.Id,
                ParentId = story.CursorBlockId,
                Kind = StoryBlockKind.Prose,
                Origin = StoryBlockOrigin.Ai,
                Text = text,
                TokenCount = counter.Count(text),
                GenerationId = generation.Id,
            };

            db.StoryBlocks.Add(block);
            story.CursorBlockId = block.Id;
            story.UpdatedAt = now;

            generation.Status = StoryGenerationStatus.Succeeded;
            generation.Output = text;
            generation.OutputTokens = block.TokenCount;
            generation.ResultBlockId = block.Id;
            generation.FinishedAt = now;
            generation.DurationMs = stopwatch.ElapsedMilliseconds;

            await db.SaveChangesAsync(CancellationToken.None);

            await hub
                .Clients.Group(GenerationHub.GroupFor(generation.UserId))
                .SendAsync(
                    GenerationEvents.Completed,
                    new
                    {
                        generationId,
                        block = StoryBlockDto.From(block),
                        durationMs = generation.DurationMs,
                    },
                    CancellationToken.None
                );
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await TryMarkAsync(generationId, StoryGenerationStatus.Cancelled, "Cancelled.", stopwatch);
            await NotifyFailureAsync(generationId, "Cancelled.");
        }
        catch (NovelAiTextException exception)
        {
            logger.LogWarning("Generation {GenerationId} failed: {Message}", generationId, exception.Message);
            await TryMarkAsync(generationId, StoryGenerationStatus.Failed, exception.Message, stopwatch);
            await NotifyFailureAsync(generationId, exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Generation {GenerationId} failed unexpectedly.", generationId);
            var message = "Text generation failed unexpectedly. Check the API logs for details.";
            await TryMarkAsync(generationId, StoryGenerationStatus.Failed, message, stopwatch);
            await NotifyFailureAsync(generationId, message);
        }
        finally
        {
            active.TryRemove(generationId, out _);
        }
    }

    private Task PublishTokens(string userId, Guid generationId, string text, CancellationToken ct) =>
        hub
            .Clients.Group(GenerationHub.GroupFor(userId))
            .SendAsync(GenerationEvents.Token, new { generationId, text }, ct);

    /// <summary>
    /// Records an immediate failure and tells the client, so a request that never
    /// reaches NovelAI does not leave the UI waiting for tokens that never come.
    /// </summary>
    private async Task FailAndNotifyAsync(
        AppDbContext db,
        StoryGeneration generation,
        string message,
        Stopwatch stopwatch
    )
    {
        await FailAsync(db, generation, message, stopwatch, cancelled: false);
        await PublishFailureAsync(generation.UserId, generation.Id, message);
    }

    private Task PublishFailureAsync(string userId, Guid generationId, string message) =>
        hub
            .Clients.Group(GenerationHub.GroupFor(userId))
            .SendAsync(
                GenerationEvents.Failed,
                new { generationId, message },
                CancellationToken.None
            );

    /// <summary>Looks the owner up after a failure, when the scope is already gone.</summary>
    private async Task NotifyFailureAsync(Guid generationId, string message)
    {
        var userId = await TryGetUserIdAsync(generationId);
        if (userId is null)
        {
            return;
        }

        await PublishFailureAsync(userId, generationId, message);
    }

    private async Task<string?> TryGetUserIdAsync(Guid generationId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db
                .StoryGenerations.AsNoTracking()
                .Where(g => g.Id == generationId)
                .Select(g => g.UserId)
                .FirstOrDefaultAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task TryMarkAsync(
        Guid generationId,
        StoryGenerationStatus status,
        string? error,
        Stopwatch stopwatch
    )
    {
        try
        {
            await MarkAsync(generationId, status, error, stopwatch);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not record the outcome of generation {GenerationId}.", generationId);
        }
    }

    private async Task MarkAsync(
        Guid generationId,
        StoryGenerationStatus status,
        string? error,
        Stopwatch stopwatch
    )
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generation = await db.StoryGenerations.FirstOrDefaultAsync(g => g.Id == generationId);
        if (generation is null)
        {
            return;
        }

        generation.Status = status;
        generation.Error = error;
        generation.FinishedAt = DateTimeOffset.UtcNow;
        generation.DurationMs = stopwatch.ElapsedMilliseconds;
        await db.SaveChangesAsync();
    }

    private static async Task FailAsync(
        AppDbContext db,
        StoryGeneration generation,
        string message,
        Stopwatch stopwatch,
        bool cancelled
    )
    {
        generation.Status = cancelled
            ? StoryGenerationStatus.Cancelled
            : StoryGenerationStatus.Failed;
        generation.Error = message;
        generation.FinishedAt = DateTimeOffset.UtcNow;
        generation.DurationMs = stopwatch.ElapsedMilliseconds;
        await db.SaveChangesAsync();
    }

    private static TextSamplerSettings? DeserializeSampler(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TextSamplerSettings>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
