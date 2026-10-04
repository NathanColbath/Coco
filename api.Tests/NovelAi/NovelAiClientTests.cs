using System.Net;
using System.Text;
using ComicStudio.Api.NovelAi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComicStudio.Api.Tests.NovelAi;

/// <summary>
/// The connect flow used to discard NovelAI's own error message, and it used to
/// ask the primary (frontend) API for account details. Both made a rejected
/// token hard to diagnose. These tests pin the behaviour we now rely on.
/// </summary>
public class NovelAiClientTests
{
    private sealed class HostHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> responses;

        public HostHandler(params (string Host, HttpStatusCode Status, string Body)[] entries)
        {
            responses = entries.ToDictionary(
                entry => entry.Host,
                entry => (entry.Status, entry.Body),
                StringComparer.OrdinalIgnoreCase
            );
        }

        public List<string> RequestedHosts { get; } = [];

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastRequest = request;
            var host = request.RequestUri?.Host ?? "unknown";
            RequestedHosts.Add(host);

            if (!responses.TryGetValue(host, out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(
                new HttpResponseMessage(response.Status)
                {
                    Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private const string SubscriptionBody =
        """
        {
          "tier": 3,
          "active": true,
          "expiresAt": 1893456000,
          "trainingStepsLeft": { "fixedTrainingStepsLeft": 10, "purchasedTrainingSteps": 5 }
        }
        """;

    private static NovelAiClient Client(HostHandler handler, string? accountBaseUrl = null)
    {
        var settings = new Dictionary<string, string?>();
        if (accountBaseUrl is not null)
        {
            settings["NovelAi:AccountBaseUrl"] = accountBaseUrl;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new NovelAiClient(
            new HttpClient(handler),
            configuration,
            NullLogger<NovelAiClient>.Instance
        );
    }

    [Fact]
    public async Task Asks_the_image_host_for_the_account_by_default()
    {
        var handler = new HostHandler(("image.novelai.net", HttpStatusCode.OK, SubscriptionBody));

        var result = await Client(handler).GetAccountAsync("pst-token");

        // The documentation steers third-party API users away from the primary API.
        Assert.Equal("image.novelai.net", handler.RequestedHosts[0]);
        Assert.Equal(NovelAiProbeStatus.Ok, result.Status);
        Assert.Equal(Domain.NovelAiTier.Opus, result.Account?.Tier);
    }

    [Fact]
    public async Task Falls_back_to_the_primary_api_when_the_image_host_cannot_answer()
    {
        var handler = new HostHandler(
            ("image.novelai.net", HttpStatusCode.NotFound, "{}"),
            ("api.novelai.net", HttpStatusCode.OK, SubscriptionBody)
        );

        var result = await Client(handler).GetAccountAsync("pst-token");

        Assert.Equal(NovelAiProbeStatus.Ok, result.Status);
        Assert.Equal(["image.novelai.net", "api.novelai.net"], handler.RequestedHosts);
    }

    [Fact]
    public async Task Does_not_retry_another_host_when_the_token_is_rejected()
    {
        var handler = new HostHandler(
            ("image.novelai.net", HttpStatusCode.Unauthorized, """{"message":"User not found."}"""),
            ("api.novelai.net", HttpStatusCode.OK, SubscriptionBody)
        );

        var result = await Client(handler).GetAccountAsync("pst-token");

        Assert.Equal(NovelAiProbeStatus.Unauthorized, result.Status);
        Assert.Equal(["image.novelai.net"], handler.RequestedHosts);
    }

    [Fact]
    public async Task Honours_a_configured_account_host()
    {
        var handler = new HostHandler(("api.novelai.net", HttpStatusCode.OK, SubscriptionBody));

        var result = await Client(handler, "https://api.novelai.net").GetAccountAsync("pst-token");

        Assert.Equal(NovelAiProbeStatus.Ok, result.Status);
        Assert.Equal("api.novelai.net", handler.RequestedHosts[0]);
    }

    [Fact]
    public async Task Surfaces_novels_message_when_the_header_is_rejected()
    {
        var handler = new HostHandler(
            (
                "image.novelai.net",
                HttpStatusCode.BadRequest,
                """{"statusCode":400,"message":"Invalid Authorization header content."}"""
            )
        );

        var result = await Client(handler).GetAccountAsync("pst-token");

        Assert.Equal(NovelAiProbeStatus.InvalidResponse, result.Status);
        Assert.Contains("Invalid Authorization header content.", result.Message);
    }

    [Fact]
    public async Task Surfaces_novels_message_when_the_token_is_unknown()
    {
        var handler = new HostHandler(
            (
                "image.novelai.net",
                HttpStatusCode.Unauthorized,
                """{"statusCode":401,"message":"User not found."}"""
            )
        );

        var result = await Client(handler).GetAccountAsync("pst-token");

        Assert.Equal(NovelAiProbeStatus.Unauthorized, result.Status);
        Assert.Contains("User not found.", result.Message);
    }

    [Fact]
    public async Task Strips_whitespace_from_a_token_pasted_across_a_line_break()
    {
        // NovelAI answers 400 for a header containing whitespace; a wrapped paste
        // is the usual cause and the token itself is fine.
        var handler = new HostHandler(("image.novelai.net", HttpStatusCode.OK, SubscriptionBody));

        var result = await Client(handler).GetAccountAsync("pst-abc\n123 456");

        Assert.Equal(NovelAiProbeStatus.Ok, result.Status);
        Assert.Equal("pst-abc123456", handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Reads_the_account_summary()
    {
        var handler = new HostHandler(("image.novelai.net", HttpStatusCode.OK, SubscriptionBody));

        var result = await Client(handler).GetAccountAsync("pst-token");

        Assert.Equal(NovelAiProbeStatus.Ok, result.Status);
        Assert.Equal(Domain.NovelAiTier.Opus, result.Account?.Tier);
        Assert.True(result.Account?.Active);
        Assert.Equal(15, result.Account?.AnlasBalance);
        Assert.NotNull(result.Account?.ExpiresAt);
    }
}
