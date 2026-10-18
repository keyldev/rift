namespace Rift.Core;

/// <summary>
/// Which files carry which tags (tags.json: path → tag ids). Rift keeps this itself: Windows has no tags
/// that work for every file type. Paths follow renames and moves made in Rift.
/// </summary>
public sealed class TagStore
{
    private readonly string? _file;
    private readonly Dictionary<string, List<string>> _byPath;
    private readonly Lock _lock = new();

    public TagStore(string? file)
    {
        _file = file;
        var loaded = file is null ? null : JsonFile.Read(file, SettingsJson.Default.DictionaryStringListString);
        _byPath = new Dictionary<string, List<string>>(loaded ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public static string DefaultFile => Path.Combine(SessionStore.Folder, "tags.json");

    /// <summary>Raised after tags change (on the calling thread).</summary>
    public event Action? Changed;

    public IReadOnlyList<string> TagsOf(string path)
    {
        lock (_lock)
            return _byPath.TryGetValue(path, out var tags) ? [.. tags] : [];
    }

    public bool Has(string path, string tagId)
    {
        lock (_lock)
            return _byPath.TryGetValue(path, out var tags) && tags.Contains(tagId);
    }

    /// <summary>Every path with the tag, in no particular order.</summary>
    public IReadOnlyList<string> PathsWith(string tagId)
    {
        lock (_lock)
            return _byPath.Where(p => p.Value.Contains(tagId)).Select(p => p.Key).ToList();
    }

    /// <summary>Adds the tag to all paths, or removes it from all when every one already has it (a menu toggle).</summary>
    public void Toggle(IReadOnlyList<string> paths, string tagId)
    {
        lock (_lock)
        {
            var add = !paths.All(p => _byPath.TryGetValue(p, out var t) && t.Contains(tagId));
            foreach (var path in paths)
            {
                if (add)
                {
                    if (!_byPath.TryGetValue(path, out var tags))
                        _byPath[path] = tags = [];
                    if (!tags.Contains(tagId))
                        tags.Add(tagId);
                }
                else if (_byPath.TryGetValue(path, out var tags))
                {
                    tags.Remove(tagId);
                    if (tags.Count == 0)
                        _byPath.Remove(path);
                }
            }
        }
        Save();
    }

    /// <summary>A tag was deleted: it goes from every file.</summary>
    public void Forget(string tagId)
    {
        lock (_lock)
        {
            foreach (var path in _byPath.Keys.ToList())
            {
                _byPath[path].Remove(tagId);
                if (_byPath[path].Count == 0)
                    _byPath.Remove(path);
            }
        }
        Save();
    }

    /// <summary>A file or folder was renamed or moved: its tags, and those of everything inside it, follow.</summary>
    public void Move(string from, string to)
    {
        var changed = false;
        lock (_lock)
        {
            var prefix = from.TrimEnd('\\') + '\\';
            foreach (var path in _byPath.Keys.ToList())
            {
                string? target = string.Equals(path, from, StringComparison.OrdinalIgnoreCase) ? to
                    : path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? to.TrimEnd('\\') + '\\' + path[prefix.Length..]
                    : null;
                if (target is null)
                    continue;
                var tags = _byPath[path];
                _byPath.Remove(path);
                _byPath[target] = tags;
                changed = true;
            }
        }
        if (changed)
            Save();
    }

    private void Save()
    {
        if (_file is not null)
        {
            try
            {
                Dictionary<string, List<string>> copy;
                lock (_lock)
                    copy = _byPath.ToDictionary(p => p.Key, p => p.Value.ToList());
                JsonFile.Write(_file, copy, SettingsJson.Default.DictionaryStringListString);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Tags stay in memory for this session.
            }
        }
        Changed?.Invoke();
    }
}

/// <summary>How a folder was last shown.</summary>
public sealed record FolderView(string View, SortColumn Sort, bool Descending);

/// <summary>Remembers view and sort per folder (folderviews.json), newest first, a few thousand folders at most.</summary>
public sealed class FolderViewStore
{
    public const int Capacity = 3000;

    private readonly string? _file;
    private readonly Dictionary<string, FolderView> _views;
    private readonly LinkedList<string> _order = new();
    private Timer? _saveTimer;

    public FolderViewStore(string? file)
    {
        _file = file;
        var loaded = file is null ? null : JsonFile.Read(file, SettingsJson.Default.DictionaryStringFolderView);
        _views = new Dictionary<string, FolderView>(loaded ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var key in _views.Keys)
            _order.AddLast(key);
    }

    public static string DefaultFile => Path.Combine(SessionStore.Folder, "folderviews.json");

    public FolderView? Get(string folder)
    {
        lock (_views)
            return _views.GetValueOrDefault(folder);
    }

    public void Set(string folder, FolderView view)
    {
        lock (_views)
        {
            if (_views.TryGetValue(folder, out var old) && old == view)
                return;
            _views[folder] = view;
            _order.Remove(folder);
            _order.AddFirst(folder);
            while (_order.Count > Capacity && _order.Last is { } oldest)
            {
                _views.Remove(oldest.Value);
                _order.RemoveLast();
            }
        }
        // Clicking through sort orders shouldn't write the file each time.
        if (_file is not null)
            (_saveTimer ??= new Timer(_ => Save())).Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    public void Save()
    {
        if (_file is null)
            return;
        Dictionary<string, FolderView> copy;
        lock (_views)
            copy = new Dictionary<string, FolderView>(_views);
        try
        {
            JsonFile.Write(_file, copy, SettingsJson.Default.DictionaryStringFolderView);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Next time, then.
        }
    }
}
