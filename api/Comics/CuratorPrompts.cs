namespace ComicStudio.Api.Comics;

/// <summary>
/// The curator's instructions. The model is an adaptation editor, never a prompt
/// writer: it describes what happens and our composer decides how to ask for it.
/// Prompts live here so they can be tuned without touching logic.
/// </summary>
public static class CuratorPrompts
{
    public const string SharedRules =
        """
        You are a manga adaptation editor. You turn prose fiction into a production
        plan that artists and image models can execute.

        Rules:
        - Reply with JSON only. No prose, no explanations, no code fences.
        - Match the requested schema exactly, including property names.
        - Only use characters, places and objects that appear in the text.
        - Manga reads right to left and top to bottom.
        - Prefer showing over telling: a panel is a drawing, never a caption.
        - Keep dialogue short enough to fit in a speech bubble.
        """;

    public static string System(string extra) => $"{SharedRules}\n\n{extra}";

    /// <summary>
    /// One pass over the story that returns both the cast and the places. They
    /// were two calls reading the same chunk; the answers do not depend on each
    /// other, so a single call asks for both.
    /// </summary>
    public static string StoryDiscovery(
        string storyText,
        string knownCharacters = "(none yet)",
        string knownEnvironments = "(none yet)"
    ) =>
        $$"""
        Read the story once and list both the cast and the places worth drawing.

        Characters — for each character provide:
          name, aliases (array), role (Protagonist|Antagonist|Supporting|Background),
          bio (1-2 sentences), personality, speechStyle, appearance (1-2 sentences),
          promptTags (comma separated image tags describing them, e.g.
            "1girl, long auburn hair, green eyes, freckles, slim"),
          outfitTags (comma separated tags for what they usually wear),
          hairColor, eyeColor

        Skip crowds and unnamed people. Use consistent visual tags across the cast:
        start with "1girl", "1boy" or "1other", then hair, eyes, build, and one
        distinguishing feature.

        Places — for each place provide:
          name (short and stable, e.g. "Rain-soaked alley"),
          aliases (array of other names the story uses for this place),
          kind (Exterior|Interior|Landscape|Vehicle|Abstract),
          visualTags (comma separated danbooru-style tags describing the place,
            e.g. "alley, brick wall, fire escape, puddle, night"),
          lighting (time of day, weather and the light sources in the scene),
          paletteNotes (black-and-white value guidance — this comic is
            monochrome, so describe values and contrast, not colour),
          features (recurring objects that anchor the place, one per line),
          notes (anything else that must stay consistent when we return),
          firstSeenPage (1 based page number where the place first appears)

        Include rooms, buildings, streets, landscapes and vehicles.

        Characters already in the bible (reuse their exact names, do not redefine):
        {{knownCharacters}}

        Places already in the bible (reuse their exact names, do not redefine):
        {{knownEnvironments}}

        Story:
        ---
        {{storyText}}
        ---

        Reply with: { "characters": [ ... ], "environments": [ ... ] }
        """;

    public static string SceneSegmentation(string storyText) =>
        $$"""
        Divide the story into scenes. A scene is a continuous stretch of time and
        place. Most short stories have between two and eight scenes.

        For each scene provide:
          index (0 based, in reading order), title (short), summary (1-2 sentences),
          location, timeOfDay, mood, cast (array of character names present),
          startExcerpt (the first 8 words of the scene, verbatim),
          endExcerpt (the last 8 words of the scene, verbatim)

        Story:
        ---
        {{storyText}}
        ---

        Reply with: { "scenes": [ ... ] }
        """;

    public static string PanelBreakdown(
        string sceneJson,
        string characters,
        string dialogueRule,
        int suggestedPanels,
        string readingDirection,
        string? contentNote = null
    ) =>
        $$"""
        Break this scene into manga panels. Produce about {{suggestedPanels}} panels.

        For each panel provide:
          index (0 based, in {{readingDirection}} reading order),
          shot (Establishing|Wide|Medium|CloseUp|ExtremeCloseUp|OverShoulder|Insert|Splash|DutchAngle),
          action (a concrete drawn moment, under 20 words),
          importance ("high" for a splash-worthy beat, otherwise "normal"),
          continuity (a fact the drawing must honour, e.g. "lantern is unlit"),
          cast: [ { name, expression, pose, position (left|center|right|background),
                    prominence (foreground|midground|background) } ],
          dialogue: [ { kind (Speech|Thought|Narration|Sfx), speaker, text } ]

        Be terse. Do not restate the scene's place, time or mood — those are
        inherited. Keep each line of dialogue under 15 words. Narration is rare:
        at most one per page, and only when the art cannot carry the information.

        Dialogue rule: {{dialogueRule}}

        {{contentNote}}

        Characters available:
        {{characters}}

        Scene:
        {{sceneJson}}

        Reply with: { "panels": [ ... ] }
        """;

    /// <summary>
    /// Divides the story into scenes and decides how many pages each deserves.
    /// Scene boundaries are where a comic is allowed to cut, so getting them
    /// right is what lets pages inside a scene flow into one another.
    /// </summary>
    public static string ScenePlan(string storyText, int totalPages, string environments) =>
        $$"""
        Divide the story below into scenes, and decide how many comic pages each
        deserves. The whole story gets about {{totalPages}} pages.

        For each scene provide:
          index (0 based, in reading order), title, summary (1-2 sentences),
          environment (exactly one name from the list below),
          timeOfDay, mood, purpose (the dramatic job this scene does),
          cast (character names present),
          pageCount (how many pages this scene needs — an action scene earns more
                     than a transition, and a scene carrying little story can be one)

        The pageCount values should add up to roughly {{totalPages}}.

        Environments available:
        {{environments}}

        Story:
        ---
        {{storyText}}
        ---

        Reply with: { "scenes": [ ... ] }
        """;

    /// <summary>
    /// Scripts pages inside one scene.
    ///
    /// Everything that creates consistency lives here: the art style so the
    /// writing suits it, the value anchoring so monochrome stays readable, the
    /// environment canon so a revisited place looks the same, the scene's own
    /// state, and the previous pages so this one continues rather than restarts.
    /// </summary>
    public static string PageScript(
        int firstPage,
        int lastPage,
        string sceneJson,
        string characters,
        string environmentCanon,
        string previousPages,
        string? continuity,
        string styleSpec,
        string palette,
        string contentGuidance
    ) =>
        $$"""
        You are scripting pages {{firstPage}} to {{lastPage}} of a manga. These pages are
        all part of ONE scene, so they must read as a continuous moment — do not
        re-establish what the reader has already seen, and do not restart the
        action. Continue directly.

        The art is:
        {{styleSpec}}

        Values in this comic:
        {{palette}}

        Scene:
        {{sceneJson}}

        Cast (use these exact names):
        {{characters}}

        This place (use it so a revisited location looks the same):
        {{environmentCanon}}

        The pages immediately before these ended like this:
        {{previousPages}}

        Continuity entering these pages — do not contradict any of it:
        {{continuity ?? "(this is the opening of the scene; establish it)"}}

        {{contentGuidance}}

        For each page provide:

          index (starting at {{firstPage}}),
          prompt — one or two sentences stating the moment. The panels carry the
            detail, so this sets the scene rather than describing the drawing.
          environment — the name of the place (or a different one if the page moves),
          movesTo — only when the page physically moves somewhere else,
          panels — in reading order, each with:
            order, framing (establishing|wide|medium|close|extreme close|insert|over-shoulder),
            action (what is happening), background (what is visible behind),
            cast (name, pose, expression, facing, prominence),
            props (objects that matter in this panel)
          bubbles — every piece of text on the page:
            kind (Speech|Thought|Narration|Sfx), speaker, text, position
            (upper left|upper right|middle left|middle right|lower left|lower right).
            Text must be under 60 characters. Compress anything longer — it is
            drawn, not typeset, and long lines come out unreadable.
          continuity — ONE STRING (not an object), with these exact headings each
            on its own line, keeping everything still true and updating what changed:

            LOCATION   where, time, weather
            CAST       each character present: wardrobe, condition, injuries, mood
            PROPS      objects that matter and their state
            THREADS    unresolved things the reader is waiting on
            PALETTE    the light and value scheme right now
            LAST BEAT  one line: the last thing the reader saw

        Vary the rhythm. Not every panel is the same distance. Open the scene with
        an establishing panel, then work closer as the beat develops.

        Reply with: { "pages": [ ... ] }
        """;

    /// <summary>
    /// The seam pass. Receives only each page's opening and closing line plus its
    /// last beat, and returns corrections for the pages that do not connect.
    /// </summary>
    public static string SeamFix(string seams) =>
        $$"""
        Below is the running order of a manga: for each page, its opening line,
        its closing line and the last thing the reader saw.

        Find the pages that do not connect to the page before them — a jump in
        place, time, position, or an action that does not follow from the previous
        image. Ignore anything that reads cleanly.

        For each page that needs fixing, return its index and a rewritten opening
        line that follows from the page before. Only the opening line.

        If every page connects, return an empty list.

        Pages:
        {{seams}}

        Reply with: { "fixes": [ { "index": 0, "opening": "..." } ] }
        """;

    public static string Cover(string title, string premise, string characters) =>
        $$"""
        Design a manga cover illustration.

        Reply with JSON only:
          prompt (comma separated image tags for the cover art),
          negative (comma separated tags to avoid)

        Show the lead character or characters in a striking pose that implies the
        story's tone, leaving space near the top for the title. Do not include any
        text, lettering or logos — the title is added afterwards.

        Title: {{title}}
        Premise: {{premise}}
        Characters: {{characters}}

        Reply with: { "prompt": "...", "negative": "..." }
        """;
}
