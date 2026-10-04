namespace ComicStudio.Api.NovelAi.Image;

/// <summary>
/// Chooses the largest legal image size for a panel's shape.
///
/// Two constraints matter: NovelAI requires width and height to be multiples of
/// 64, and Opus accounts get free generations only at or below 1,048,576 pixels
/// with at most 28 steps. Staying inside that envelope is what makes a
/// hundred-panel book affordable, so it is the default.
/// </summary>
public static class ResolutionPolicy
{
    public const int SizeStep = 64;
    public const int MaxDimension = 2048;

    /// <summary>Opus free-generation ceiling.</summary>
    public const int FreePixelBudget = 1_048_576;

    /// <summary>NovelAI's absolute ceiling for a single image.</summary>
    public const int MaxPixelBudget = 3_145_728;

    public const int FreeStepLimit = 28;

    public static (int Width, int Height) ForAspect(
        double aspectRatio,
        int pixelBudget = FreePixelBudget
    )
    {
        if (aspectRatio <= 0 || double.IsNaN(aspectRatio))
        {
            aspectRatio = 1;
        }

        pixelBudget = Math.Clamp(pixelBudget, SizeStep * SizeStep, MaxPixelBudget);

        var best = (Width: SizeStep, Height: SizeStep);
        var bestPixels = 0;
        var bestError = double.MaxValue;

        for (var height = SizeStep; height <= MaxDimension; height += SizeStep)
        {
            var idealWidth = height * aspectRatio;
            var width = (int)Math.Round(idealWidth / SizeStep) * SizeStep;
            width = Math.Clamp(width, SizeStep, MaxDimension);

            var pixels = width * height;
            if (pixels > pixelBudget)
            {
                continue;
            }

            var error = Math.Abs((double)width / height - aspectRatio);

            // Prefer more pixels; break ties on shape accuracy.
            if (pixels > bestPixels || (pixels == bestPixels && error < bestError))
            {
                best = (width, height);
                bestPixels = pixels;
                bestError = error;
            }
        }

        return best;
    }

    /// <summary>The aspect ratio a panel rect occupies on the page canvas.</summary>
    public static double AspectForPanel(
        double panelWidth,
        double panelHeight,
        int pageWidth,
        int pageHeight
    )
    {
        var width = panelWidth * pageWidth;
        var height = panelHeight * pageHeight;
        return height <= 0 ? 1 : width / height;
    }

    public static bool QualifiesForFreeGeneration(int width, int height, int steps) =>
        (long)width * height <= FreePixelBudget && steps <= FreeStepLimit;

    public static int StepsFor(bool allowPaidQuality, int requestedSteps) =>
        allowPaidQuality ? requestedSteps : Math.Min(requestedSteps, FreeStepLimit);
}
