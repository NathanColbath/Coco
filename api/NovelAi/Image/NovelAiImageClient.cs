using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComicStudio.Api.NovelAi.Image;

/// <summary>
/// Calls NovelAI's image endpoint. The response is a ZIP archive containing one
/// PNG per sample, so the archive is unwrapped before anything else sees it.
/// </summary>
public class NovelAiImageClient(HttpClient http, ILogger<NovelAiImageClient> logger)
{
    public const string TransportName = "image";

    public async Task<ImageGenerationResult> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var body = ImageRequestBuilder.Build(request);

        using var message = new HttpRequestMessage(HttpMethod.Post, "ai/generate-image")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.Token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));

        HttpResponseMessage response;
        try
        {
            // Generation can take a while; the client has no timeout for this call.
            response = await http.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "NovelAI image endpoint is unreachable.");
            throw new ImageGenerationException(
                "Could not reach NovelAI. Check your connection and try again."
            );
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var text = await SafeReadAsync(response, cancellationToken);
                throw new ImageGenerationException(
                    Describe(response.StatusCode, text),
                    (int)response.StatusCode
                );
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            // ZipArchive needs a seekable stream.
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            var payload = buffer.ToArray();

            var png = ExtractPng(payload);
            var (width, height) = PngInfo.ReadDimensions(png);

            return new ImageGenerationResult
            {
                Png = png,
                Width = width,
                Height = height,
                Metadata = PngInfo.ReadComment(png),
            };
        }
    }

    private static byte[] ExtractPng(byte[] payload)
    {
        if (PngInfo.IsPng(payload))
        {
            return payload;
        }

        if (payload.Length < 4 || payload[0] != 0x50 || payload[1] != 0x4B)
        {
            // Not a PNG and not a ZIP: surface whatever NovelAI said.
            var text = Encoding.UTF8.GetString(payload, 0, Math.Min(payload.Length, 500));
            throw new ImageGenerationException($"NovelAI returned an unexpected payload. {text}");
        }

        using var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read);
        var entry =
            archive.Entries.FirstOrDefault(item =>
                item.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            )
            ?? archive.Entries.FirstOrDefault()
            ?? throw new ImageGenerationException("NovelAI returned an empty archive.");

        using var entryStream = entry.Open();
        using var image = new MemoryStream();
        entryStream.CopyTo(image);
        return image.ToArray();
    }

    private static async Task<string> SafeReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string Describe(HttpStatusCode status, string body)
    {
        // Trial accounts get a 400 demanding a reCAPTCHA token; paid accounts
        // that lapsed get a 402. Both mean "no subscription".
        if (NovelAiErrors.IsRecaptchaRequired(body))
        {
            return NovelAiErrors.SubscriptionRequired;
        }

        return NovelAiErrors.Describe(status, NovelAiErrors.ExtractMessage(body));
    }

    private static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            var node = JsonNode.Parse(body);
            var message =
                node?["message"]?.GetValue<string>()
                ?? node?["error"]?.GetValue<string>()
                ?? node?["detail"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body.
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "…";
    }
}
