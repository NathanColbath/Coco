using ComicStudio.Api.Text;

namespace ComicStudio.Api.Tests.Text;

/// <summary>
/// The splitter is the highest-risk piece of the text layer: NovelAI cuts the
/// stream at arbitrary byte boundaries, so every marker can arrive in pieces.
/// These tests pin down the boundary cases and then prove, exhaustively, that
/// where the chunks are cut never changes the answer.
/// </summary>
public class ThinkingSplitterTests
{
    private const string RealisticStream =
        "Scene one. The garden was still, and the gate stood open. "
        + "<this is a literal angle bracket, not a marker. "
        + "<think>First I should establish the setting. Let me consider the words: "
        + "garden, roses, morning dew. Keep the sentence short.</think>"
        + "Morning dew clung to the roses."
        + "<THINKING>Second block: mention bees? No, keep it short.</Thinking>"
        + "A bee hummed past the open gate. The end."
        + "</think>"
        + "A stray closing marker stays in the prose. Unclosed long marker: <thinking";

    private const string RealisticUnterminated =
        "The gate creaked open. "
        + "<think>I should decide whether the hinge is rusty or merely old. "
        + "Let me weigh the atmosphere before writing anything.";

    private static List<TextDelta> Feed(params string[] chunks)
    {
        var splitter = new ThinkingSplitter();
        var deltas = new List<TextDelta>();

        foreach (var chunk in chunks)
        {
            deltas.AddRange(splitter.Push(chunk));
        }

        deltas.AddRange(splitter.Push(null, flush: true));
        return deltas;
    }

    private static string All(IEnumerable<TextDelta> deltas) =>
        string.Concat(deltas.Select(delta => delta.Text));

    private static string Content(IEnumerable<TextDelta> deltas) =>
        string.Concat(
            deltas
                .Where(delta => delta.Kind == TextDeltaKind.Content)
                .Select(delta => delta.Text)
        );

    private static string Reasoning(IEnumerable<TextDelta> deltas) =>
        string.Concat(
            deltas
                .Where(delta => delta.Kind == TextDeltaKind.Reasoning)
                .Select(delta => delta.Text)
        );

    /// <summary>
    /// Splits the stream at every possible pair of boundaries and at every
    /// single character, then checks the result is identical to feeding it in
    /// one chunk. This is the guarantee that fragmented markers cannot change
    /// or lose anything.
    /// </summary>
    private static void AssertBoundaryInvariant(string stream)
    {
        var expected = Feed(stream);
        Assert.Equal(stream, All(expected));
        var expectedContent = Content(expected);
        var expectedReasoning = Reasoning(expected);

        // Every one-byte boundary, in the strongest form: one character per
        // "network chunk".
        var byteWise = Feed(stream.Select(character => character.ToString()).ToArray());
        Assert.Equal(stream, All(byteWise));
        Assert.Equal(expectedContent, Content(byteWise));
        Assert.Equal(expectedReasoning, Reasoning(byteWise));

        // Every two-chunk cut.
        for (var first = 0; first <= stream.Length; first++)
        {
            var deltas = Feed(stream[..first], stream[first..]);
            Assert.Equal(stream, All(deltas));
            Assert.Equal(expectedContent, Content(deltas));
            Assert.Equal(expectedReasoning, Reasoning(deltas));
        }

        // Every three-chunk cut.
        for (var first = 0; first <= stream.Length; first++)
        {
            for (var second = first; second <= stream.Length; second++)
            {
                var deltas = Feed(stream[..first], stream[first..second], stream[second..]);
                Assert.Equal(stream, All(deltas));
                Assert.Equal(expectedContent, Content(deltas));
                Assert.Equal(expectedReasoning, Reasoning(deltas));
            }
        }
    }

    [Fact]
    public void Every_boundary_of_a_realistic_stream_gives_the_same_answer()
    {
        AssertBoundaryInvariant(RealisticStream);
    }

    [Fact]
    public void Every_boundary_of_an_unterminated_think_block_gives_the_same_answer()
    {
        AssertBoundaryInvariant(RealisticUnterminated);
    }

    [Theory]
    [InlineData("<think>")]
    [InlineData("<thinking>")]
    [InlineData("</think>")]
    [InlineData("</thinking>")]
    public void Every_split_of_every_marker_on_its_own_gives_the_same_answer(string marker)
    {
        AssertBoundaryInvariant($"before{marker}after");
    }

    [Fact]
    public void No_markers_passes_every_chunk_straight_through_as_content()
    {
        var splitter = new ThinkingSplitter();

        var first = splitter.Push("Hello");
        Assert.Single(first);
        Assert.Equal(TextDeltaKind.Content, first[0].Kind);
        Assert.Equal("Hello", first[0].Text);

        var second = splitter.Push(", world");
        Assert.Single(second);
        Assert.Equal(", world", second[0].Text);

        // Nothing is held back at the end of a marker-free stream.
        Assert.Empty(splitter.Push(null, flush: true));
    }

    [Fact]
    public void Markers_are_emitted_as_reasoning_and_never_inside_content()
    {
        var deltas = Feed("before<think>thought</think>after");

        Assert.Equal("beforeafter", Content(deltas));
        Assert.Equal("<think>thought</think>", Reasoning(deltas));
        Assert.DoesNotContain("<think", Content(deltas));
        Assert.DoesNotContain("</think", Content(deltas));
    }

    [Fact]
    public void Opening_marker_at_the_start_becomes_reasoning()
    {
        var deltas = Feed("<think>thinking</think>hi");

        Assert.Equal("hi", Content(deltas));
        Assert.Equal("<think>thinking</think>", Reasoning(deltas));
    }

    [Fact]
    public void An_opening_marker_with_no_closer_keeps_everything_after_it_as_reasoning()
    {
        var deltas = Feed("<think>hi");

        Assert.Empty(Content(deltas));
        Assert.Equal("<think>hi", Reasoning(deltas));
    }

    [Fact]
    public void Closing_marker_without_an_opening_is_literal_content()
    {
        var deltas = Feed("hi</think>");

        Assert.Equal("hi</think>", Content(deltas));
        Assert.Empty(Reasoning(deltas));
    }

    [Fact]
    public void Content_before_a_partial_marker_is_emitted_immediately()
    {
        var splitter = new ThinkingSplitter();

        var first = splitter.Push("Before<thi");
        Assert.Single(first);
        Assert.Equal("Before", first[0].Text);
        Assert.Equal(TextDeltaKind.Content, first[0].Kind);

        var second = splitter.Push("nk>After</think>Tail");
        Assert.Equal(4, second.Count);
        Assert.Equal(TextDeltaKind.Reasoning, second[0].Kind);
        Assert.Equal("<think>", second[0].Text);
        Assert.Equal("After", second[1].Text);
        Assert.Equal("</think>", second[2].Text);
        Assert.Equal(TextDeltaKind.Content, second[3].Kind);
        Assert.Equal("Tail", second[3].Text);
    }

    [Fact]
    public void A_partial_marker_that_turns_out_to_be_text_is_never_lost()
    {
        var deltas = Feed("A<thi", "s is", " not.");

        Assert.Equal("A<this is not.", Content(deltas));
        Assert.Empty(Reasoning(deltas));
    }

    [Fact]
    public void A_false_closing_prefix_inside_reasoning_is_never_lost()
    {
        var deltas = Feed("<think>a</thi", "s is just text</think>Tail");

        Assert.Equal("Tail", Content(deltas));
        Assert.Equal("<think>a</this is just text</think>", Reasoning(deltas));
    }

    [Fact]
    public void Two_think_blocks_split_the_stream_correctly()
    {
        var deltas = Feed("A<think>B</think>C<think>D</thinking>E");

        Assert.Equal("ACE", Content(deltas));
        Assert.Equal("<think>B</think><think>D</thinking>", Reasoning(deltas));
    }

    [Fact]
    public void Case_does_not_matter_on_markers()
    {
        var deltas = Feed("A<THINK>B</ThInK>C<Thinking>D</THINKING>E");

        Assert.Equal("ACE", Content(deltas));
        Assert.Equal("<THINK>B</ThInK><Thinking>D</THINKING>", Reasoning(deltas));
    }

    [Fact]
    public void Unterminated_think_block_flushes_as_reasoning()
    {
        var deltas = Feed("A<think>B and then it just stops");

        Assert.Equal("A", Content(deltas));
        Assert.Equal("<think>B and then it just stops", Reasoning(deltas));
    }

    [Fact]
    public void Bare_think_at_end_of_stream_is_an_unterminated_marker()
    {
        var deltas = Feed("A<think");

        Assert.Equal("A", Content(deltas));
        Assert.Equal("<think", Reasoning(deltas));
    }

    [Fact]
    public void Bare_long_prefix_at_end_of_stream_is_an_unterminated_marker()
    {
        var deltas = Feed("A<thinking");

        Assert.Equal("A", Content(deltas));
        Assert.Equal("<thinking", Reasoning(deltas));
    }

    [Theory]
    [InlineData("A<")]
    [InlineData("A<t")]
    [InlineData("A<th")]
    [InlineData("A<thi")]
    [InlineData("A<thin")]
    public void Short_prefixes_at_end_of_stream_stay_content(string stream)
    {
        var deltas = Feed(stream);

        Assert.Equal(stream, Content(deltas));
        Assert.Empty(Reasoning(deltas));
    }

    [Fact]
    public void Only_reasoning_produces_no_content()
    {
        var deltas = Feed("<think>all thought</think>");

        Assert.Empty(Content(deltas));
        Assert.Equal("<think>all thought</think>", Reasoning(deltas));
    }

    [Fact]
    public void Reasoning_can_contain_a_nested_opening_marker_as_text()
    {
        var deltas = Feed("<think>a<think>b</think>c");

        Assert.Equal("c", Content(deltas));
        Assert.Equal("<think>a<think>b</think>", Reasoning(deltas));
    }

    [Fact]
    public void Empty_and_null_chunks_do_not_disturb_the_state()
    {
        var splitter = new ThinkingSplitter();

        Assert.Empty(splitter.Push(""));
        Assert.Empty(splitter.Push(null));
        Assert.Empty(splitter.Push("<thi"));

        var mid = splitter.Push(null);
        Assert.Empty(mid);
        Assert.Empty(splitter.Push(""));

        var continued = splitter.Push("nk>hello");
        Assert.Equal(2, continued.Count);
        Assert.Equal("<think>", continued[0].Text);
        Assert.Equal(TextDeltaKind.Reasoning, continued[1].Kind);
        Assert.Equal("hello", continued[1].Text);
        Assert.Empty(splitter.Push(null, flush: true));
    }

    [Fact]
    public void A_chunk_that_is_only_a_less_than_waits_for_the_next_chunk()
    {
        var splitter = new ThinkingSplitter();

        Assert.Empty(splitter.Push("<"));

        var next = splitter.Push("think>hi");
        Assert.Equal(2, next.Count);
        Assert.Equal(TextDeltaKind.Reasoning, next[0].Kind);
        Assert.Equal("hi", next[1].Text);
    }

    [Fact]
    public void A_lone_less_than_at_end_of_stream_stays_content()
    {
        var splitter = new ThinkingSplitter();
        Assert.Empty(splitter.Push("<"));

        var flushed = splitter.Push(null, flush: true);
        Assert.Single(flushed);
        Assert.Equal(TextDeltaKind.Content, flushed[0].Kind);
        Assert.Equal("<", flushed[0].Text);
    }

    [Fact]
    public void Flush_resets_the_splitter_so_it_can_be_reused()
    {
        var splitter = new ThinkingSplitter();

        var first = splitter.Push("<think>a", flush: true);
        Assert.Equal("<think>a", Reasoning(first));

        var second = splitter.Push("plain text");
        Assert.Single(second);
        Assert.Equal(TextDeltaKind.Content, second[0].Kind);
        Assert.Equal("plain text", second[0].Text);
    }

    [Fact]
    public void One_chunk_can_contain_several_markers()
    {
        var deltas = Feed("x<think>1</think>y<think>2</think>z");

        Assert.Equal("xyz", Content(deltas));
        Assert.Equal("<think>1</think><think>2</think>", Reasoning(deltas));
    }

    [Fact]
    public void Unterminated_block_split_character_by_character_still_flushes_as_reasoning()
    {
        var deltas = Feed(RealisticUnterminated.Select(character => character.ToString()).ToArray());

        Assert.Equal("The gate creaked open. ", Content(deltas));
        Assert.StartsWith("<think>", Reasoning(deltas));
        Assert.Equal(RealisticUnterminated, All(deltas));
    }
}
