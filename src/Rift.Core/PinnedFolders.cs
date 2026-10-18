namespace Rift.Core;

/// <summary>Folders the user pinned to the sidebar, in the order they were pinned. Saved as one path per line.</summary>
public sealed class PinnedFolders
{
    private readonly string _file;
    private readonly List<string> _items = [];

    public PinnedFolders(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file))
                _items.AddRange(File.ReadAllLines(file).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Start empty rather than fail.
        }
    }

    public static string DefaultFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rift", "pinned.txt");

    public IReadOnlyList<string> Items => _items;

    public bool Contains(string path) => _items.Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <returns>False if it was already pinned.</returns>
    public bool Pin(string path)
    {
        if (Contains(path))
            return false;
        _items.Add(path);
        Save();
        return true;
    }

    public bool Unpin(string path)
    {
        var removed = _items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
            Save();
        return removed;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllLines(_file, _items);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The pin still holds for this session.
        }
    }
}
