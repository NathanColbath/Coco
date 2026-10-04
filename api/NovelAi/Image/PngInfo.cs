using System.Buffers.Binary;
using System.Text;

namespace ComicStudio.Api.NovelAi.Image;

/// <summary>
/// Minimal PNG reader: dimensions and the text chunk NovelAI uses for metadata.
/// Deliberately dependency-free so the image pipeline never depends on a native
/// graphics library.
/// </summary>
public static class PngInfo
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsPng(byte[] bytes) =>
        bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(Signature);

    public static (int Width, int Height) ReadDimensions(byte[] bytes)
    {
        // IHDR is the first chunk: 8 byte signature, 4 length, 4 type, then data.
        if (bytes.Length < 24 || !IsPng(bytes))
        {
            return (0, 0);
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        return (width, height);
    }

    /// <summary>Reads the <c>Comment</c>/<c>Description</c> tEXt chunk when present.</summary>
    public static string? ReadComment(byte[] bytes)
    {
        if (!IsPng(bytes))
        {
            return null;
        }

        var offset = 8;

        while (offset + 8 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length < 0 || offset + 12 + length > bytes.Length)
            {
                break;
            }

            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (type is "tEXt" or "iTXt")
            {
                var data = bytes.AsSpan(offset + 8, length);
                var separator = data.IndexOf((byte)0);
                if (separator > 0)
                {
                    var keyword = Encoding.ASCII.GetString(data[..separator]);
                    if (keyword is "Comment" or "Description")
                    {
                        var value = data[(separator + 1)..];
                        // iTXt has extra fields before the text; fall back to the tail.
                        var text = keyword == "Comment"
                            ? Encoding.UTF8.GetString(value)
                            : Encoding.UTF8.GetString(value);
                        return text.Trim();
                    }
                }
            }

            if (type == "IEND")
            {
                break;
            }

            offset += 12 + length;
        }

        return null;
    }
}
