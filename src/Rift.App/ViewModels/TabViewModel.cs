using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.Core;
using Rift.Core.Localization;

namespace Rift.App.ViewModels;

public sealed record CrumbItem(string Label, string Path, bool HasSeparator);

public partial class TabViewModel : ViewModelBase
{
    private readonly NavigationHistory _history;
    private CancellationTokenSource? _loading;

    public TabViewModel(string path)
    {
        _history = new NavigationHistory(path);
        _ = LoadAsync(path);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Crumbs))]
    private string _path = "";

    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _status = "";

    public string Title => PathCrumbs.Title(Path);
    public IReadOnlyList<CrumbItem> Crumbs =>
        PathCrumbs.Split(Path).Select((c, i) => new CrumbItem(c.Label, c.Path, i > 0)).ToList();
    /// <summary>Replaced as a whole on every load: one change notification instead of one per file.</summary>
    [ObservableProperty] private IReadOnlyList<FileItem> _items = [];
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
    private Task Up() => PathCrumbs.Parent(Path) is { } p ? NavigateTo(p) : Task.CompletedTask;

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    /// <summary>Reloads the folder; keeps the current selection unless <paramref name="select"/> names other items.</summary>
    public Task RefreshAsync(IEnumerable<string>? select = null) =>
        LoadAsync(Path, (select ?? Selected.Select(i => i.FullPath)).ToList());

    public void BeginRename()
    {
        CancelRename();
        if (Selected.FirstOrDefault() is not { } item)
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
    }

    [RelayCommand]
    public async Task Open(FileItem? item)
    {
        if (item is null)
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
        RenamingItem = null;
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();

        Path = path;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));

        try
        {
            var entries = await DirectoryReader.ReadAsync(path, ct: cts.Token);
            if (cts.IsCancellationRequested)
                return;

            var items = entries.Select(e => new FileItem(e)).ToList();
            Items = items;
            Error = null;

            if (select is { Count: > 0 })
            {
                var wanted = new HashSet<string>(select, StringComparer.OrdinalIgnoreCase);
                var found = items.Where(i => wanted.Contains(i.FullPath)).ToList();
                if (found.Count > 0)
                    SelectRequested?.Invoke(found);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
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

    public void UpdateStatus()
    {
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
