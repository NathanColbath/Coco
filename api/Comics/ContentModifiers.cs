using ComicStudio.Api.Domain;

namespace ComicStudio.Api.Comics;

/// <summary>
/// Applies a project's content rating to generated prompts.
///
/// This is not a single toggle: at <see cref="ContentRating.General"/> the safety
/// tags are added, and above that they are actively <em>removed</em>, because a
/// style preset that says "nsfw" in its negatives would otherwise silently
/// suppress exactly what was asked for. Censoring tags go the other way — an
/// adult page should not come back with a mosaic bar.
/// </summary>
public static class ContentModifiers
{
    /// <summary>Excluded from General requests, and stripped from adult ones.</summary>
    private static readonly string[] SafetyTags =
    [
        "nsfw",
        "nude",
        "nudity",
        "explicit",
        "sex",
        "sexual content",
        "suggestive",
        "ecchi",
        "large breasts",
    ];

    /// <summary>Applied to adult requests so the output is not obscured.</summary>
    private static readonly string[] UncensoredTags = ["uncensored", "no mosaic"];

    /// <summary>Excluded from adult requests so the model does not cover things up.</summary>
    private static readonly string[] CensoringTags = ["censored", "mosaic censoring", "bar censor"];

    public static string ApplyToPositive(string caption, ContentRating rating) =>
        rating switch
        {
            ContentRating.General => caption,
            _ => Append(caption, UncensoredTags),
        };

    public static string ApplyToNegative(string negative, ContentRating rating)
    {
        var parts = Split(negative);

        return rating switch
        {
            // Everything adult is pushed away.
            ContentRating.General => Join(parts.Concat(SafetyTags)),

            // Adult is allowed, so anything blocking or hiding it is removed.
            _ => Join(
                parts
                    .Where(tag => !SafetyTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    .Where(tag => !CensoringTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    .Concat(CensoringTags)
            ),
        };
    }

    /// <summary>
    /// Extra direction for the curator. NovelAI's text models are uncensored, but
    /// they will still soften a scene unless told otherwise.
    /// </summary>
    public static string? CuratorNote(ContentRating rating) =>
        rating switch
        {
            ContentRating.General =>
                "Keep the plan suitable for a general audience.",
            ContentRating.Mature =>
                "This is an adult story. Describe suggestive moments faithfully without "
                + "softening them, staying within what a panel can show.",
            ContentRating.Explicit =>
                "This is an explicit adult story. Do not sanitise or fade to black: "
                + "describe what happens plainly, in terms of what is drawn.",
            _ => null,
        };

    /// <summary>
    /// True when the model will resist the project's rating. NovelAI's Curated
    /// variants are safety tuned, so adult work should use a Full model.
    /// </summary>
    public static bool ModelFightsRating(string? modelId, ContentRating rating) =>
        rating != ContentRating.General
        && modelId is not null
        && modelId.Contains("curated", StringComparison.OrdinalIgnoreCase);

    private static List<string> Split(string? value) =>
        (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static string Join(IEnumerable<string> tags) =>
        string.Join(", ", tags.Distinct(StringComparer.OrdinalIgnoreCase));

    private static string Append(string caption, IEnumerable<string> tags)
    {
        var parts = Split(caption);
        parts.AddRange(tags);
        return Join(parts);
    }
}
