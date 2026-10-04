using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComicStudio.Api.Comics;

// The shapes the curator must produce. Property names are deliberately simple
// because the model is asked to match them exactly.

/// <summary>
/// The continuity ledger is asked for as named sections, so a model returns it as
/// an object about as often as it returns a flat string. Both mean the same thing,
/// and everything downstream — the seam pass's <c>LAST BEAT</c> lookup, the
/// workspace's section rendering — reads the canonical text form with the headings
/// on their own lines. Normalise here rather than throwing away a whole page over
/// a shape the prompt itself invited.
/// </summary>
public class LedgerTextConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => ReadValue(ref reader, options);

    private static string? ReadValue(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartArray => ReadArray(ref reader, options),
            JsonTokenType.StartObject => ReadObject(ref reader, options),
            _ => Skip(ref reader),
        };

    private static string? Skip(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }

    private static string? ReadArray(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        string separator = "\n"
    )
    {
        var parts = new List<string>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var part = ReadValue(ref reader, options);
            if (!string.IsNullOrWhiteSpace(part))
            {
                parts.Add(part.Trim());
            }
        }

        return parts.Count == 0 ? null : string.Join(separator, parts);
    }

    private static string? ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var builder = new StringBuilder();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                break;
            }

            var value = Scalar(ref reader, options);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            // Upper-cased and leading, so the seam pass's LAST BEAT lookup and the
            // workspace's section splitter both still find their headings.
            builder.Append(name.Trim().ToUpperInvariant()).Append("  ").Append(value.Trim());
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string Scalar(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Null => string.Empty,
            // A list inside a section belongs on that section's line; only a
            // top-level array is a sequence of ledger lines.
            JsonTokenType.StartArray => ReadArray(ref reader, options, "; ") ?? string.Empty,
            JsonTokenType.StartObject => ReadObject(ref reader, options) ?? string.Empty,
            JsonTokenType.Number => reader.TryGetInt64(out var integer)
                ? integer.ToString()
                : reader.GetDouble().ToString(),
            JsonTokenType.True => bool.TrueString,
            JsonTokenType.False => bool.FalseString,
            _ => Skip(ref reader) ?? string.Empty,
        };

    public override void Write(
        Utf8JsonWriter writer,
        string? value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value);
}

public class CastDiscoveryResult
{
    public List<DiscoveredCharacter> Characters { get; set; } = [];
}

/// <summary>
/// The cast and the places from one pass over the story. They used to be two
/// calls with the same prose; the answers do not depend on each other, so one
/// prompt can ask for both.
/// </summary>
public class StoryDiscoveryResult
{
    public List<DiscoveredCharacter> Characters { get; set; } = [];

    public List<DiscoveredEnvironment> Environments { get; set; } = [];
}

public class DiscoveredCharacter
{
    public string Name { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];

    /// <summary>Protagonist, Antagonist, Supporting or Background.</summary>
    public string Role { get; set; } = "Supporting";

    public string? Bio { get; set; }

    public string? Personality { get; set; }

    public string? SpeechStyle { get; set; }

    /// <summary>One or two sentences describing how they look.</summary>
    public string? Appearance { get; set; }

    /// <summary>Danbooru-style visual tags used as the character's canon fragment.</summary>
    public string? PromptTags { get; set; }

    public string? OutfitTags { get; set; }

    public string? HairColor { get; set; }

    public string? EyeColor { get; set; }
}

public class EnvironmentDiscoveryResult
{
    public List<DiscoveredEnvironment> Environments { get; set; } = [];
}

public class DiscoveredEnvironment
{
    public string Name { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];

    /// <summary>Exterior, Interior, Landscape, Vehicle or Abstract.</summary>
    public string Kind { get; set; } = "Exterior";

    /// <summary>Danbooru-style tags describing the place.</summary>
    public string? VisualTags { get; set; }

    /// <summary>Time of day, weather and light sources.</summary>
    public string? Lighting { get; set; }

    /// <summary>Black-and-white value guidance; this comic is monochrome.</summary>
    public string? PaletteNotes { get; set; }

    /// <summary>Recurring objects that anchor the place, one per line.</summary>
    public string? Features { get; set; }

    public string? Notes { get; set; }

    /// <summary>Page where the place first appears, when the model can tell.</summary>
    public int? FirstSeenPage { get; set; }
}

public class SceneSegmentationResult
{
    public List<DiscoveredScene> Scenes { get; set; } = [];
}

public class DiscoveredScene
{
    public int Index { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? Location { get; set; }

    public string? TimeOfDay { get; set; }

    public string? Mood { get; set; }

    public List<string> Cast { get; set; } = [];

    /// <summary>First few words of the scene, used to anchor it in the prose.</summary>
    public string? StartExcerpt { get; set; }

    public string? EndExcerpt { get; set; }
}

public class ScenePanelPlan
{
    public List<PlannedPanel> Panels { get; set; } = [];
}

public class PlannedPanel
{
    public int Index { get; set; }

    /// <summary>Establishing, Wide, Medium, CloseUp, ExtremeCloseUp, OverShoulder, Insert, Splash or DutchAngle.</summary>
    public string Shot { get; set; } = "Medium";

    public string? Action { get; set; }

    public string? Setting { get; set; }

    public string? TimeOfDay { get; set; }

    public string? Mood { get; set; }

    public string? CameraNotes { get; set; }

    /// <summary>"high" reserves this panel its own page.</summary>
    public string Importance { get; set; } = "normal";

    [JsonConverter(typeof(LedgerTextConverter))]
    public string? Continuity { get; set; }

    public List<PlannedCast> Cast { get; set; } = [];

    public List<PlannedDialogue> Dialogue { get; set; } = [];
}

public class PlannedCast
{
    public string Name { get; set; } = string.Empty;

    public string? Expression { get; set; }

    public string? Pose { get; set; }

    /// <summary>left, center-right, background… used to place them in the frame.</summary>
    public string? Position { get; set; }

    /// <summary>foreground, midground or background.</summary>
    public string? Prominence { get; set; }
}

public class PlannedDialogue
{
    /// <summary>Speech, Thought, Narration or Sfx.</summary>
    public string Kind { get; set; } = "Speech";

    public string? Speaker { get; set; }

    public string Text { get; set; } = string.Empty;
}

// --- Page scripting ---------------------------------------------------------
// The page is the unit of generation: the curator writes one rich description
// per page plus the bubble text, and the continuity it leaves behind.

public class PageScriptResult
{
    public List<ScriptedPage> Pages { get; set; } = [];
}

public class ScriptedPage
{
    public int Index { get; set; }

    /// <summary>
    /// One or two sentences of scene-setting. The panels carry the detail, so
    /// this exists to state the moment, not to describe the drawing.
    /// </summary>
    public string Prompt { get; set; } = string.Empty;

    public string? SceneTitle { get; set; }

    /// <summary>Name of the environment this page is set in.</summary>
    public string? Environment { get; set; }

    /// <summary>Set when the page moves somewhere else partway through.</summary>
    public string? MovesTo { get; set; }

    /// <summary>
    /// What is drawn, in reading order. Structured because pose and expression
    /// detail is exactly what a paragraph quietly drops.
    /// </summary>
    public List<ScriptedPanel> Panels { get; set; } = [];

    /// <summary>State carried out of this page into every later page.</summary>
    [JsonConverter(typeof(LedgerTextConverter))]
    public string? Continuity { get; set; }

    public List<ScriptedBubble> Bubbles { get; set; } = [];
}

public class ScriptedPanel
{
    public int Order { get; set; }

    /// <summary>establishing, wide, medium, close, extreme close, insert, over-shoulder.</summary>
    public string? Framing { get; set; }

    /// <summary>What is happening in this panel.</summary>
    public string? Action { get; set; }

    /// <summary>What is visible behind the cast.</summary>
    public string? Background { get; set; }

    public List<ScriptedPose> Cast { get; set; } = [];

    public List<string> Props { get; set; } = [];
}

public class ScriptedPose
{
    public string Name { get; set; } = string.Empty;

    public string? Pose { get; set; }

    public string? Expression { get; set; }

    /// <summary>Which way they are facing or looking.</summary>
    public string? Facing { get; set; }

    /// <summary>foreground, midground or background.</summary>
    public string? Prominence { get; set; }
}

/// <summary>A scene and the pages it will occupy.</summary>
public class ScriptedScene
{
    public int Index { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string? Environment { get; set; }

    public string? TimeOfDay { get; set; }

    public string? Mood { get; set; }

    /// <summary>The dramatic job of this scene.</summary>
    public string? Purpose { get; set; }

    public List<string> Cast { get; set; } = [];

    /// <summary>How many pages this scene deserves.</summary>
    public int PageCount { get; set; } = 1;
}

public class ScenePlanResult
{
    public List<ScriptedScene> Scenes { get; set; } = [];
}

public class ScriptedBubble
{
    public string Kind { get; set; } = "Speech";

    public string? Speaker { get; set; }

    public string Text { get; set; } = string.Empty;

    public string? Position { get; set; }
}

/// <summary>A rewritten opening line for a page that did not connect.</summary>
public class SeamFixResult
{
    public List<SeamFix> Fixes { get; set; } = [];
}

public class SeamFix
{
    public int Index { get; set; }

    public string? Opening { get; set; }
}
