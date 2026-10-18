using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.Core;

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
    public ObservableCollection<FileItem> Items { get; } = [];
    public ObservableCollection<FileItem> Selected { get; } = [];

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
    private Task Refresh() => LoadAsync(Path);

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

    private async Task LoadAsync(string path)
    {
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

            Items.Clear();
            foreach (var e in entries)
                Items.Add(new FileItem(e));
            Error = null;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Items.Clear();
            Error = ex is UnauthorizedAccessException ? "Нет доступа к этой папке" : ex.Message;
        }

        UpdateStatus();
    }

    public void UpdateStatus()
    {
        var text = $"{Items.Count} элем.";
        if (Selected.Count > 0)
        {
            var bytes = Selected.Sum(i => i.Entry.Size ?? 0);
            text += $"  ·  выбрано {Selected.Count}";
            if (bytes > 0)
                text += $" · {SizeFormatter.Format(bytes)}";
        }
        Status = text;
    }
}
