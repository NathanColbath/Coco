using System.Text;
using ComicStudio.Api.Domain;
using ComicStudio.Api.NovelAi.Image;

namespace ComicStudio.Api.Comics;

public record ComposedPage(
    string Positive,
    string Negative,
    IReadOnlyList<CharacterPrompt> Characters,
    int Width,
    int Height,
    int Steps,
    double Scale,
    string Sampler,
    string NoiseSchedule,
    int UcPreset,
    bool QualityToggle,
    long Seed,
    /// <summary>True when the page carries lettering, which decides the model under the Auto policy.</summary>
    bool HasText,
    IReadOnlyList<string> Notes
);

/// <summary>
/// Turns a page's script into one image request.
///
/// The lettering is drawn by the model, not typeset on top, so every bubble is
/// phrased into the prompt with its exact words. That changes the negatives
/// fundamentally: the model must be allowed — required — to write.
/// </summary>
public class PagePromptComposer
{
    private const string PageStructureTags =
        "manga page, comic page, multiple panels, panel layout, black and white, "
        + "high contrast, screentone, lineart";

    /// <summary>Tags that would stop the page being a page.</summary>
    private static readonly string[] PageConflicts =
    [
        "panel border",
        "speech bubble",
        "comic page",
        "4koma",
        "multiple views",
        "1panel",
    ];

    /// <summary>
    /// Tags that would stop the lettering being drawn. These are stripped from
    /// the preset negatives, because asking for text and forbidding it in the
    /// same request produces blank or garbled bubbles.
    /// </summary>
    private static readonly string[] TextConflicts = ["text", "english text", "letterboxing"];

    public ComposedPage Compose(
        ComicPage page,
        ComicProject project,
        IReadOnlyList<Character> cast,
        long? forcedSeed = null,
        bool allowPaidQuality = true
    )
    {
        var style = project.Style;

        var positive = new StringBuilder();
        positive.Append(style.BaseTags).Append(", ").Append(PageStructureTags);

        if (!string.IsNullOrWhiteSpace(page.Script))
        {
            positive.Append(", ").Append(page.Script.Trim());
        }

        AppendContinuity(positive, page);
        AppendPalette(positive, project);

        var lines = page.Dialogue.OrderBy(line => line.Index).ToList();
        var hasText = lines.Any(line => !string.IsNullOrWhiteSpace(line.Text));
        AppendLettering(positive, lines, cast);

        var onPage = OnPageCharacters(page, cast, lines);
        var characters = BuildCharacters(onPage);
        AppendValues(positive, onPage);

        var negative = ContentModifiers.ApplyToNegative(BuildNegative(style), project.Rating);
        var finalPositive = ContentModifiers.ApplyToPositive(positive.ToString(), project.Rating);

        var print = ResolutionFor(project, allowPaidQuality);
        var steps = ResolutionPolicy.StepsFor(allowPaidQuality, style.Steps);
        var notes = BuildNotes(page, project, allowPaidQuality).ToList();

        return new ComposedPage(
            finalPositive,
            negative,
            characters,
            print.Width,
            print.Height,
            steps,
            style.Scale,
            style.Sampler,
            style.NoiseSchedule,
            style.UcPreset,
            style.QualityToggle,
            forcedSeed ?? style.Seed ?? Random.Shared.NextInt64(0, 4_294_967_295),
            hasText,
            notes
        );
    }

    /// <summary>
    /// The plan for one page without building the prompt: steps, lettering,
    /// rating fit, the model policy, and — once the runner has resolved them —
    /// the exact model and reference count. The workspace reads these back so
    /// the cost is visible before anything is spent.
    /// </summary>
    public IReadOnlyList<string> BuildNotes(
        ComicPage page,
        ComicProject project,
        bool allowPaidQuality = true,
        string? resolvedModel = null,
        int? referenceCount = null
    )
    {
        var style = project.Style;
        var notes = new List<string>();
        var hasText = page.Dialogue.Any(line => !string.IsNullOrWhiteSpace(line.Text));

        if (!allowPaidQuality && style.Steps > ResolutionPolicy.FreeStepLimit)
        {
            notes.Add(
                $"Steps reduced to {ResolutionPolicy.StepsFor(allowPaidQuality, style.Steps)} "
                + "to stay inside free generations."
            );
        }

        if (hasText)
        {
            notes.Add("Lettering is drawn by the model, so this page needs a text-capable model.");
        }

        if (ContentModifiers.ModelFightsRating(style.ModelId, project.Rating))
        {
            notes.Add(
                "This project is adult-rated but the style preset uses a Curated model, "
                + "which is safety tuned and will resist it. Switch to a Full model."
            );
        }

        notes.Add(DescribeModelPolicy(project, page, hasText));

        if (!string.IsNullOrWhiteSpace(resolvedModel))
        {
            notes.Add($"will use {resolvedModel}");
        }

        if (referenceCount is { } count)
        {
            notes.Add(count == 1 ? "1 reference attached" : $"{count} references attached");
        }

        var print = ResolutionFor(project, allowPaidQuality);
        notes.Add($"page of {print.Width}×{print.Height}");

        return notes;
    }

    /// <summary>Which family the page's model policy will ask for.</summary>
    private static string DescribeModelPolicy(ComicProject project, ComicPage page, bool hasText)
    {
        if (!string.IsNullOrWhiteSpace(page.ModelOverride))
        {
            return $"Page model override: {page.ModelOverride.Trim()}.";
        }

        return project.ImageModelPolicy switch
        {
            ImageModelPolicy.AlwaysV5 => "Model policy: always V5.",
            ImageModelPolicy.AlwaysV4Point5 => "Model policy: always V4.5.",
            ImageModelPolicy.Explicit when !string.IsNullOrWhiteSpace(project.ExplicitImageModelId) =>
                $"Model policy: explicit model {project.ExplicitImageModelId.Trim()}.",
            _ => hasText
                ? "Model policy: Auto — this page has lettering, so V5."
                : "Model policy: Auto — no lettering, so V4.5.",
        };
    }

    /// <summary>
    /// The project's greyscale palette and default environment language,
    /// appended as one clause so every page inherits the same values.
    /// </summary>
    private static void AppendPalette(StringBuilder builder, ComicProject project)
    {
        var palette = Join(project.PaletteNotes, project.EnvironmentDefaults);
        if (palette.Length > 0)
        {
            builder.Append(", palette: ").Append(palette);
        }
    }

    /// <summary>
    /// Monochrome consistency is a value problem, not a hue problem: each named
    /// character gets a value line so the model keeps their greys stable.
    /// </summary>
    private static void AppendValues(StringBuilder builder, IReadOnlyList<Character> onPage)
    {
        var lines = onPage.Select(ValueLine).Where(line => line is not null).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        builder.Append(", values: ").Append(string.Join(" | ", lines));
    }

    private static string? ValueLine(Character character)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(character.Visual.HairColor))
        {
            parts.Add($"hair reads as {character.Visual.HairColor.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(character.Visual.EyeColor))
        {
            parts.Add($"eyes as {character.Visual.EyeColor.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(character.Visual.Notes))
        {
            parts.Add(character.Visual.Notes.Trim());
        }

        return parts.Count == 0 ? null : $"{character.Name}: {string.Join("; ", parts)}";
    }

    /// <summary>
    /// The state carried into this page. Repeating it every page is what makes
    /// consecutive pages look like one continuous comic.
    /// </summary>
    private static void AppendContinuity(StringBuilder builder, ComicPage page)
    {
        if (!string.IsNullOrWhiteSpace(page.Continuity))
        {
            builder.Append(", consistent with the ongoing scene: ").Append(page.Continuity.Trim());
        }
    }

    /// <summary>Bubbles, with the words they must contain.</summary>
    private static void AppendLettering(
        StringBuilder builder,
        IReadOnlyList<DialogueLine> lines,
        IReadOnlyList<Character> cast
    )
    {
        foreach (var line in lines)
        {
            var text = line.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var quoted = $"\"{text}\"";
            var where = string.IsNullOrWhiteSpace(line.Position) ? string.Empty : $" in the {line.Position}";
            var speaker = cast.FirstOrDefault(item => item.Id == line.SpeakerCharacterId)?.Name;

            var phrase = line.Kind switch
            {
                DialogueKind.Thought => $"thought bubble containing {quoted}",
                DialogueKind.Narration => $"narrator caption box reading {quoted}",
                DialogueKind.Sfx => $"sound effect lettering reading {quoted}",
                _ => $"speech bubble reading {quoted}",
            };

            builder.Append($", {phrase}{where}");

            if (!string.IsNullOrWhiteSpace(speaker) && line.Kind == DialogueKind.Speech)
            {
                builder.Append($", spoken by {speaker}");
            }
        }
    }

    private static string BuildNegative(ComicStylePreset style)
    {
        var parts = (style.NegativeTags ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => !PageConflicts.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Where(tag => !TextConflicts.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();

        parts.Add("watermark");
        parts.Add("signature");

        return string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Everyone the page's script or dialogue actually names, capped at the
    /// same six their captions allow. Both the character captions and the
    /// value lines are built from this one set.
    /// </summary>
    private static List<Character> OnPageCharacters(
        ComicPage page,
        IReadOnlyList<Character> cast,
        IReadOnlyList<DialogueLine> lines
    )
    {
        var script = page.Script ?? string.Empty;
        var speakers = lines
            .Where(line => line.SpeakerCharacterId is not null)
            .Select(line => line.SpeakerCharacterId!.Value)
            .ToHashSet();

        return cast
            .Where(character =>
                speakers.Contains(character.Id)
                || script.Contains(character.Name, StringComparison.OrdinalIgnoreCase)
            )
            .Take(6)
            .ToList();
    }

    /// <summary>
    /// Character captions for whoever appears on this page, without coordinates:
    /// a page holds several panels, so pinning a character to one point would
    /// fight the layout.
    /// </summary>
    private static List<CharacterPrompt> BuildCharacters(IReadOnlyList<Character> onPage) =>
        onPage
            .Select(character => new CharacterPrompt(
                Join(character.Visual.PromptTags, character.Visual.OutfitTags),
                0.5,
                0.5,
                character.Visual.NegativeTags
            ))
            .Where(prompt => !string.IsNullOrWhiteSpace(prompt.Tags))
            .ToList();

    /// <summary>Page-sized output: the same shape as the page canvas.</summary>
    public static (int Width, int Height) ResolutionFor(ComicProject project, bool allowPaidQuality)
    {
        var aspect = project.PageHeight <= 0 ? 0.707 : (double)project.PageWidth / project.PageHeight;

        return ResolutionPolicy.ForAspect(
            aspect,
            allowPaidQuality ? ResolutionPolicy.MaxPixelBudget : ResolutionPolicy.FreePixelBudget
        );
    }

    private static string Join(params string?[] values) =>
        string.Join(
            ", ",
            values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim())
        );
}
