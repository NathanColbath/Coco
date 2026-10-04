using ComicStudio.Api.Domain;

namespace ComicStudio.Api.Comics;

/// <summary>
/// Vocabulary the curator is allowed to use, by rating.
///
/// The image models respond to danbooru-style tags far better than to prose, and
/// a text model will otherwise write around adult content rather than describe
/// it. This supplies the actual words.
///
/// Kept as code rather than a JSON file for now: there is no runtime IO, nothing
/// can fail to load, and it is covered by tests. Moving it to a data file later
/// is a small change if it needs to grow independently of releases.
/// </summary>
public static class ContentTagLibrary
{
    private const string Anatomy =
        "anatomy: breasts, nipples, large breasts, small breasts, wide hips, "
        + "thighs, navel, collarbone, sweat";

    private const string State =
        "state: nude, topless, bottomless, partially undressed, undressed, "
        + "wet clothes, see-through, torn clothes, dishevelled";

    private const string Intimate =
        "intimacy: kiss, tongue, embrace, straddling, lap sitting, "
        + "hand on thigh, undressing another";

    private const string Acts =
        "acts: sex, vaginal, fellatio, cunnilingus, paizuri, handjob, masturbation, "
        + "from behind, missionary, cowgirl position, against wall, on bed";

    private const string Reactions =
        "reactions: blush, heavy breathing, parted lips, half-closed eyes, "
        + "ahegao, sweatdrop, trembling, aroused";

    private const string Fluids =
        "fluids: saliva, sweat, tears, cum, cum on body, wet";

    private const string Setting =
        "context: bedroom, bath, hot springs, changing room, public, outdoors, "
        + "clothing aside, discarded clothes";

    /// <summary>
    /// Direction for the curator, tailored to the project's rating.
    /// </summary>
    public static string CuratorGuidance(ContentRating rating) =>
        rating switch
        {
            ContentRating.General =>
                "No adult content. Keep every description suitable for a general audience.",

            ContentRating.Mature =>
                $"""
                This is an adult story and the art is uncensored. Describe suggestive
                and intimate moments plainly — never fade to black, never imply
                instead of stating. Write the visual in danbooru-style tags where a
                tag is clearer than prose, drawing on this vocabulary:

                {State}
                {Anatomy}
                {Intimate}
                {Reactions}

                Stop short of explicit acts; atmosphere and partial states belong here.
                """,

            ContentRating.Explicit =>
                $"""
                This is an explicit adult story and the art is uncensored. Describe
                exactly what happens, in the same terms the drawing needs — never
                fade to black, never soften, never imply instead of stating. Write
                the visual in danbooru-style tags where a tag is clearer than prose,
                drawing on this vocabulary:

                {Acts}
                {Anatomy}
                {State}
                {Intimate}
                {Reactions}
                {Fluids}
                {Setting}

                Name who is doing what to whom, and where hands and bodies are.
                """,
            _ => string.Empty,
        };

    /// <summary>True when the rating expects adult vocabulary.</summary>
    public static bool IsAdult(ContentRating rating) => rating != ContentRating.General;
}
