using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using Avalonia.Threading;
using Rift.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.Core;
using Rift.Core.Localization;
using Rift.Core.Operations;
using Rift.Shell;

namespace Rift.App.ViewModels;

public sealed record CrumbItem(string Label, string Path, bool HasSeparator);

public partial class TabViewModel : ViewModelBase, IDisposable
{
    private readonly NavigationHistory _history;
    private CancellationTokenSource? _loading;
    private readonly List<FolderWatcher> _watchers = [];
    /// <summary>In the Recycle Bin view: what each row stands for, by its $R path.</summary>
    private Dictionary<string, RecycledItem> _recycled = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The folder whose listing is on screen; reloading it edits the list instead of replacing it.</summary>
    private string? _loadedPath;
    /// <summary>A change on disk arrived while the user was renaming; reload once they're done.</summary>
    private bool _reloadAfterRename;

    public TabViewModel(string path)
    {
        _history = new NavigationHistory(path);
        ViewOptions.Current.PropertyChanged += OnViewOptionsChanged;
        _ = LoadAsync(path);
    }

    public void Dispose()
    {
        ViewOptions.Current.PropertyChanged -= OnViewOptionsChanged;
        _loading?.Cancel();
        StopWatching();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Crumbs), nameof(DisplayPath), nameof(Location), nameof(IsFileSystem),
        nameof(IsRecycleBin), nameof(DateColumn), nameof(TypeColumn))]
    private string _path = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Notice))]
    private string? _error;

    [ObservableProperty] private string _status = "";

    public string Title => Locations.Title(Path);
    public string DisplayPath => Locations.IsVirtual(Path) ? Title : Path;
    public IReadOnlyList<CrumbItem> Crumbs =>
        Locations.Crumbs(Path).Select((c, i) => new CrumbItem(c.Label, c.Path, i > 0)).ToList();

    public LocationKind Location => Locations.KindOf(Path);
    /// <summary>A real folder: files can be copied here, renamed, created. False for This PC and the Recycle Bin.</summary>
    public bool IsFileSystem => Location == LocationKind.Folder;
    public bool IsRecycleBin => Location == LocationKind.RecycleBin;

    public string DateColumn => Location switch
    {
        LocationKind.ThisPc => Strings.ColumnFree,
        LocationKind.RecycleBin => Strings.ColumnDeleted,
        _ => Strings.ColumnModified,
    };

    public string TypeColumn => IsRecycleBin ? Strings.ColumnOriginalLocation : Strings.ColumnType;

    /// <summary>Centered text over the list: an error, or why it's empty.</summary>
    public string? Notice => Error ?? (IsRecycleBin && Items.Count == 0 && _loadedPath is not null ? Strings.BinEmpty : null);

    /// <summary>The Recycle Bin entries behind the selected rows.</summary>
    public IReadOnlyList<RecycledItem> SelectedRecycled =>
        Selected.Select(i => _recycled.GetValueOrDefault(i.FullPath)).OfType<RecycledItem>().ToList();
    /// <summary>
    /// Replaced as a whole when navigating (one change notification instead of one per file);
    /// edited in place when the same folder reloads, so scroll, focus and selection survive.
    /// </summary>
    [ObservableProperty] private ObservableCollection<FileItem> _items = [];
    public ObservableCollection<FileItem> Selected { get; } = [];

    public IReadOnlyList<string> SelectedPaths => Selected.Select(i => i.FullPath).ToList();

    /// <summary>Raised after a reload when the view should select these rows (e.g. pasted files).</summary>
    public event Action<IReadOnlyList<FileItem>>? SelectRequested;

    public FileItem? RenamingItem { get; private set; }

    public bool CanGoBack => _history.CanGoBack;
    public bool CanGoForward => _history.CanGoForward;

    [RelayCommand]
    public Task NavigateTo(string path)
    {
        _history.Push(path);
        return LoadAsync(_history.Current);
    }

    [RelayCommand]
    private Task Back() => _history.Back() is { } p ? LoadAsync(p) : Task.CompletedTask;

    [RelayCommand]
    private Task Forward() => _history.Forward() is { } p ? LoadAsync(p) : Task.CompletedTask;

    [RelayCommand]
    private Task Up() => Locations.Parent(Path) is { } p ? NavigateTo(p) : Task.CompletedTask;

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    /// <summary>Reloads the folder; keeps the current selection unless <paramref name="select"/> names other items.</summary>
    public Task RefreshAsync(IEnumerable<string>? select = null) =>
        LoadAsync(Path, (select ?? Selected.Select(i => i.FullPath)).ToList());

    public void BeginRename()
    {
        CancelRename();
        if (!IsFileSystem || Selected.FirstOrDefault() is not { } item)
            return;
        item.EditName = item.Name;
        item.IsRenaming = true;
        RenamingItem = item;
    }

    public void CancelRename()
    {
        if (RenamingItem is { } item)
            item.IsRenaming = false;
        RenamingItem = null;

        if (_reloadAfterRename)
        {
            _reloadAfterRename = false;
            _ = RefreshAsync();
        }
    }

    [RelayCommand]
    public async Task Open(FileItem? item)
    {
        // Deleted files don't open; Restore brings them back first.
        if (item is null || IsRecycleBin)
            return;
        if (item.IsDirectory)
        {
            await NavigateTo(item.Entry.FullPath);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(item.Entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private async Task LoadAsync(string path, IReadOnlyCollection<string>? select = null)
    {
        var sameFolder = string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase);
        if (!sameFolder)
        {
            RenamingItem = null;
            _loadedPath = null;
        }
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();

        Path = path;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        if (!sameFolder)
            Watch(path);
        var kind = Locations.KindOf(path);

        try
        {
            var entries = kind switch
            {
                LocationKind.ThisPc => await Task.Run(Locations.ReadDrives, cts.Token),
                LocationKind.RecycleBin => await Task.Run(ReadRecycleBin, cts.Token),
                _ => await DirectoryReader.ReadAsync(path, ViewOptions.Current.ShowHidden, cts.Token),
            };
            if (cts.IsCancellationRequested)
                return;

            if (sameFolder)
            {
                SortedSync.Apply(Items, entries, i => i.FullPath, e => e.FullPath, e => new FileItem(e),
                    (item, entry) => item.Update(entry), (item, entry) => item.IsDirectory == entry.IsDirectory);
                if (RenamingItem is { } renaming && !Items.Contains(renaming))
                    RenamingItem = null;
            }
            else
            {
                Items = new ObservableCollection<FileItem>(entries.Select(e => new FileItem(e)));
                if (kind == LocationKind.Folder)
                    RecentPlaces.Visit(path);
            }
            _loadedPath = path;
            Error = null;

            if (select is { Count: > 0 })
            {
                var wanted = new HashSet<string>(select, StringComparer.OrdinalIgnoreCase);
                var found = Items.Where(i => wanted.Contains(i.FullPath)).ToList();
                if (found.Count > 0)
                    SelectRequested?.Invoke(found);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (DirectoryNotFoundException) when (sameFolder && NearestExisting(path) is { } parent)
        {
            // The folder was deleted or renamed under us: step out, like Explorer.
            await NavigateTo(parent);
            return;
        }
        catch (Exception ex)
        {
            _loadedPath = null;
            Items = [];
            Error = ex switch
            {
                UnauthorizedAccessException => Strings.ErrorAccessDenied,
                DirectoryNotFoundException => Strings.ErrorFolderNotFound,
                _ => ex.Message,
            };
        }

        UpdateStatus();
    }

    private IReadOnlyList<FileEntry> ReadRecycleBin()
    {
        var items = OperatingSystem.IsWindows() ? RecycleBinStore.Read(RecycleBin.Folders()) : [];
        var map = new Dictionary<string, RecycledItem>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<FileEntry>(items.Count);
        foreach (var item in items)
        {
            map[item.DataPath] = item;
            entries.Add(new FileEntry(item.Name, item.DataPath, item.IsDirectory, item.IsDirectory ? null : item.Size,
                item.DeletedAt, item.IsDirectory ? FileKind.Folder : FileKinds.FromExtension(System.IO.Path.GetExtension(item.Name)), false,
                item.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                TypeNote: System.IO.Path.GetDirectoryName(item.OriginalPath)));
        }
        _recycled = map;
        return entries;
    }

    private void Watch(string path)
    {
        StopWatching();
        IEnumerable<string> folders = Locations.KindOf(path) switch
        {
            LocationKind.Folder => Directory.Exists(path) ? [path] : [],
            LocationKind.RecycleBin when OperatingSystem.IsWindows() => RecycleBin.Folders(),
            _ => [],
        };
        foreach (var folder in folders)
        {
            var watcher = new FolderWatcher(folder);
            watcher.Changed += () => Dispatcher.UIThread.Post(() => OnDiskChanged(watcher));
            _watchers.Add(watcher);
        }
    }

    private void StopWatching()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
    }

    private void OnDiskChanged(FolderWatcher source)
    {
        if (!_watchers.Contains(source))
            return;
        // Reloading now would close the rename box under the user's fingers.
        if (RenamingItem is not null)
        {
            _reloadAfterRename = true;
            return;
        }
        _ = RefreshAsync();
    }

    private void OnViewOptionsChanged(object? sender, PropertyChangedEventArgs e) => _ = RefreshAsync();

    private static string? NearestExisting(string path)
    {
        for (var p = PathCrumbs.Parent(path); p is not null; p = PathCrumbs.Parent(p))
        {
            if (Directory.Exists(p))
                return p;
        }
        return null;
    }

    public void UpdateStatus()
    {
        OnPropertyChanged(nameof(Notice));
        var text = Strings.ItemsCount(Items.Count);
        if (Selected.Count > 0)
        {
            var bytes = Selected.Sum(i => i.Entry.Size ?? 0);
            text += "  ·  " + Strings.SelectedCount(Selected.Count);
            if (bytes > 0)
                text += $" · {SizeFormatter.Format(bytes)}";
        }
        Status = text;
    }
}
