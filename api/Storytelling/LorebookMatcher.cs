using System.Text.RegularExpressions;
using ComicStudio.Api.Domain;

namespace ComicStudio.Api.Storytelling;

/// <summary>
/// Decides which lorebook entries activate for a given slice of story text.
/// Key matching is case-insensitive and boundary-aware for word-like keys so
/// that "Ana" does not trigger inside "banana", while CJK keys match directly.
/// </summary>
public static class LorebookMatcher
{
    public static IReadOnlyList<string> ParseKeys(string? keysRaw)
    {
        if (string.IsNullOrWhiteSpace(keysRaw))
        {
            return [];
        }

        return keysRaw
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<LorebookEntry> Select(
        IEnumerable<LorebookEntry> entries,
        string scanText
    )
    {
        var selected = new List<LorebookEntry>();

        foreach (var entry in entries)
        {
            if (!entry.Enabled)
            {
                continue;
            }

            if (entry.AlwaysOn)
            {
                selected.Add(entry);
                continue;
            }

            var keys = ParseKeys(entry.KeysRaw);
            if (keys.Count == 0)
            {
                continue;
            }

            if (keys.Any(key => Contains(scanText, key)))
            {
                selected.Add(entry);
            }
        }

        // Higher insertion order reserves tokens and is placed first.
        return selected.OrderByDescending(entry => entry.InsertionOrder).ToArray();
    }

    /// <summary>Index of the last key match, or -1. Used for key-relative insertion.</summary>
    public static int LastMatchIndex(string text, LorebookEntry entry)
    {
        var index = -1;
        foreach (var key in ParseKeys(entry.KeysRaw))
        {
            var match = FindLast(text, key);
            if (match > index)
            {
                index = match;
            }
        }

        return index;
    }

    private static bool Contains(string haystack, string key) => FindLast(haystack, key) >= 0;

    private static int FindLast(string haystack, string key)
    {
        if (haystack.Length == 0 || key.Length == 0)
        {
            return -1;
        }

        if (!IsWordLike(key))
        {
            return haystack.LastIndexOf(key, StringComparison.OrdinalIgnoreCase);
        }

        return Regex.Matches(haystack, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(key)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Index)
            .DefaultIfEmpty(-1)
            .Max();
    }

    private static bool IsWordLike(string key) =>
        key.All(character => char.IsLetterOrDigit(character) || character == '\'');
}
