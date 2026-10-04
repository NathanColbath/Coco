using ComicStudio.Api.NovelAi.Image;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ComicStudio.Api.Tests.NovelAi;

/// <summary>
/// NovelAI rejects any director reference that is not exactly 1024x1536,
/// 1536x1024 or 1472x1472 with an opaque 500. These tests pin the reshaping
/// rules: nearest canvas, fit inside, centre, black padding, PNG out, and no
/// re-encode of an image that is already valid.
/// </summary>
public class ReferenceImageNormalizerTests
{
    private static readonly (int Width, int Height)[] Permitted =
    [
        (1024, 1536),
        (1536, 1024),
        (1472, 1472),
    ];

    [Theory]
    [InlineData(1600, 900, 1536, 1024)] // landscape 16:9
    [InlineData(900, 1600, 1024, 1536)] // portrait 9:16
    [InlineData(1000, 1000, 1472, 1472)] // square
    [InlineData(1472, 2048, 1024, 1536)] // the production page shape
    public void Lands_on_the_canvas_with_the_closest_aspect_ratio(
        int width,
        int height,
        int expectedWidth,
        int expectedHeight
    )
    {
        var output = ReferenceImageNormalizer.Normalize(TestImages.Png(width, height), out var info);

        Assert.Equal((expectedWidth, expectedHeight), PngInfo.ReadDimensions(output));
        Assert.Equal((expectedWidth, expectedHeight), (info.CanvasWidth, info.CanvasHeight));
    }

    [Theory]
    [InlineData(1472, 2048)]
    [InlineData(2048, 1472)]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]
    [InlineData(4096, 64)]
    [InlineData(64, 4096)]
    [InlineData(7, 5)]
    [InlineData(5, 7)]
    [InlineData(1, 1)]
    public void Output_dimensions_are_always_permitted(int width, int height)
    {
        var output = ReferenceImageNormalizer.Normalize(TestImages.Png(width, height), out _);

        Assert.Contains(PngInfo.ReadDimensions(output), Permitted);
    }

    [Fact]
    public void Preserves_aspect_ratio_within_a_pixel_and_centres_the_content()
    {
        const int sourceWidth = 1472;
        const int sourceHeight = 2048;

        var output = ReferenceImageNormalizer.Normalize(
            TestImages.Png(sourceWidth, sourceHeight),
            out var info
        );
        var bounds = ContentBounds(output);

        // The portrait canvas 1024x1536 is chosen; the width limit binds.
        Assert.Equal((1024, 1536), (info.CanvasWidth, info.CanvasHeight));
        var scale = 1024 / (double)sourceWidth;

        Assert.InRange(Math.Abs(bounds.Width - sourceWidth * scale), 0, 1);
        Assert.InRange(Math.Abs(bounds.Height - sourceHeight * scale), 0, 1);
        Assert.Equal(sourceWidth / (double)sourceHeight, bounds.Width / (double)bounds.Height, 3);

        // Centred: equal black margins on both sides of each axis.
        Assert.Equal((info.CanvasWidth - bounds.Width) / 2, bounds.X);
        Assert.Equal((info.CanvasHeight - bounds.Height) / 2, bounds.Y);
    }

    [Fact]
    public void Pads_with_opaque_black_to_the_canvas_edges()
    {
        // 2:1 lands on the landscape canvas, leaving padding above and below.
        var output = ReferenceImageNormalizer.Normalize(TestImages.Png(200, 100), out var info);
        var bounds = ContentBounds(output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(1536, image.Width);
        Assert.Equal(1024, image.Height);

        foreach (var (x, y) in new[]
        {
            (0, 0),
            (image.Width - 1, 0),
            (0, image.Height - 1),
            (image.Width - 1, image.Height - 1),
        })
        {
            var pixel = image[x, y];
            Assert.Equal(0, pixel.R);
            Assert.Equal(0, pixel.G);
            Assert.Equal(0, pixel.B);
            Assert.Equal(255, pixel.A);
        }

        // Directly outside the content is padding, the content itself is not.
        var outside = image[bounds.X - 1, bounds.Y - 1];
        Assert.Equal(new Rgba32(0, 0, 0), outside);
        var inside = image[bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2];
        Assert.Equal(new Rgba32(200, 30, 30), inside);
        Assert.True(info.CanvasWidth == 1536 && info.CanvasHeight == 1024);
    }

    [Fact]
    public void A_tiny_image_is_padded_rather_than_upscaled()
    {
        var output = ReferenceImageNormalizer.Normalize(TestImages.Png(20, 10), out var info);
        var bounds = ContentBounds(output);

        // Landscape canvas, but the 20x10 source keeps every pixel.
        Assert.Equal((1536, 1024), (info.CanvasWidth, info.CanvasHeight));
        Assert.Equal(20, bounds.Width);
        Assert.Equal(10, bounds.Height);
        Assert.Equal((1536 - 20) / 2, bounds.X);
        Assert.Equal((1024 - 10) / 2, bounds.Y);
    }

    [Theory]
    [InlineData(1024, 1536)]
    [InlineData(1536, 1024)]
    [InlineData(1472, 1472)]
    public void An_already_permitted_image_is_returned_byte_identical(int width, int height)
    {
        var source = TestImages.Png(width, height);

        var output = ReferenceImageNormalizer.Normalize(source, out var info);

        Assert.Same(source, output);
        Assert.True(info.AlreadyValid);
        Assert.Equal(source.LongLength, info.SourceBytes);
        Assert.Equal(source.LongLength, info.OutputBytes);
    }

    [Fact]
    public void Output_round_trips_as_a_readable_png()
    {
        var output = ReferenceImageNormalizer.Normalize(TestImages.Png(1472, 2048), out _);

        Assert.True(PngInfo.IsPng(output));

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(1024, image.Width);
        Assert.Equal(1536, image.Height);
    }

    [Fact]
    public void Undecodable_bytes_throw_a_specific_exception()
    {
        var exception = Assert.Throws<ReferenceImageNormalizationException>(() =>
            ReferenceImageNormalizer.Normalize([0x00, 0x01, 0x02, 0x03, 0x04], out _)
        );

        Assert.Contains("could not be decoded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_bytes_throw_rather_than_sending_a_reference_less_page()
    {
        var exception = Assert.Throws<ReferenceImageNormalizationException>(() =>
            ReferenceImageNormalizer.Normalize([], out _)
        );

        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1600, 900, 1536, 1024)]
    [InlineData(900, 1600, 1024, 1536)]
    [InlineData(1000, 1000, 1472, 1472)]
    public void ChooseCanvas_matches_the_permitted_canvases(
        int width,
        int height,
        int expectedWidth,
        int expectedHeight
    )
    {
        Assert.Equal(
            (expectedWidth, expectedHeight),
            ReferenceImageNormalizer.ChooseCanvas(width, height)
        );
    }

    /// <summary>Bounding box of every non-black pixel; padding must be pure black.</summary>
    private static (int X, int Y, int Width, int Height) ContentBounds(byte[] png)
    {
        using var image = Image.Load<Rgba32>(png);
        var minX = image.Width;
        var minY = image.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (pixel.R == 0 && pixel.G == 0 && pixel.B == 0)
                {
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }
        }

        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
