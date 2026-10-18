namespace Rift.Core.Operations;

public sealed record OperationError(string Path, string Message);

/// <summary>
/// Records what an operation changed on disk so it can be undone (Ctrl+Z).
/// Undo replays the entries in reverse. Overwritten files and permanent deletes are not recorded:
/// their old content is gone, so they can't be undone. The Recycle Bin can: items go back where they were.
/// </summary>
public sealed class OperationJournal
{
    private abstract record Entry;
    private sealed record Created(string Path, bool IsDirectory) : Entry;
    private sealed record Moved(string From, string To) : Entry;
    private sealed record RemovedEmptyDirectory(string Path) : Entry;
    private sealed record Recycled(RecycledItem Item) : Entry;

    private readonly List<Entry> _entries = [];
    private readonly Lock _lock = new();

    public bool IsEmpty
    {
        get { lock (_lock) return _entries.Count == 0; }
    }

    internal void AddCreated(string path, bool isDirectory) => Add(new Created(path, isDirectory));
    internal void AddMoved(string from, string to) => Add(new Moved(from, to));
    internal void AddRemovedEmptyDirectory(string path) => Add(new RemovedEmptyDirectory(path));

    /// <summary>Del sent this to the Recycle Bin; undo brings it back.</summary>
    public void AddRecycled(RecycledItem item) => Add(new Recycled(item));

    private void Add(Entry e)
    {
        lock (_lock) _entries.Add(e);
    }

    public Task<IReadOnlyList<OperationError>> UndoAsync(CancellationToken ct = default) => Task.Run(() => Undo(ct), ct);

    private IReadOnlyList<OperationError> Undo(CancellationToken ct)
    {
        Entry[] entries;
        lock (_lock) entries = [.. _entries];

        var errors = new List<OperationError>();
        for (var i = entries.Length - 1; i >= 0; i--)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                switch (entries[i])
                {
                    case Created { IsDirectory: false } c:
                        if (File.Exists(c.Path))
                            File.Delete(c.Path);
                        break;
                    case Created { IsDirectory: true } c:
                        // Files inside were undone before; keep the folder if something new appeared in it.
                        if (Directory.Exists(c.Path) && !Directory.EnumerateFileSystemEntries(c.Path).Any())
                            Directory.Delete(c.Path);
                        break;
                    case Moved m:
                        if (FileNames.Exists(m.From))
                            throw new IOException($"'{m.From}' already exists");
                        Directory.CreateDirectory(Path.GetDirectoryName(m.From)!);
                        MoveExact(m.To, m.From);
                        break;
                    case RemovedEmptyDirectory r:
                        Directory.CreateDirectory(r.Path);
                        break;
                    case Recycled { Item: var item }:
                        if (FileNames.Exists(item.OriginalPath))
                            throw new IOException($"'{item.OriginalPath}' already exists");
                        RecycleBinStore.Restore(item);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(new OperationError(entries[i] switch
                {
                    Created c => c.Path,
                    Moved m => m.To,
                    RemovedEmptyDirectory r => r.Path,
                    Recycled r => r.Item.OriginalPath,
                    _ => "",
                }, ex.Message));
            }
        }

        lock (_lock) _entries.Clear();
        return errors;
    }

    /// <summary>Moves a file or folder to an exact path, falling back to copy+delete across volumes.</summary>
    internal static void MoveExact(string from, string to)
    {
        if (File.Exists(from))
        {
            File.Move(from, to);
            return;
        }

        if (FileTransfer.SameVolume(from, to))
        {
            Directory.Move(from, to);
            return;
        }

        CopyDirectory(from, to);
        Directory.Delete(from, recursive: true);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
