namespace Rift.Core;

/// <summary>
/// Fuzzy matching for the command palette: the query's letters must appear in order ("nwf" → "New folder").
/// Matches at word starts and runs of consecutive letters score higher, so the obvious hit comes first.
/// </summary>
public static class FuzzyMatcher
{
    /// <returns>A score (higher is better), or null when the text doesn't match.</returns>
    public static int? Score(string query, string text)
    {
        query = query.Trim();
        if (query.Length == 0)
            return 0;

        var score = 0;
        var t = 0;
        var previousMatch = -2;
        foreach (var qc in query)
        {
            if (qc == ' ')
                continue;
            var found = -1;
            for (; t < text.Length; t++)
            {
                if (char.ToLowerInvariant(text[t]) == char.ToLowerInvariant(qc))
                {
                    found = t;
                    break;
                }
            }
            if (found < 0)
                return null;

            score += 1;
            if (found == previousMatch + 1)
                score += 5; // consecutive letters
            if (found == 0 || IsWordStart(text, found))
                score += 8; // start of a word: "nf" → "New Folder"
            previousMatch = found;
            t = found + 1;
        }

        // A plain substring beats a scattered match; a prefix beats both. Shorter texts win ties.
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            score += 30;
        else if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 15;
        return score * 100 - Math.Min(text.Length, 99);
    }

    private static bool IsWordStart(string text, int i)
    {
        var before = text[i - 1];
        return before is ' ' or '\\' or '/' or '-' or '_' or '.' or '(' ||
               (char.IsLower(before) && char.IsUpper(text[i]));
    }
}
