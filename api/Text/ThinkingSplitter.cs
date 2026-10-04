using System.Text;

namespace ComicStudio.Api.Text;

/// <summary>
/// Splits NovelAI's inline chain-of-thought out of a streamed answer.
///
/// GLM-4.6 is a hybrid reasoner and NovelAI streams its thinking inline in the
/// same content field as the answer, wrapped in "&lt;think&gt;"-style markers.
/// The transport has to keep the two apart, but NovelAI emits the stream at
/// arbitrary byte boundaries: an opening marker regularly arrives as "&lt;",
/// then "thi", then "nk&gt;". A per-chunk Contains() check would miss those and
/// leak the whole chain of thought into the answer.
///
/// This class therefore holds back any trailing text that could still become a
/// marker and emits it only once a later chunk proves it was ordinary text.
/// Nothing is ever dropped: the concatenation of every emitted delta equals the
/// concatenation of every pushed chunk, markers included. Marker text itself is
/// emitted as <see cref="TextDeltaKind.Reasoning"/> so a consumer can
/// reconstruct the raw stream exactly.
///
/// With no markers at all it degrades to passing every character straight
/// through as content, with nothing held back once <c>flush</c> is requested.
/// </summary>
public sealed class ThinkingSplitter
{
    private const string OpenTag = "<think>";
    private const string OpenTagLong = "<thinking>";
    private const string CloseTag = "</think>";
    private const string CloseTagLong = "</thinking>";

    /// <summary>
    /// The shortest trailing text that is still read as an opening marker at end
    /// of stream ("&lt;think"). Shorter prefixes are ordinary content.
    /// </summary>
    private const string OpenPrefix = "<think";

    private static readonly string[] OpenMarkers = [OpenTag, OpenTagLong];
    private static readonly string[] CloseMarkers = [CloseTag, CloseTagLong];

    private readonly StringBuilder pending = new();
    private bool thinking;

    /// <summary>
    /// Feeds the next raw chunk through the state machine and returns the deltas
    /// it completes. Text that might be the start of a marker stays held back
    /// until a later chunk (or <paramref name="flush"/>) resolves it.
    /// </summary>
    /// <param name="chunk">Raw text from the transport. Null or empty is allowed.</param>
    /// <param name="flush">
    /// Set once at end of stream. Flushes everything that is still held: an
    /// unterminated think block is emitted as reasoning, including a stream that
    /// ends on a bare "&lt;think". Resets the splitter so it can be reused.
    /// </param>
    public IReadOnlyList<TextDelta> Push(string? chunk, bool flush = false)
    {
        if (!string.IsNullOrEmpty(chunk))
        {
            pending.Append(chunk);
        }

        var deltas = new List<TextDelta>();
        var text = pending.ToString();
        var consumed = 0;

        while (true)
        {
            var match = FindMarker(text, consumed, thinking);
            if (match is null)
            {
                break;
            }

            if (match.Value.Index > consumed)
            {
                Append(deltas, thinking, text[consumed..match.Value.Index]);
            }

            // Markers travel as reasoning, so consuming every delta in order
            // reproduces the raw stream, markers and all.
            Append(deltas, reasoning: true, text.Substring(match.Value.Index, match.Value.Length));
            thinking = !thinking;
            consumed = match.Value.Index + match.Value.Length;
        }

        pending.Clear();
        var tail = text[consumed..];

        if (flush)
        {
            // A stream that stops on a bare opening prefix is an unterminated
            // think block. Short prefixes ("<", "<thi") could be anything, so
            // they stay content and the no-marker case loses nothing.
            if (!thinking && TrySplitBareOpening(tail, out var before, out var opener))
            {
                Append(deltas, reasoning: false, before);
                Append(deltas, reasoning: true, opener);
                thinking = true;
                tail = string.Empty;
            }

            Append(deltas, thinking, tail);
            thinking = false;
            return deltas;
        }

        // Hold back the longest trailing partial that could still turn into a
        // marker. Its length is bounded by the longest marker, so nothing grows
        // without bound even if the model never emits a marker.
        var hold = LongestMarkerPrefixSuffix(tail, thinking);
        if (hold < tail.Length)
        {
            Append(deltas, thinking, tail[..(tail.Length - hold)]);
        }

        if (hold > 0)
        {
            pending.Append(tail, tail.Length - hold, hold);
        }

        return deltas;
    }

    private static void Append(List<TextDelta> deltas, bool reasoning, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        deltas.Add(reasoning ? TextDelta.Reasoning(text) : TextDelta.Content(text));
    }

    /// <summary>Finds the earliest full marker the current mode accepts.</summary>
    private static (int Index, int Length)? FindMarker(string text, int start, bool thinking)
    {
        (int Index, int Length)? best = null;

        foreach (var marker in thinking ? CloseMarkers : OpenMarkers)
        {
            var index = text.IndexOf(marker, start, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best is null || index < best.Value.Index))
            {
                best = (index, marker.Length);
            }
        }

        return best;
    }

    /// <summary>
    /// Length of the longest trailing text that is a proper prefix of a marker
    /// accepted by the current mode (case-insensitive).
    /// </summary>
    private static int LongestMarkerPrefixSuffix(string text, bool thinking)
    {
        var markers = thinking ? CloseMarkers : OpenMarkers;
        var longest = 0;

        foreach (var marker in markers)
        {
            var limit = Math.Min(text.Length, marker.Length - 1);
            for (var length = limit; length > longest; length--)
            {
                var suffix = text.AsSpan(text.Length - length);
                if (suffix.Equals(marker.AsSpan(0, length), StringComparison.OrdinalIgnoreCase))
                {
                    longest = length;
                    break;
                }
            }
        }

        return longest;
    }

    /// <summary>
    /// Splits a trailing bare opening prefix ("&lt;think" up to "&lt;thinking")
    /// off ordinary content for the end-of-stream case.
    /// </summary>
    private static bool TrySplitBareOpening(string text, out string before, out string opener)
    {
        var max = Math.Min(text.Length, OpenTagLong.Length - 1);

        for (var length = max; length >= OpenPrefix.Length; length--)
        {
            var suffix = text.AsSpan(text.Length - length);
            if (!suffix.StartsWith(OpenPrefix.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var marker in OpenMarkers)
            {
                if (
                    length <= marker.Length - 1
                    && suffix.Equals(marker.AsSpan(0, length), StringComparison.OrdinalIgnoreCase)
                )
                {
                    before = text[..(text.Length - length)];
                    opener = text[(text.Length - length)..];
                    return true;
                }
            }
        }

        before = text;
        opener = string.Empty;
        return false;
    }
}
