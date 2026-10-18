namespace Rift.Core;

/// <summary>Most recently opened folders, newest first; feeds the taskbar jump list.</summary>
public sealed class RecentFolders
{
    public const int Capacity = 10;

    private readonly string _file;
    private readonly List<string> _items = [];
    private readonly Lock _lock = new();

    public RecentFolders(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file))
                _items.AddRange(File.ReadAllLines(file).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(Capacity));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A broken history file is not worth failing over.
        }
    }

    public static string DefaultFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rift", "recent.txt");

    public IReadOnlyList<string> Items
    {
        get { lock (_lock) return [.. _items]; }
    }

    /// <summary>Moves the folder to the top. Returns false if it already was there.</summary>
    public bool Add(string path)
    {
        lock (_lock)
        {
            if (_items.Count > 0 && string.Equals(_items[0], path, StringComparison.OrdinalIgnoreCase))
                return false;
            _items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _items.Insert(0, path);
            if (_items.Count > Capacity)
                _items.RemoveRange(Capacity, _items.Count - Capacity);
            return true;
        }
    }

    public void Remove(IEnumerable<string> paths)
    {
        var set = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_lock) _items.RemoveAll(set.Contains);
    }

    public void Save()
    {
        var items = Items;
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllLines(_file, items);
    }
}
