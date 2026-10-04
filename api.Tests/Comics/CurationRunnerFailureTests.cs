using System.Text.Json;
using ComicStudio.Api.Comics;
using ComicStudio.Api.Data;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Features.Comics;
using ComicStudio.Api.Hubs;
using ComicStudio.Api.Security;
using ComicStudio.Api.Storytelling;
using ComicStudio.Api.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// Curation used to log "Scene N returned no pages; skipping it" and move on,
/// so a failed pass produced zero pages, two full generations of cost and no
/// record. This drives the real runner against an in-memory SQLite database to
/// prove a failed batch now leaves a failed CurationRun behind, keeps the pages
/// that did script, and ends the project in an error state.
/// </summary>
public class CurationRunnerFailureTests : IDisposable
{
    private const string Discovery =
        """{"characters":[{"name":"Aiko"}],"environments":[{"name":"Rooftop"}]}""";

    private const string ScenePlan =
        """{"scenes":[{"index":0,"title":"The Rooftop","summary":"Aiko waits out the storm.","environment":"Rooftop","pageCount":4}]}""";

    private const string FirstBatch =
        """{"pages":[{"index":1,"prompt":"Aiko climbs the fire escape.","panels":[],"bubbles":[],"continuity":"LOCATION rooftop"},{"index":2,"prompt":"She looks down at the empty street.","panels":[],"bubbles":[],"continuity":"LOCATION rooftop"}]}""";

    private const string Truncated =
        """{"pages":[{"index":3,"prompt":"Aiko turns as the neon sign flick""";

    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"comic-studio-runner-{Guid.NewGuid():N}.db"
    );

    public void Dispose()
    {
        try
        {
            File.Delete(databasePath);
        }
        catch (IOException)
        {
            // The provider still holds the file on some platforms; temp cleanup
            // is best-effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: a locked temp file must not fail the test run.
        }
    }

    [Fact]
    public async Task A_failed_batch_leaves_a_failed_run_and_an_error_state_behind()
    {        // One two-page batch scripts, the next truncates even after shrinking,
        // and the remaining single page truncates too.
        var client = new ScriptedTextClient(
            Discovery,
            ScenePlan,
            FirstBatch,
            Truncated,
            Truncated,
            Truncated
        );
        var hub = new RecordingHub();

        using var provider = BuildProvider(client, hub);
        var projectId = await SeedAsync(provider);

        provider.GetRequiredService<CurationRunner>().Start(projectId);

        var failedCount = await WaitForFailedRunsAsync(provider, projectId, expected: 3);
        Assert.True(
            failedCount >= 3,
            $"expected at least 3 failed runs (two batches plus the scripting summary), saw {failedCount}"
        );

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var failed = await db
            .CurationRuns.Where(run =>
                run.ProjectId == projectId && run.Status == CurationStatus.Failed
            )
            .ToListAsync();

        // Every failure carries its error; none of them vanish.
        Assert.All(failed, run => Assert.False(string.IsNullOrWhiteSpace(run.Error)));

        // The batch failures name the scene and the exact pages that are gone.
        // The size-2 attempt truncated, so the recorded failure is the shrunk
        // one-page retry: pages 3-3 and 4-4.
        Assert.Contains(
            failed,
            run => run.Error!.Contains("pages 3-3") && run.Error.Contains("The Rooftop")
        );

        // The diagnostics live in ResultJson, not only in the log.
        var batchFailure = Assert.Single(failed, run => run.Error!.Contains("pages 3-3"));
        Assert.Contains("\"pagesRequested\":1", batchFailure.ResultJson);
        Assert.Contains("\"pagesReturned\":0", batchFailure.ResultJson);
        Assert.Contains("\"truncated\":true", batchFailure.ResultJson);
        Assert.Contains("\"attempts\":1", batchFailure.ResultJson);

        // The summary row knows how many pages are missing, and how many calls
        // the shrink retries cost.
        var summary = Assert.Single(failed, run => run.ResultJson!.Contains("\"missingPages\":2"));
        Assert.Contains("\"pagesRequested\":6", summary.ResultJson);
        Assert.Contains("\"pagesReturned\":2", summary.ResultJson);
        Assert.Contains("\"batches\":4", summary.ResultJson);

        // The first batch is kept, not thrown away with the failures.
        var pages = await db
            .ComicPages.Where(page => page.ProjectId == projectId)
            .ToListAsync();
        Assert.Equal(2, pages.Count);

        // The run must not report success with holes in it.
        var project = await db.ComicProjects.FirstAsync(item => item.Id == projectId);
        Assert.Equal(ComicStatus.Draft, project.Status);

        lock (hub.Messages)
        {
            Assert.Contains(
                hub.Messages,
                message =>
                    message.Contains("Plan incomplete")
                    && message.Contains("2 missing")
                    && message.Contains("failed batch")
            );
        }
    }

    [Fact]
    public async Task An_unchanged_story_skips_discovery_on_the_second_run()
    {
        const string TwoPagePlan =
            """{"scenes":[{"index":0,"title":"The Rooftop","summary":"Aiko waits.","environment":"Rooftop","pageCount":2}]}""";
        const string Seam = """{"fixes":[]}""";

        // Two passes are scripted: the first, then a re-run that must not ask
        // the model to rediscover an unchanged story.
        var client = new ScriptedTextClient(
            Discovery,
            TwoPagePlan,
            FirstBatch,
            Seam,
            TwoPagePlan,
            FirstBatch,
            Seam
        );
        var hub = new RecordingHub();

        using var provider = BuildProvider(client, hub);
        var projectId = await SeedAsync(provider);
        var runner = provider.GetRequiredService<CurationRunner>();

        runner.Start(projectId);
        Assert.True(
            await WaitForMessagesAsync(hub, message => message.Contains("Plan ready"), 1),
            "the first curation run did not finish"
        );

        runner.Start(projectId);
        Assert.True(
            await WaitForMessagesAsync(
                hub,
                message => message.Contains("Story unchanged"),
                1
            ),
            "the second run did not report that discovery was skipped"
        );
        Assert.True(
            await WaitForMessagesAsync(hub, message => message.Contains("Plan ready"), 2),
            "the second curation run did not finish"
        );

        // Only the first pass pays for discovery; the second reuses the bible.
        var discoveryCalls = client.Requests.Count(request =>
            request.Prompt.Contains("list both the cast and the places")
        );
        Assert.Equal(1, discoveryCalls);

        lock (hub.Messages)
        {
            Assert.Contains(
                hub.Messages,
                message => message.Contains("Story unchanged")
            );
        }
    }

    private ServiceProvider BuildProvider(ScriptedTextClient client, RecordingHub hub)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Curator:MaxConcurrentTextRequests"] = "2",
                    ["Curator:RetryBaseDelayMs"] = "1",
                    // Pin the batch size so the split is deterministic whatever
                    // ceiling the transport ends up honouring.
                    ["Curator:EffectiveOutputTokens"] = "2048",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}")
        );
        services.AddSingleton<SecretProtector>();
        services.AddSingleton<ITokenCounter, HeuristicTokenCounter>();
        services.AddSingleton<ITextClientResolver>(new FixedTextResolver(client));
        services.AddSingleton<CuratorThrottle>();
        services.AddScoped<StructuredTextService>();
        services.AddScoped<Curator>();
        services.AddScoped<TextModelResolver>();
        services.AddSingleton<IHubContext<GenerationHub>>(hub);
        services.AddSingleton<CurationRunner>();

        return services.BuildServiceProvider();
    }

    private static async Task<Guid> SeedAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var protector = scope.ServiceProvider.GetRequiredService<SecretProtector>();
        var storyId = Guid.NewGuid();
        var blockId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        db.Users.Add(
            new AppUser
            {
                Id = "runner-test-user",
                UserName = "runner-test-user",
                NormalizedUserName = "RUNNER-TEST-USER",
                NovelAiTokenCipher = protector.Protect("runner-test-user", "pst-test"),
                NovelAiTier = NovelAiTier.Tablet,
            }
        );

        db.AiModels.Add(
            new AiModel
            {
                Id = "scripted-text",
                Kind = AiModelKind.Text,
                DisplayName = "Scripted",
                ProviderModelId = "scripted",
                ContextTokens = 8192,
                IsDefault = true,
            }
        );

        db.Stories.Add(
            new Story
            {
                Id = storyId,
                UserId = "runner-test-user",
                Title = "Aiko on the roof",
                CursorBlockId = blockId,
            }
        );

        db.StoryBlocks.Add(
            new StoryBlock
            {
                Id = blockId,
                StoryId = storyId,
                Kind = StoryBlockKind.Prose,
                Text = "Aiko climbs the fire escape while the rain hammers the roof.",
            }
        );

        db.ComicProjects.Add(
            new ComicProject
            {
                Id = projectId,
                UserId = "runner-test-user",
                StoryId = storyId,
                Title = "The Rooftop",
                Premise = "Aiko waits out the storm.",
                Status = ComicStatus.Draft,
            }
        );

        await db.SaveChangesAsync();
        return projectId;
    }

    private static async Task<int> WaitForFailedRunsAsync(
        ServiceProvider provider,
        Guid projectId,
        int expected
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var count = await db.CurationRuns.CountAsync(run =>
                    run.ProjectId == projectId && run.Status == CurationStatus.Failed
                );

                if (count >= expected)
                {
                    return count;
                }
            }

            await Task.Delay(50);
        }

        using var finalScope = provider.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await finalDb.CurationRuns.CountAsync(run =>
            run.ProjectId == projectId && run.Status == CurationStatus.Failed
        );
    }

    private static async Task<bool> WaitForMessagesAsync(
        RecordingHub hub,
        Func<string, bool> predicate,
        int expected
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            lock (hub.Messages)
            {
                if (hub.Messages.Count(predicate) >= expected)
                {
                    return true;
                }
            }

            await Task.Delay(50);
        }

        return false;
    }

    /// <summary>Captures the SignalR progress payloads the runner pushes.</summary>
    private sealed class RecordingHub : IHubContext<GenerationHub>
    {
        public List<string> Messages { get; } = [];

        public IHubClients Clients { get; }

        public IGroupManager Groups { get; } = new NoopGroups();

        public RecordingHub() => Clients = new RecordingClients(this);

        private sealed class RecordingClients(RecordingHub owner) : IHubClients
        {
            public IClientProxy All => new RecordingProxy(owner);

            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
                new RecordingProxy(owner);

            public IClientProxy Client(string connectionId) => new RecordingProxy(owner);

            public IClientProxy Clients(IReadOnlyList<string> connectionIds) =>
                new RecordingProxy(owner);

            public IClientProxy Group(string groupName) => new RecordingProxy(owner);

            public IClientProxy GroupExcept(
                string groupName,
                IReadOnlyList<string> excludedConnectionIds
            ) => new RecordingProxy(owner);

            public IClientProxy Groups(IReadOnlyList<string> groupNames) =>
                new RecordingProxy(owner);

            public IClientProxy User(string userId) => new RecordingProxy(owner);

            public IClientProxy Users(IReadOnlyList<string> userIds) => new RecordingProxy(owner);
        }

        private sealed class RecordingProxy(RecordingHub owner) : IClientProxy
        {
            public Task SendCoreAsync(
                string method,
                object?[] args,
                CancellationToken cancellationToken = default
            )
            {
                var payload = args.Length > 0 ? JsonSerializer.Serialize(args[0]) : string.Empty;

                lock (owner.Messages)
                {
                    owner.Messages.Add(payload);
                }

                return Task.CompletedTask;
            }
        }

        private sealed class NoopGroups : IGroupManager
        {
            public Task AddToGroupAsync(
                string connectionId,
                string groupName,
                CancellationToken cancellationToken = default
            ) => Task.CompletedTask;

            public Task RemoveFromGroupAsync(
                string connectionId,
                string groupName,
                CancellationToken cancellationToken = default
            ) => Task.CompletedTask;
        }
    }
}
