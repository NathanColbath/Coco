namespace ComicStudio.Api.Comics;

/// <summary>
/// Turns the curator's loose placement words into frame coordinates. Keeping
/// this here (rather than in the prompt) means positions stay stable even when
/// the model is vague.
/// </summary>
public static class FrameMath
{
    public static (double X, double Y) Place(
        string? position,
        string? prominence,
        int index,
        int count
    )
    {
        var x = PlaceX(position, index, count);
        var y = prominence?.ToLowerInvariant() switch
        {
            "foreground" => 0.70,
            "background" => 0.36,
            _ => 0.55,
        };

        return (Clamp(x), Clamp(y));
    }

    private static double PlaceX(string? position, int index, int count)
    {
        var value = position?.ToLowerInvariant() ?? string.Empty;

        // Check the side before "center" so "center-left" reads as left.
        if (value.Contains("left"))
        {
            return 0.30;
        }

        if (value.Contains("right"))
        {
            return 0.70;
        }

        if (value.Contains("center") || value.Contains("middle"))
        {
            return 0.50;
        }

        // Unspecified: spread the cast across the frame in a stable order.
        return count <= 1 ? 0.5 : 0.28 + (0.44 * index / (count - 1));
    }

    /// <summary>Bubbles stack downward from the top-right, the manga convention.</summary>
    public static (double X, double Y) BubblePlace(int index, int count)
    {
        var spacing = count <= 3 ? 0.16 : 0.12;
        var y = 0.10 + (index * spacing);
        return (0.60, Math.Clamp(y, 0.06, 0.80));
    }

    private static double Clamp(double value) => Math.Clamp(value, 0.04, 0.96);
}
