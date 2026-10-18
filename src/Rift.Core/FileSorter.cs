namespace Rift.Core;

public enum SortColumn
{
    Name,
    Modified,
    Created,
    Type,
    Size,
}

/// <summary>Sorts a listing by a column. Folders always come first, like in Explorer; ties fall back to the name.</summary>
public static class FileSorter
{
    public static List<FileEntry> Sort(IEnumerable<FileEntry> entries, SortColumn column, bool descending)
    {
        var list = entries.ToList();
        list.Sort(Comparer(column, descending));
        return list;
    }

    public static Comparison<FileEntry> Comparer(SortColumn column, bool descending) => (a, b) =>
    {
        if (a.IsDirectory != b.IsDirectory)
            return a.IsDirectory ? -1 : 1;

        var result = column switch
        {
            SortColumn.Modified => a.Modified.CompareTo(b.Modified),
            SortColumn.Created => a.Created.CompareTo(b.Created),
            SortColumn.Size => (a.Size ?? -1).CompareTo(b.Size ?? -1),
            SortColumn.Type => string.Compare(FileKinds.Label(a), FileKinds.Label(b), StringComparison.CurrentCultureIgnoreCase),
            _ => NaturalStringComparer.Instance.Compare(a.Name, b.Name),
        };
        if (result != 0)
            return descending ? -result : result;
        // Ties read by name, A to Z, whichever way the column goes.
        result = NaturalStringComparer.Instance.Compare(a.Name, b.Name);
        return result != 0 ? result : string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
    };
}
