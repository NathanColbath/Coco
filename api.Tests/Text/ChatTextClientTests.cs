using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ComicStudio.Api.Domain;
using ComicStudio.Api.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComicStudio.Api.Tests.Text;

/// <summary>
/// Exercises the chat transport through a stub HTTP handler: fragmented
/// "&lt;think&gt;" markers must arrive as reasoning, and max_tokens must obey
/// the single configured ceiling instead of the old hardcoded 2048.
/// </summary>
public class ChatTextClientTests
{
    private const string ThinkTag = "<think>";
    private const string ThinkClose = "</think>";

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
            _ => Sse("data: [DONE]\n\n");

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastBody =
                request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);
            return Respond(request);
        }
    }

    private static HttpResponseMessage Sse(params string[] contents)
    {
        var builder = new StringBuilder();

        foreach (var content in contents)
        {
            var json = JsonSerializer.Serialize(
                new { choices = new[] { new { delta = new { content } } } }
            );
            builder.Append("data: ").Append(json).Append("\n\n");
        }

        builder.Append("data: [DONE]\n\n");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(builder.ToString(), Encoding.UTF8, "text/event-stream"),
        };
    }

    private static (ChatTextClient Client, StubHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null,
        Dictionary<string, string?>? settings = null
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var handler = new StubHandler();
        if (respond is not null)
        {
            handler.Respond = respond;
        }

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://text.novelai.net") };
        var client = new ChatTextClient(http, configuration, NullLogger<ChatTextClient>.Instance);

        return (client, handler);
    }

    private static TextGenerationRequest Request(
        int maxTokens,
        JsonObject? extra = null,
        bool bypassClamp = false
    ) =>
        new()
        {
            ModelId = "glm-4.6",
            Messages = [new TextMessage("system", "sys"), new TextMessage("user", "hi")],
            Prompt = "hi",
            Sampler = new TextSamplerSettings { MaxTokens = maxTokens, Temperature = 0.7 },
            Token = "token",
            ExtraParameters = extra,
            BypassOutputTokenClamp = bypassClamp,
        };

    private static async Task<List<TextDelta>> ConsumeAsync(
        ITextClient client,
        TextGenerationRequest request
    )
    {
        var deltas = new List<TextDelta>();
        await foreach (var delta in client.StreamAsync(request, CancellationToken.None))
        {
            deltas.Add(delta);
        }

        return deltas;
    }

    private static string Joined(IEnumerable<TextDelta> deltas, TextDeltaKind kind) =>
        string.Concat(
            deltas.Where(delta => delta.Kind == kind).Select(delta => delta.Text)
        );

    private static async Task<JsonObject> LastPayloadAsync(StubHandler handler)
    {
        var body = handler.LastBody;
        Assert.NotNull(body);
        return JsonNode.Parse(body)!.AsObject();
    }

    [Fact]
    public async Task Fragmented_think_markers_become_reasoning_and_the_answer_becomes_content()
    {
        var (client, _) = CreateClient(
            _ =>
                Sse(
                    "<thi",
                    "nk>Let me think",
                    " about this",
                    "</thi",
                    "nk>Hello there."
                )
        );

        var deltas = await ConsumeAsync(client, Request(512));

        Assert.Equal(ThinkTag + "Let me think about this" + ThinkClose, Joined(deltas, TextDeltaKind.Reasoning));
        Assert.Equal("Hello there.", Joined(deltas, TextDeltaKind.Content));
        Assert.Equal(
            ThinkTag + "Let me think about this" + ThinkClose + "Hello there.",
            string.Concat(deltas.Select(delta => delta.Text))
        );
    }

    [Fact]
    public async Task Nothing_is_held_back_when_the_stream_has_no_markers()
    {
        var (client, _) = CreateClient(_ => Sse("Hello", " world", "!"));

        var deltas = await ConsumeAsync(client, Request(512));

        Assert.All(deltas, delta => Assert.Equal(TextDeltaKind.Content, delta.Kind));
        Assert.Equal("Hello world!", string.Concat(deltas.Select(delta => delta.Text)));
    }

    [Fact]
    public async Task An_unterminated_think_block_is_flushed_as_reasoning_at_end_of_stream()
    {
        var (client, _) = CreateClient(_ => Sse("Answer? <think>thinking forever"));

        var deltas = await ConsumeAsync(client, Request(512));

        Assert.Equal("Answer? ", Joined(deltas, TextDeltaKind.Content));
        Assert.Equal(ThinkTag + "thinking forever", Joined(deltas, TextDeltaKind.Reasoning));
    }

    [Fact]
    public async Task Default_ceiling_clamps_a_huge_request_to_4096()
    {
        var (client, handler) = CreateClient();
        await ConsumeAsync(client, Request(99_999));

        var payload = await LastPayloadAsync(handler);
        Assert.Equal(4096, payload["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task Configured_ceiling_is_honoured()
    {
        var (client, handler) = CreateClient(
            settings: new Dictionary<string, string?>
            {
                [TextTokenLimits.MaxOutputTokensConfigKey] = "8192",
            }
        );

        await ConsumeAsync(client, Request(99_999));

        var payload = await LastPayloadAsync(handler);
        Assert.Equal(8192, payload["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_small_request_is_not_raised_to_the_ceiling()
    {
        var (client, handler) = CreateClient();
        await ConsumeAsync(client, Request(160));

        var payload = await LastPayloadAsync(handler);
        Assert.Equal(160, payload["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task Diagnostics_can_bypass_the_clamp_for_the_ceiling_probe()
    {
        var (client, handler) = CreateClient();
        await ConsumeAsync(client, Request(8192, bypassClamp: true));

        var payload = await LastPayloadAsync(handler);
        Assert.Equal(8192, payload["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task Extra_probe_parameters_are_merged_but_cannot_replace_core_fields()
    {
        var extra = new JsonObject
        {
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
            ["model"] = "tampered",
        };

        var (client, handler) = CreateClient();
        await ConsumeAsync(client, Request(512, extra));

        var payload = await LastPayloadAsync(handler);
        Assert.False(
            payload["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>()
        );
        Assert.Equal("glm-4.6", payload["model"]!.GetValue<string>());
    }
}
