using System.Text.Json;
using ComicStudio.Api.Comics;

namespace ComicStudio.Api.Tests.Comics;

/// <summary>
/// The page-script prompt asks for the continuity ledger as named sections, so a
/// model returns it as an object about as often as a flat string. A live run
/// failed every page with "The JSON value could not be converted to
/// System.String. Path: $.pages[0].continuity" and produced nothing, twice,
/// because of exactly that. These pin the tolerant read.
/// </summary>
public class LedgerTextConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    private static ScriptedPage Page(string continuityJson)
    {
        var json = $$"""
            {
              "pages": [
                {
                  "index": 1,
                  "sceneTitle": "The Blue Light",
                  "prompt": "Mira climbs the tower stair.",
                  "continuity": {{continuityJson}},
                  "panels": [],
                  "bubbles": []
                }
              ]
            }
            """;

        var result = JsonSerializer.Deserialize<PageScriptResult>(json, Options);
        Assert.NotNull(result);
        return Assert.Single(result!.Pages);
    }

    [Fact]
    public void A_flat_string_passes_through_unchanged()
    {
        var page = Page("\"LOCATION  the tower, night\\nLAST BEAT  the door closes\"");

        Assert.Equal("LOCATION  the tower, night\nLAST BEAT  the door closes", page.Continuity);
    }

    [Fact]
    public void A_section_object_becomes_the_canonical_text()
    {
        var page = Page(
            """
            {
              "LOCATION": "the tower floor, night, rain",
              "CAST": "Mira — soaked coat, calm",
              "PROPS": "a lantern, unlit",
              "THREADS": "who locked the door",
              "PALETTE": "cold greys, one warm lantern highlight",
              "LAST BEAT": "the door swings shut behind her"
            }
            """
        );

        var ledger = Assert.IsType<string>(page.Continuity);

        // The seam pass looks for LAST BEAT; the workspace splits on the headings.
        Assert.Contains("LAST BEAT", ledger);
        Assert.Contains("LOCATION", ledger);
        Assert.Contains("the door swings shut behind her", ledger);

        // Headings lead their own lines, upper-cased, so both consumers find them.
        Assert.Contains("\nLAST BEAT  the door swings shut behind her", ledger);
        Assert.StartsWith("LOCATION  the tower floor", ledger);
    }

    [Fact]
    public void Section_order_is_preserved()
    {
        var page = Page(
            """{ "LOCATION": "first", "LAST BEAT": "second" }"""
        );

        var ledger = Assert.IsType<string>(page.Continuity);
        Assert.True(
            ledger.IndexOf("LOCATION", StringComparison.Ordinal)
                < ledger.IndexOf("LAST BEAT", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Nested_lists_flatten_into_their_section()
    {
        var page = Page(
            """{ "CAST": ["Mira — calm", "The Warden — limping"], "LAST BEAT": "silence" }"""
        );

        var ledger = Assert.IsType<string>(page.Continuity);
        Assert.Contains("Mira — calm; The Warden — limping", ledger);
        Assert.Contains("LAST BEAT  silence", ledger);
    }

    [Fact]
    public void A_bare_array_joins_into_lines()
    {
        var page = Page("""["LOCATION  the tower", "LAST BEAT  the door closes"]""");

        Assert.Equal("LOCATION  the tower\nLAST BEAT  the door closes", page.Continuity);
    }

    [Fact]
    public void Blank_sections_are_dropped_rather_than_left_as_empty_headings()
    {
        var page = Page("""{ "LOCATION": "the tower", "CAST": "", "PROPS": null }""");

        Assert.Equal("LOCATION  the tower", page.Continuity);
    }

    [Fact]
    public void Null_and_missing_stay_null()
    {
        Assert.Null(Page("null").Continuity);
    }

    [Fact]
    public void An_empty_object_does_not_invent_a_ledger()
    {
        Assert.Null(Page("{}").Continuity);
    }

    [Fact]
    public void A_numeric_section_is_read_rather_than_throwing()
    {
        var page = Page("""{ "THREADS": 3, "LOCATION": "the tower" }""");

        var ledger = Assert.IsType<string>(page.Continuity);
        Assert.Contains("THREADS  3", ledger);
    }

    [Fact]
    public void A_scene_plan_page_ledger_is_tolerant_too()
    {
        var json = """
            {
              "pages": [
                { "index": 1, "sceneTitle": "Open", "prompt": "x",
                  "continuity": { "LOCATION": "the deck", "LAST BEAT": "the horn sounds" } }
              ]
            }
            """;

        var page = JsonSerializer.Deserialize<PageScriptResult>(json, Options)!.Pages[0];
        Assert.Contains("LAST BEAT  the horn sounds", page.Continuity);
    }
}
