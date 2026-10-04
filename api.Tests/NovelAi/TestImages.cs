using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ComicStudio.Api.Tests.NovelAi;

/// <summary>Synthetic in-memory PNGs for the reference normaliser tests.</summary>
internal static class TestImages
{
    public static byte[] Png(int width, int height, byte r = 200, byte g = 30, byte b = 30)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(r, g, b));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
