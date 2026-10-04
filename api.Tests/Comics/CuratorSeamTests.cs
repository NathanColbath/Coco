using System.Runtime.CompilerServices;
using ComicStudio.Api.Comics;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Storytelling;
using ComicStudio.Api.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// The seam pass reduces each page to its opening sentence, its closing
/// sentence and its last beat. Those three picks are private, so the only way
/// to see them is the prompt FixSeamsAsync sends; a scripted text client lets
/// us read that prompt back without a live model.
/// </summary>
public class CuratorSeamTests
{
    private sealed class ScriptedTextClient(params string[] responses) : ITextClient
    {
        private int index;

        public string Transport => "scripted";

        public List<TextGenerationRequest> Requests { get; } = [];

        public async IAsyncEnumerable<TextDelta> StreamAsync(
            TextGenerationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            var response = responses[Math.Min(index, responses.Length - 1)];
            index++;
            await Task.Yield();
            yield return TextDelta.Content(response);
        }
    }

    private sealed class FixedResolver(ITextClient client) : ITextClientResolver
    {
        public ITextClient Resolve(AiModel model) => client;
    }

    private static Curator CuratorFor(ITextClient client)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Curator:MaxConcurrentTextRequests"] = "2",
                    ["Curator:RetryBaseDelayMs"] = "1",
                }
            )
            .Build();

        var structured = new StructuredTextService(
            new FixedResolver(client),
            new CuratorThrottle(configuration, NullLogger<CuratorThrottle>.Instance),
            NullLogger<StructuredTextService>.Instance
        );

        return new Curator(structured, new HeuristicTokenCounter());
    }

    private static CuratorContext Context() =>
        new(
            "pst-test",
            new AiModel
            {
                Id = "scripted",
                DisplayName = "Scripted",
                ProviderModelId = "scripted",
            },
            ContextTokens: 8000,
            DialogueAdaptation.Verbatim,
            ReadingDirection.RightToLeft
        );

    private static List<ComicPage> TwoPages() =>
    [
        new()
        {
            Index = 0,
            Script = "The rain falls. Aiko steps onto the roof. She looks down.",
            Continuity = "LOCATION rooftop\nLAST BEAT the neon sign flickers",
        },
        new()
        {
            Index = 1,
            Script = "Below, the street is empty.",
            Continuity = "LOCATION street\nLAST BEAT the door slams shut",
        },
    ];

    [Fact]
    public async Task Seam_prompt_reduces_each_page_to_its_opening_closing_and_beat()
    {
        var client = new ScriptedTextClient("""{"fixes":[]}""");

        await CuratorFor(client).FixSeamsAsync(TwoPages(), Context());

        var prompt = Assert.Single(client.Requests).Prompt;
        Assert.Contains("page 1:", prompt);
        Assert.Contains("page 2:", prompt);
        // Only the first and last sentences survive; the middle is noise.
        Assert.Contains("opens \"The rain falls\"", prompt);
        Assert.Contains("closes \"She looks down.\"", prompt);
        Assert.DoesNotContain("Aiko steps onto the roof", prompt);
        // The beat is pulled out of the continuity ledger.
        Assert.Contains("last beat: the neon sign flickers", prompt);
    }

    [Fact]
    public async Task Returns_only_the_rewritten_openings_keyed_by_index()
    {
        var client = new ScriptedTextClient(
            """{"fixes":[{"index":0,"opening":"The rain has stopped."},{"index":1,"opening":"  "}]}"""
        );

        var fixes = await CuratorFor(client).FixSeamsAsync(TwoPages(), Context());

        // A blank rewrite is not a fix and is dropped.
        Assert.Single(fixes);
        Assert.Equal("The rain has stopped.", fixes[0]);
    }

    [Fact]
    public async Task Does_not_call_the_model_for_a_single_page()
    {
        var client = new ScriptedTextClient("""{"fixes":[]}""");

        var fixes = await CuratorFor(client).FixSeamsAsync(
            [new ComicPage { Index = 0, Script = "Only page." }],
            Context()
        );

        Assert.Empty(fixes);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task A_page_without_a_script_still_contributes_a_seam_line()
    {
        var client = new ScriptedTextClient("""{"fixes":[]}""");

        await CuratorFor(client)
            .FixSeamsAsync(
                [new ComicPage { Index = 0 }, new ComicPage { Index = 1, Script = "Second." }],
                Context()
            );

        var prompt = Assert.Single(client.Requests).Prompt;
        Assert.Contains("opens \"\"", prompt);
        Assert.Contains("last beat: (unknown)", prompt);
    }

    [Fact]
    public async Task Seam_prompt_falls_back_to_the_raw_continuity_when_no_beat_is_marked()
    {
        var client = new ScriptedTextClient("""{"fixes":[]}""");
        var pages = new List<ComicPage>
        {
            new() { Index = 0, Script = "First.", Continuity = "Aiko carries the lantern" },
            new() { Index = 1, Script = "Second.", Continuity = "The lantern is lit" },
        };

        await CuratorFor(client).FixSeamsAsync(pages, Context());

        var prompt = Assert.Single(client.Requests).Prompt;
        Assert.Contains("last beat: Aiko carries the lantern", prompt);
        Assert.Contains("last beat: The lantern is lit", prompt);
    }
}
