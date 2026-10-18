using System.Diagnostics;

namespace Rift.Core;

/// <summary>
/// Recursive search by name under a folder. Results arrive in batches while the walk goes on,
/// so the first hits show up at once even on a big drive.
/// </summary>
public static class FileSearch
{
    /// <summary>More than this is not a search result anymore; the user should narrow the query.</summary>
    public const int MaxResults = 20_000;

    private static readonly TimeSpan BatchInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Walks <paramref name="root"/> and reports matches. Runs on the calling thread.</summary>
    /// <returns>True if it stopped at <see cref="MaxResults"/>.</returns>
    public static bool Run(string root, string query, bool includeHidden, Action<IReadOnlyList<FileEntry>> onBatch, CancellationToken ct)
    {
        var filter = new NameFilter(query);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            // Junctions like "Application Data" point back up the tree; following them loops forever.
            AttributesToSkip = FileAttributes.ReparsePoint | (includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System),
        };

        var batch = new List<FileEntry>();
        var clock = Stopwatch.StartNew();
        var total = 0;
        foreach (var info in new DirectoryInfo(root).EnumerateFileSystemInfos("*", options))
        {
            ct.ThrowIfCancellationRequested();
            if (filter.Matches(info.Name))
            {
                var entry = FileEntry.From(info);
                batch.Add(entry with { TypeNote = RelativeFolder(root, info.FullName) });
                if (++total >= MaxResults)
                {
                    onBatch(batch);
                    return true;
                }
            }
            if (batch.Count > 0 && clock.Elapsed >= BatchInterval)
            {
                onBatch(batch);
                batch = [];
                clock.Restart();
            }
        }
        if (batch.Count > 0)
            onBatch(batch);
        return false;
    }

    /// <summary>Where a hit lives, relative to the search root: "src\Rift.App", or "." for the root itself.</summary>
    public static string RelativeFolder(string root, string path)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        var relative = Path.GetRelativePath(root, folder);
        return relative == "." ? "." : relative;
    }
}
