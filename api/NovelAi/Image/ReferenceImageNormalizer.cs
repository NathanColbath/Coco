using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace ComicStudio.Api.NovelAi.Image;

/// <summary>What one normalisation actually did, so callers can log it and tests can assert it.</summary>
/// <param name="SourceWidth">Decoded width of the stored image.</param>
/// <param name="SourceHeight">Decoded height of the stored image.</param>
/// <param name="CanvasWidth">Width of the canvas the image was placed on.</param>
/// <param name="CanvasHeight">Height of the canvas the image was placed on.</param>
/// <param name="SourceBytes">Encoded size of the source in bytes.</param>
/// <param name="OutputBytes">Encoded size of the result in bytes.</param>
/// <param name="AlreadyValid">True when the source was already exactly one of NovelAI's permitted sizes.</param>
public readonly record struct ReferenceImageInfo(
    int SourceWidth,
    int SourceHeight,
    int CanvasWidth,
    int CanvasHeight,
    long SourceBytes,
    long OutputBytes,
    bool AlreadyValid
)
{
    public override string ToString() =>
        $"{SourceWidth}x{SourceHeight} ({SourceBytes} bytes) -> {CanvasWidth}x{CanvasHeight} ({OutputBytes} bytes)";
}

/// <summary>Raised when a director reference cannot be decoded or prepared.</summary>
public sealed class ReferenceImageNormalizationException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Reshapes a director reference image to one of the only three canvases
/// NovelAI accepts: 1024x1536, 1536x1024 or 1472x1472. Anything else is
/// rejected with an opaque HTTP 500, which is why full page renders
/// (1472x2048) failed while reference-free generations succeeded.
///
/// The source is scaled to fit inside the chosen canvas - never cropped and
/// never upscaled - centred, and padded with black. Output is always PNG. An
/// image already at a permitted size is returned byte-identical so a stored
/// asset is never needlessly re-encoded.
/// </summary>
public static class ReferenceImageNormalizer
{
    /// <summary>
    /// NovelAI's permitted director-reference canvases, ordered portrait,
    /// landscape, square. The order is also the tie-break when two canvases
    /// are equally close to the source's aspect ratio.
    /// </summary>
    public static readonly IReadOnlyList<(int Width, int Height)> Canvases =
    [
        (1024, 1536),
        (1536, 1024),
        (1472, 1472),
    ];

    /// <summary>True when the dimensions are exactly one of NovelAI's permitted canvases.</summary>
    public static bool IsPermitted(int width, int height) =>
        Canvases.Any(canvas => canvas.Width == width && canvas.Height == height);

    /// <summary>
    /// Picks the permitted canvas whose aspect ratio is closest to the
    /// source's, comparing the ratios directly (absolute difference). Real
    /// sources are far enough from the midpoints that the metric is
    /// unambiguous; exact ties fall to the earlier entry in <see cref="Canvases"/>.
    /// </summary>
    public static (int Width, int Height) ChooseCanvas(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Image height must be positive.");
        }

        var aspect = width / (double)height;
        var best = Canvases[0];
        var bestError = double.MaxValue;

        foreach (var canvas in Canvases)
        {
            var error = Math.Abs(aspect - canvas.Width / (double)canvas.Height);
            if (error < bestError)
            {
                best = canvas;
                bestError = error;
            }
        }

        return best;
    }

    /// <summary>
    /// Normalises a stored PNG to a permitted director-reference canvas.
    /// Pure and synchronous; throws <see cref="ReferenceImageNormalizationException"/>
    /// rather than returning a broken reference.
    /// </summary>
    public static byte[] Normalize(byte[] source, out ReferenceImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Length == 0)
        {
            throw new ReferenceImageNormalizationException("The reference image is empty.");
        }

        Image<Rgba32> image;
        try
        {
            image = ImageSharpImage.Load<Rgba32>(source);
        }
        catch (Exception exception) when (exception is ImageFormatException or NotSupportedException)
        {
            throw new ReferenceImageNormalizationException(
                $"The reference image could not be decoded ({source.Length} bytes).",
                exception
            );
        }

        using (image)
        {
            var sourceWidth = image.Width;
            var sourceHeight = image.Height;

            // Idempotence: already exactly a permitted canvas. Hand back the
            // stored bytes untouched; re-encoding would only lose quality.
            if (IsPermitted(sourceWidth, sourceHeight))
            {
                info = new ReferenceImageInfo(
                    sourceWidth,
                    sourceHeight,
                    sourceWidth,
                    sourceHeight,
                    source.LongLength,
                    source.LongLength,
                    AlreadyValid: true
                );
                return source;
            }

            var canvas = ChooseCanvas(sourceWidth, sourceHeight);

            // Fit inside the canvas, never past the source's own size.
            var scale = Math.Min(
                1.0,
                Math.Min(
                    canvas.Width / (double)sourceWidth,
                    canvas.Height / (double)sourceHeight
                )
            );
            var targetWidth = Math.Clamp(
                (int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero),
                1,
                canvas.Width
            );
            var targetHeight = Math.Clamp(
                (int)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero),
                1,
                canvas.Height
            );

            if (targetWidth != sourceWidth || targetHeight != sourceHeight)
            {
                // Lanczos3 is a ratio-aware windowed-sinc filter: it low-passes
                // before decimating, so screentone averages out instead of
                // aliasing into moire, while the sharp passband keeps inked
                // linework crisp. Nearest-neighbour (aliasing) and filters
                // without a widened kernel are the usual cause of halftone
                // break-up at these scale factors.
                image.Mutate(context =>
                    context.Resize(targetWidth, targetHeight, KnownResamplers.Lanczos3)
                );
            }

            // Padding is opaque black, matching what NovelAI asks for.
            using var result = new Image<Rgba32>(canvas.Width, canvas.Height, new Rgba32(0, 0, 0));
            var left = (canvas.Width - targetWidth) / 2;
            var top = (canvas.Height - targetHeight) / 2;
            result.Mutate(context => context.DrawImage(image, new Point(left, top), 1f));

            using var buffer = new MemoryStream();
            result.SaveAsPng(buffer);
            var png = buffer.ToArray();

            info = new ReferenceImageInfo(
                sourceWidth,
                sourceHeight,
                canvas.Width,
                canvas.Height,
                source.LongLength,
                png.LongLength,
                AlreadyValid: false
            );
            return png;
        }
    }
}
