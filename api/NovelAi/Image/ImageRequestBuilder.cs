using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace ComicStudio.Api.NovelAi.Image;

/// <summary>
/// Builds the image request body. Kept pure and separate from transport because
/// NovelAI's newer parameters are undocumented: these shapes are pinned by
/// tests, and there is exactly one place to change when they move.
///
/// Two quirks the API enforces:
/// <list type="bullet">
/// <item><c>v4_prompt</c> and <c>v4_negative_prompt</c> must both be present or
/// the request fails with a 500 rather than a validation error.</item>
/// <item>Character captions are positional arrays, so the negative caption list
/// must have the same length as the positive one.</item>
/// </list>
/// </summary>
public static class ImageRequestBuilder
{
    /// <summary>
    /// This class is static and its production call site lives outside this
    /// file, so there is no injected ILogger to use. A dedicated console logger
    /// keeps the reference-normalisation evidence visible in the server output
    /// no matter how the host's logging is configured.
    /// </summary>
    private static readonly Lazy<ILogger> Logger = new(
        () =>
            LoggerFactory
                .Create(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                    builder.AddSimpleConsole(options => options.SingleLine = true);
                })
                .CreateLogger("ComicStudio.Api.NovelAi.ImageRequestBuilder"),
        LazyThreadSafetyMode.ExecutionAndPublication
    );

    /// <summary>
    /// Normalised references keyed by a hash of the source bytes. The same
    /// asset is reused across generations (style anchors, character sheets)
    /// and <see cref="Build"/> runs twice per generation, so re-encoding every
    /// time is pure waste. Bounded by total bytes; the cache evicts on
    /// pressure.
    /// </summary>
    private static readonly MemoryCache NormalizedCache = new(
        new MemoryCacheOptions { SizeLimit = 256L * 1024 * 1024 }
    );

    public static JsonObject Build(ImageGenerationRequest request)
    {
        var positive = BuildCaption(request.BaseCaption, request.Characters, includePositions: request.UseCharacterPrompts);
        var negativeCharacters = request.Characters
            .Select(character => character.NegativeTags ?? string.Empty)
            .ToArray();

        var parameters = new JsonObject
        {
            ["params_version"] = request.ParamsVersion,
            ["width"] = request.Width,
            ["height"] = request.Height,
            ["scale"] = request.Scale,
            ["sampler"] = request.Sampler,
            ["steps"] = request.Steps,
            ["seed"] = request.Seed,
            ["n_samples"] = 1,
            ["ucPreset"] = request.UcPreset,
            ["qualityToggle"] = request.QualityToggle,
            ["dynamic_thresholding"] = false,
            ["controlnet_strength"] = 1,
            ["cfg_rescale"] = 0,
            ["noise_schedule"] = request.NoiseSchedule,
            ["legacy"] = false,
            ["legacy_v3_extend"] = false,
            ["add_original_image"] = false,
            ["uncond_scale"] = 1,
            ["prompt"] = request.BaseCaption,
            ["negative_prompt"] = request.NegativeCaption,
            ["v4_prompt"] = positive,
            ["v4_negative_prompt"] = BuildCaption(
                request.NegativeCaption,
                negativeCharacters.Select(tags => new CharacterPrompt(tags, 0.5, 0.5)).ToArray(),
                includePositions: false
            ),
        };

        AppendReferences(parameters, request);

        return new JsonObject
        {
            ["input"] = request.BaseCaption,
            ["model"] = request.ModelId,
            ["action"] = "generate",
            ["parameters"] = parameters,
        };
    }

    private static JsonObject BuildCaption(
        string baseCaption,
        IReadOnlyList<CharacterPrompt> characters,
        bool includePositions
    )
    {
        var captions = new JsonArray();

        foreach (var character in characters)
        {
            var entry = new JsonObject { ["char_caption"] = character.Tags };

            if (includePositions)
            {
                entry["centers"] = new JsonArray(
                    new JsonObject { ["x"] = character.CenterX, ["y"] = character.CenterY }
                );
            }
            else
            {
                entry["centers"] = new JsonArray(
                    new JsonObject { ["x"] = 0.5, ["y"] = 0.5 }
                );
            }

            captions.Add(entry);
        }

        return new JsonObject
        {
            ["caption"] = new JsonObject
            {
                ["base_caption"] = baseCaption,
                ["char_captions"] = captions,
            },
            ["use_coords"] = includePositions && characters.Count > 0,
            ["use_order"] = characters.Count > 0,
            ["legacy_uc"] = false,
        };
    }

    private static void AppendReferences(JsonObject parameters, ImageGenerationRequest request)
    {
        if (!request.UsePreciseReference || request.References.Count == 0)
        {
            // The arrays are always sent, even when empty, to keep one body shape.
            parameters["director_reference_images"] = new JsonArray();
            parameters["director_reference_descriptions"] = new JsonArray();
            parameters["director_reference_strength_values"] = new JsonArray();
            parameters["director_reference_secondary_strength_values"] = new JsonArray();
            parameters["director_reference_information_extracted"] = new JsonArray();
            parameters["normalize_reference_strength_multiple"] = false;
            return;
        }

        var images = new JsonArray();
        var descriptions = new JsonArray();
        var strengths = new JsonArray();
        var secondary = new JsonArray();
        var extracted = new JsonArray();

        for (var index = 0; index < request.References.Count; index++)
        {
            var reference = request.References[index];

            // NovelAI only accepts 1024x1536, 1536x1024 or 1472x1472 for
            // director references; anything else fails the whole request with
            // an opaque 500. Normalise before the base64 encoding below.
            images.Add(Convert.ToBase64String(NormalizeReference(reference, index)));
            descriptions.Add(
                new JsonObject
                {
                    ["caption"] = new JsonObject
                    {
                        ["base_caption"] = Describe(reference.Kind),
                        ["char_captions"] = new JsonArray(),
                    },
                    ["legacy_uc"] = false,
                }
            );
            strengths.Add(Math.Clamp(reference.Strength, 0, 1));
            // NovelAI's secondary strength is the inverse of the fidelity slider.
            secondary.Add(Math.Clamp(1 - reference.Fidelity, 0, 1));
            extracted.Add(1.0);
        }

        parameters["director_reference_images"] = images;
        parameters["director_reference_descriptions"] = descriptions;
        parameters["director_reference_strength_values"] = strengths;
        parameters["director_reference_secondary_strength_values"] = secondary;
        parameters["director_reference_information_extracted"] = extracted;
        parameters["normalize_reference_strength_multiple"] = false;
    }

    /// <summary>
    /// Returns the reference as a permitted-size PNG, reusing the last result
    /// for identical source bytes. Decode failures throw rather than silently
    /// dropping the reference: a page drawn without its reference is an
    /// inconsistency the user cannot see.
    /// </summary>
    private static byte[] NormalizeReference(ReferenceImage reference, int index)
    {
        var key = Convert.ToHexString(SHA256.HashData(reference.Bytes));

        if (NormalizedCache.TryGetValue(key, out CachedReference? cached) && cached is not null)
        {
            LogNormalized(index, reference.Kind, cached.Info);
            return cached.Png;
        }

        byte[] png;
        ReferenceImageInfo info;
        try
        {
            png = ReferenceImageNormalizer.Normalize(reference.Bytes, out info);
        }
        catch (ReferenceImageNormalizationException exception)
        {
            throw new ReferenceImageNormalizationException(
                $"Director reference {index + 1} ({reference.Kind}) could not be prepared: {exception.Message}",
                exception
            );
        }

        NormalizedCache.Set(
            key,
            new CachedReference(png, info),
            new MemoryCacheEntryOptions { Size = png.LongLength }
        );
        LogNormalized(index, reference.Kind, info);
        return png;
    }

    /// <summary>
    /// The operational evidence for the reference fix: source dimensions,
    /// chosen canvas and encoded size before/after for every reference sent.
    /// </summary>
    private static void LogNormalized(int index, ReferenceKind kind, ReferenceImageInfo info) =>
        Logger.Value.LogInformation(
            "Director reference {Index} ({Kind}) normalised: {SourceWidth}x{SourceHeight} "
                + "({SourceBytes} bytes) -> canvas {CanvasWidth}x{CanvasHeight} ({OutputBytes} bytes){Note}.",
            index + 1,
            kind,
            info.SourceWidth,
            info.SourceHeight,
            info.SourceBytes,
            info.CanvasWidth,
            info.CanvasHeight,
            info.OutputBytes,
            info.AlreadyValid ? " (already valid, unchanged)" : string.Empty
        );

    private sealed record CachedReference(byte[] Png, ReferenceImageInfo Info);

    private static string Describe(ReferenceKind kind) =>
        kind switch
        {
            ReferenceKind.Character => "character",
            ReferenceKind.Style => "style",
            _ => "character&style",
        };
}
