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

public enum ViewMode
{
    Details,
    Tiles,
    Thumbnails,
}

public enum SortState
{
    None,
    Ascending,
    Descending,
}

public partial class TabViewModel : ViewModelBase, IDisposable
{
    private readonly NavigationHistory _history;
    private CancellationTokenSource? _loading;
    private readonly List<FolderWatcher> _watchers = [];
    /// <summary>In the Recycle Bin view: what each row stands for, by its $R path.</summary>
    private Dictionary<string, RecycledItem> _recycled = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The folder whose listing is on screen; reloading it edits the list instead of replacing it.</summary>
    private string? _loadedPath;
    /// <summary>Everything the last read returned; sorting and filtering are views over it, no disk access.</summary>
    private IReadOnlyList<FileEntry> _entries = [];
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
        nameof(IsRecycleBin), nameof(IsSearch), nameof(CanEditItems), nameof(IsPlaceList), nameof(DateColumn), nameof(TypeColumn), nameof(SearchRoot))]
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
    public bool IsSearch => Location == LocationKind.Search;
    /// <summary>The rows are real files that can be copied, moved, renamed, deleted: a folder, search results, a library.</summary>
    public bool CanEditItems => Location is LocationKind.Folder or LocationKind.Search or LocationKind.Library;
    /// <summary>The rows are places to open (drives, computers, shares, libraries), not files.</summary>
    public bool IsPlaceList => Location is LocationKind.ThisPc or LocationKind.Network or LocationKind.Server or LocationKind.Libraries;
    /// <summary>Where Ctrl+F searches from this tab: the folder itself, or the root of the current search.</summary>
    public string? SearchRoot => Location switch
    {
        LocationKind.Folder => Path,
        LocationKind.Search => Locations.ParseSearch(Path)!.Value.Root,
        _ => null,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetails), nameof(IsTiles), nameof(IsThumbnails))]
    private ViewMode _viewMode = ViewMode.Details;

    public bool IsDetails => ViewMode == ViewMode.Details;
    public bool IsTiles => ViewMode == ViewMode.Tiles;
    public bool IsThumbnails => ViewMode == ViewMode.Thumbnails;

    [RelayCommand]
    private void SetView(ViewMode mode) => ViewMode = mode;

    /// <summary>Ctrl+wheel: details → tiles → thumbnails and back.</summary>
    public void StepView(int delta) => ViewMode = (ViewMode)Math.Clamp((int)ViewMode + delta, 0, 2);

    /// <summary>A search is walking the disk.</summary>
    [ObservableProperty] private bool _isBusy;
    private string? _searchStatus;

    public string DateColumn => Location switch
    {
        LocationKind.ThisPc => Strings.ColumnFree,
        LocationKind.RecycleBin => Strings.ColumnDeleted,
        _ => Strings.ColumnModified,
    };

    public string TypeColumn => Location switch
    {
        LocationKind.RecycleBin => Strings.ColumnOriginalLocation,
        LocationKind.Search => Strings.ColumnLocation,
        _ => Strings.ColumnType,
    };

    /// <summary>Centered text over the list: an error, or why it's empty.</summary>
    public string? Notice => Error ?? (Items.Count > 0 || _loadedPath is null || IsBusy ? null : Location switch
    {
        LocationKind.RecycleBin => Strings.BinEmpty,
        LocationKind.Search when !IsFiltered => Strings.SearchNothing,
        LocationKind.Network => Strings.NetworkEmpty,
        LocationKind.Server => Strings.ServerEmpty,
        _ => null,
    });

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSort), nameof(ModifiedSort), nameof(CreatedSort), nameof(TypeSort), nameof(SizeSort))]
    private SortColumn _sortColumn = SortColumn.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSort), nameof(ModifiedSort), nameof(CreatedSort), nameof(TypeSort), nameof(SizeSort))]
    private bool _sortDescending;

    // The arrow on each column header.
    public SortState NameSort => StateOf(SortColumn.Name);
    public SortState ModifiedSort => StateOf(SortColumn.Modified);
    public SortState CreatedSort => StateOf(SortColumn.Created);
    public SortState TypeSort => StateOf(SortColumn.Type);
    public SortState SizeSort => StateOf(SortColumn.Size);

    private SortState StateOf(SortColumn column) =>
        column != SortColumn ? SortState.None : SortDescending ? SortState.Descending : SortState.Ascending;

    /// <summary>Header click: sort by that column, or flip the direction if it already is. Dates and sizes start with the largest.</summary>
    public void SortBy(SortColumn column)
    {
        if (column == SortColumn)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = column is SortColumn.Modified or SortColumn.Created or SortColumn.Size;
        }
        ShowEntries(inPlace: false, Selected.Select(i => i.FullPath).ToList());
    }

    /// <summary>Type-to-filter: only names containing this (or matching a * ? pattern) stay visible.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltered))]
    private string _filter = "";

    public bool IsFiltered => Filter.Length > 0;

    partial void OnFilterChanged(string value)
    {
        if (_loadedPath is not null)
            ShowEntries(inPlace: true, Selected.Select(i => i.FullPath).ToList());
        UpdateStatus();
    }

    /// <summary>The breadcrumbs turned into a text box (click on the bar, Ctrl+L, Alt+D, F4).</summary>
    [ObservableProperty] private bool _isEditingAddress;
    [ObservableProperty] private string _addressText = "";

    [RelayCommand]
    private void EditAddress()
    {
        AddressText = Path;
        IsEditingAddress = true;
    }

    public void CancelAddress() => IsEditingAddress = false;

    /// <summary>Goes where the typed text points; a file opens its folder with the file selected.</summary>
    /// <returns>False if nothing is there (the box stays open so the text can be fixed).</returns>
    public async Task<bool> CommitAddressAsync(string text)
    {
        var (kind, target) = AddressResolver.Resolve(text, Path);
        switch (kind)
        {
            case AddressKind.Folder:
                IsEditingAddress = false;
                await NavigateTo(target);
                return true;
            case AddressKind.File when System.IO.Path.GetDirectoryName(target) is { } folder:
                IsEditingAddress = false;
                await NavigateTo(folder);
                await RefreshAsync([target]);
                return true;
            default:
                return false;
        }
    }

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

    /// <summary>Ctrl+R: reread the folder; in a search tab, run the search again.</summary>
    [RelayCommand]
    private Task Refresh() => LoadAsync(Path, Selected.Select(i => i.FullPath).ToList(), rerun: true);

    /// <summary>Reloads the folder; keeps the current selection unless <paramref name="select"/> names other items.</summary>
    public Task RefreshAsync(IEnumerable<string>? select = null) =>
        LoadAsync(Path, (select ?? Selected.Select(i => i.FullPath)).ToList());

    public void BeginRename()
    {
        CancelRename();
        if (!CanEditItems || Selected.FirstOrDefault() is not { } item)
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

    private async Task LoadAsync(string path, IReadOnlyCollection<string>? select = null, bool rerun = false)
    {
        var sameFolder = string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase);
        if (!sameFolder)
        {
            RenamingItem = null;
            _loadedPath = null;
            Filter = ""; // a filter belongs to the folder it was typed in
            if (Locations.KindOf(path) != Locations.KindOf(Path))
                ResetSort(Locations.KindOf(path));
        }
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();

        Path = path;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        if (!sameFolder)
            Watch(path);
        var kind = Locations.KindOf(path);

        if (kind == LocationKind.Search)
        {
            await SearchAsync(path, revalidate: sameFolder && !rerun, select, cts.Token);
            return;
        }

        try
        {
            var entries = kind switch
            {
                LocationKind.ThisPc => await Task.Run(Locations.ReadDrives, cts.Token),
                LocationKind.RecycleBin => await Task.Run(ReadRecycleBin, cts.Token),
                LocationKind.Network or LocationKind.Server or LocationKind.Libraries or LocationKind.Library
                    => await Places.ReadAsync(path, ViewOptions.Current.ShowHidden, cts.Token),
                _ => await DirectoryReader.ReadAsync(path, ViewOptions.Current.ShowHidden, cts.Token),
            };
            if (cts.IsCancellationRequested)
                return;

            _entries = entries;
            ShowEntries(sameFolder, select);
            if (!sameFolder && kind == LocationKind.Folder)
                RecentPlaces.Visit(path);
            _loadedPath = path;
            Error = null;
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
            _entries = [];
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

    /// <summary>
    /// Runs a search and shows hits as they come. <paramref name="revalidate"/> instead just drops results
    /// that are gone and updates the rest: after a delete or move there's no need to walk the disk again.
    /// </summary>
    private async Task SearchAsync(string path, bool revalidate, IReadOnlyCollection<string>? select, CancellationToken ct)
    {
        var (root, query) = Locations.ParseSearch(path)!.Value;
        try
        {
            if (revalidate)
            {
                var current = _entries;
                _entries = await Task.Run(() => current.Select(Restat).OfType<FileEntry>().ToList(), ct);
                ShowEntries(inPlace: true, select);
                _searchStatus = Strings.SearchDone(_entries.Count);
                UpdateStatus();
                return;
            }

            _entries = [];
            Items = [];
            Error = null;
            _loadedPath = path;
            IsBusy = true;
            _searchStatus = Strings.SearchRunning(0);
            UpdateStatus();

            var found = new List<FileEntry>();
            var showHidden = ViewOptions.Current.ShowHidden;
            var capped = await Task.Run(() => FileSearch.Run(root, query, showHidden, batch => Dispatcher.UIThread.Post(() =>
            {
                if (ct.IsCancellationRequested)
                    return;
                found.AddRange(batch);
                _entries = [.. found];
                ShowEntries(inPlace: true, null);
                _searchStatus = Strings.SearchRunning(found.Count);
                UpdateStatus();
            }), ct), ct);

            // Let the last batch posted above land before the totals.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (ct.IsCancellationRequested)
                return;
            _searchStatus = capped ? Strings.SearchCapped(_entries.Count) : Strings.SearchDone(_entries.Count);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _loadedPath = null;
            _entries = [];
            Items = [];
            Error = ex is DirectoryNotFoundException ? Strings.ErrorFolderNotFound : ex.Message;
            _searchStatus = null;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsBusy = false;
        }
        UpdateStatus();
    }

    /// <summary>Fresh facts for a search hit, or null if it's gone.</summary>
    private static FileEntry? Restat(FileEntry e)
    {
        FileSystemInfo info = e.IsDirectory ? new DirectoryInfo(e.FullPath) : new FileInfo(e.FullPath);
        return info.Exists ? FileEntry.From(info) with { TypeNote = e.TypeNote } : null;
    }

    /// <summary>A search hit was renamed: it keeps its row, under the new name.</summary>
    public void ReplaceEntry(string oldPath, string newPath)
    {
        _entries = _entries
            .Select(e => string.Equals(e.FullPath, oldPath, StringComparison.OrdinalIgnoreCase) && Restat(e with { FullPath = newPath }) is { } fresh
                ? fresh
                : e)
            .ToList();
        ShowEntries(inPlace: true, [newPath]);
    }

    /// <summary>Puts the sorted, filtered entries on screen: edited in place, or as a new list.</summary>
    private void ShowEntries(bool inPlace, IReadOnlyCollection<string>? select)
    {
        var filter = new NameFilter(Filter);
        var view = FileSorter.Sort(filter.IsEmpty ? _entries : _entries.Where(e => filter.Matches(e.Name)), SortColumn, SortDescending);

        if (inPlace)
        {
            SortedSync.Apply(Items, view, i => i.FullPath, e => e.FullPath, e => new FileItem(e),
                (item, entry) => item.Update(entry), (item, entry) => item.IsDirectory == entry.IsDirectory);
            if (RenamingItem is { } renaming && !Items.Contains(renaming))
                RenamingItem = null;
        }
        else
        {
            Items = new ObservableCollection<FileItem>(view.Select(e => new FileItem(e)));
        }

        if (select is { Count: > 0 })
        {
            var wanted = new HashSet<string>(select, StringComparer.OrdinalIgnoreCase);
            var found = Items.Where(i => wanted.Contains(i.FullPath)).ToList();
            if (found.Count > 0)
                SelectRequested?.Invoke(found);
        }
        UpdateStatus();
    }

    private void ResetSort(LocationKind kind)
    {
        // The Recycle Bin is most useful newest-deleted first; everything else starts by name.
        SortColumn = kind == LocationKind.RecycleBin ? SortColumn.Modified : SortColumn.Name;
        SortDescending = kind == LocationKind.RecycleBin;
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
            LocationKind.Library => Places.LibraryFolders(path),
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

    private void OnViewOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewOptions.ShowHidden))
            _ = RefreshAsync();
    }

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
        var text = IsSearch && _searchStatus is { } search
            ? (IsFiltered ? $"{search} · {Strings.FilteredCount(Items.Count, _entries.Count)}" : search)
            : IsFiltered ? Strings.FilteredCount(Items.Count, _entries.Count) : Strings.ItemsCount(Items.Count);
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
