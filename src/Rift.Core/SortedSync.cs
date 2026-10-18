namespace Rift.Core;

/// <summary>
/// Brings a list shown on screen in line with a fresh, identically sorted listing, touching only what changed.
/// Keeps scroll position, focus and selection intact when a folder reloads under the user.
/// </summary>
public static class SortedSync
{
    /// <param name="target">The displayed list, sorted the same way as <paramref name="source"/>.</param>
    /// <param name="source">The fresh listing.</param>
    /// <param name="key">Identity of an item; items whose key disappears are removed.</param>
    /// <param name="sameSlot">False when an item kept its key but must be re-created (e.g. a file became a folder).</param>
    public static void Apply<TItem, TEntry>(
        IList<TItem> target,
        IReadOnlyList<TEntry> source,
        Func<TItem, string> itemKey,
        Func<TEntry, string> entryKey,
        Func<TEntry, TItem> create,
        Action<TItem, TEntry> update,
        Func<TItem, TEntry, bool>? sameSlot = null)
    {
        var fresh = new Dictionary<string, TEntry>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source)
            fresh.TryAdd(entryKey(entry), entry);

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!fresh.TryGetValue(itemKey(target[i]), out var entry) || (sameSlot is not null && !sameSlot(target[i], entry)))
                target.RemoveAt(i);
        }

        // What's left in target is normally a subsequence of source (same keys, same order), so one walk is enough.
        var kept = new HashSet<string>(target.Select(itemKey), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < source.Count; i++)
        {
            var entry = source[i];
            var key = entryKey(entry);
            if (i < target.Count && string.Equals(itemKey(target[i]), key, StringComparison.OrdinalIgnoreCase))
            {
                update(target[i], entry);
            }
            else if (kept.Contains(key))
            {
                // The order changed (e.g. the sort rules did): give up on minimal edits.
                Rebuild(target, source, create);
                return;
            }
            else
            {
                target.Insert(i, create(entry));
            }
        }

        // Duplicate keys in source (shouldn't happen on a real disk) could leave extras behind.
        while (target.Count > source.Count)
            target.RemoveAt(target.Count - 1);
    }

    private static void Rebuild<TItem, TEntry>(IList<TItem> target, IReadOnlyList<TEntry> source, Func<TEntry, TItem> create)
    {
        target.Clear();
        foreach (var entry in source)
            target.Add(create(entry));
    }
}
