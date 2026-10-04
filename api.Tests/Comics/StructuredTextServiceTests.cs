using ComicStudio.Api.Comics;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// Truncation and malformed JSON look similar on the wire but need opposite
/// responses: a repair call for malformed text, a smaller request for a
/// response that ran out of output. These tests pin that distinction down.
/// </summary>
public class StructuredTextServiceTests
{
    private sealed class Payload
    {
        public string? Name { get; set; }

        public List<int> Items { get; set; } = [];
    }

    [Fact]
    public void Complete_json_parses()
    {
        var outcome = StructuredTextService.Parse<Payload>("""{"name":"Aiko","items":[1,2]}""");

        Assert.NotNull(outcome.Value);
        Assert.Equal("Aiko", outcome.Value!.Name);
        Assert.Null(outcome.Error);
        Assert.False(outcome.Truncated);
    }

    [Fact]
    public void Tolerant_extraction_survives_fences_prose_and_trailing_commas()
    {
        var fenced = StructuredTextService.Parse<Payload>(
            "Here you go:\n```json\n{\"name\": \"Aiko\", \"items\": [1, 2,],}\n```\nDone."
        );

        Assert.NotNull(fenced.Value);
        Assert.Equal([1, 2], fenced.Value!.Items);
        Assert.False(fenced.Truncated);

        var prose = StructuredTextService.Parse<Payload>("""Sure! {"name": "Aiko"} — enjoy.""");
        Assert.NotNull(prose.Value);
    }

    [Fact]
    public void An_unterminated_string_is_truncation()
    {
        var outcome = StructuredTextService.Parse<Payload>(
            """{"name": "Aiko is climbing the fire esc"""
        );

        Assert.Null(outcome.Value);
        Assert.True(outcome.Truncated);
        Assert.NotNull(outcome.Error);
    }

    [Fact]
    public void Unbalanced_braces_are_truncation()
    {
        var outcome = StructuredTextService.Parse<Payload>(
            """{"name":"Aiko","items":[1,2"""
        );

        Assert.Null(outcome.Value);
        Assert.True(outcome.Truncated);
    }

    [Fact]
    public void An_unclosed_code_fence_is_truncation()
    {
        var outcome = StructuredTextService.Parse<Payload>("```json\n{\"name\":\"Aiko\"");

        Assert.Null(outcome.Value);
        Assert.True(outcome.Truncated);
    }

    [Fact]
    public void Complete_but_invalid_json_is_malformed_not_truncated()
    {
        var prose = StructuredTextService.Parse<Payload>("I could not write that.");

        Assert.Null(prose.Value);
        Assert.False(prose.Truncated);
        Assert.NotNull(prose.Error);

        var broken = StructuredTextService.Parse<Payload>("""{"name": , "items": [1,2]}""");

        Assert.Null(broken.Value);
        Assert.False(broken.Truncated);
        Assert.NotNull(broken.Error);
    }

    [Fact]
    public void Empty_output_is_not_truncation()
    {
        var outcome = StructuredTextService.Parse<Payload>("   ");

        Assert.Null(outcome.Value);
        Assert.False(outcome.Truncated);
    }

    [Fact]
    public async Task Truncated_output_is_not_repaired()
    {
        // One truncated answer only. Before the split, this fired a full repair
        // call that truncated again, doubling the cost and still returning zero.
        var client = new ScriptedTextClient("""{"name": "Aiko is climbing the fire esc""");
        var service = ServiceFor(client);

        var result = await service.CompleteAsync<Payload>(Request());

        Assert.False(result.Succeeded);
        Assert.True(result.Truncated);
        Assert.Equal(1, result.Attempts);
        Assert.Single(client.Requests);
        Assert.Equal(
            """{"name": "Aiko is climbing the fire esc""".Length,
            result.ContentChars
        );
    }

    [Fact]
    public async Task Malformed_output_still_gets_a_repair()
    {
        var client = new ScriptedTextClient("not JSON at all", """{"name":"Aiko"}""");
        var service = ServiceFor(client);

        var result = await service.CompleteAsync<Payload>(Request());

        Assert.True(result.Succeeded);
        Assert.False(result.Truncated);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task Reasoning_is_counted_but_never_parsed()
    {
        var client = new DeltaTextClient(
            [
                TextDelta.Reasoning("The cast is Aiko. "),
                TextDelta.Content("""{"name":"Aiko"}"""),
            ]
        );
        var service = ServiceFor(client);

        var result = await service.CompleteAsync<Payload>(Request());

        Assert.True(result.Succeeded);
        Assert.Equal("""{"name":"Aiko"}""".Length, result.ContentChars);
        Assert.Equal("The cast is Aiko. ".Length, result.ReasoningChars);
        Assert.DoesNotContain("The cast", result.Raw);
    }

    private static StructuredTextService ServiceFor(ITextClient client)
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

        return new StructuredTextService(
            new FixedTextResolver(client),
            new CuratorThrottle(configuration, NullLogger<CuratorThrottle>.Instance),
            NullLogger<StructuredTextService>.Instance
        );
    }

    private static StructuredRequest Request() =>
        new()
        {
            Model = new AiModel
            {
                Id = "scripted",
                DisplayName = "Scripted",
                ProviderModelId = "scripted",
            },
            System = "system",
            User = "user",
            Token = "pst-test",
        };
}
