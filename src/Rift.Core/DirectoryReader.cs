namespace Rift.Core;

public static class DirectoryReader
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Reads a folder: folders first, then files, both by name. Hidden and system items only on request.</summary>
    public static IReadOnlyList<FileEntry> Read(string path, bool includeHidden = false)
    {
        var entries = new List<FileEntry>();
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", Options))
        {
            var entry = FileEntry.From(info);
            if (entry.IsHidden && !includeHidden)
                continue;
            entries.Add(entry);
        }

        entries.Sort(CompareEntries);
        return entries;
    }

    public static Task<IReadOnlyList<FileEntry>> ReadAsync(string path, bool includeHidden = false, CancellationToken ct = default)
        => Task.Run(() => Read(path, includeHidden), ct);

    private static int CompareEntries(FileEntry a, FileEntry b)
    {
        if (a.IsDirectory != b.IsDirectory)
            return a.IsDirectory ? -1 : 1;
        return NaturalStringComparer.Instance.Compare(a.Name, b.Name);
    }
}
