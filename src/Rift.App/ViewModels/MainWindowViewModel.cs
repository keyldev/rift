using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.Core;
using Rift.Core.Localization;
using Rift.Shell;

namespace Rift.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel() : this(StartupArgs.Parse([]))
    {
    }

    public MainWindowViewModel(StartupArgs args)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Left = new PaneViewModel(args.Left.Count > 0 ? [.. args.Left] : [Path.Combine(home, "Downloads"), home]);
        Right = new PaneViewModel(args.Right.Count > 0 ? [.. args.Right] : [Path.GetPathRoot(home) ?? @"C:\"]);
        _activePane = Left;
        Left.IsActive = true;
        LoadPinned();
    }

    public PaneViewModel Left { get; }
    public PaneViewModel Right { get; }

    public IReadOnlyList<SidebarItem> QuickAccess { get; } = SidebarItem.QuickAccess();
    public IReadOnlyList<SidebarItem> Drives { get; } = SidebarItem.Drives();

    private readonly PinnedFolders _pinnedFolders = new(PinnedFolders.DefaultFile);

    /// <summary>Folders pinned to the top of the sidebar.</summary>
    public ObservableCollection<SidebarItem> Pinned { get; } = [];

    public bool HasPinned => Pinned.Count > 0;

    private void LoadPinned()
    {
        Pinned.Clear();
        foreach (var path in _pinnedFolders.Items)
            Pinned.Add(new SidebarItem(Locations.Title(path), path, "Icon.Folder"));
        OnPropertyChanged(nameof(HasPinned));
    }

    public bool CanPin(string path) => !_pinnedFolders.Contains(path) && Locations.KindOf(path) is LocationKind.Folder or LocationKind.Library;

    [RelayCommand]
    private void Pin(string? path)
    {
        path ??= ActivePane.SelectedTab.Path;
        if (CanPin(path) && _pinnedFolders.Pin(path))
            LoadPinned();
    }

    [RelayCommand]
    private void Unpin(SidebarItem item)
    {
        if (_pinnedFolders.Unpin(item.Path))
            LoadPinned();
    }

    [ObservableProperty] private PaneViewModel _activePane;
    [ObservableProperty] private bool _isSplit = true;
    [ObservableProperty] private bool _isDark = true;

    public bool IsExplorerMenuSupported => OperatingSystem.IsWindows();

    [ObservableProperty]
    private bool _isExplorerMenuEnabled = OperatingSystem.IsWindows() && ExplorerIntegration.IsRegistered();

    partial void OnIsExplorerMenuEnabledChanged(bool value)
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (value)
            ExplorerIntegration.Register(Environment.ProcessPath!, Strings.ExplorerMenuText);
        else
            ExplorerIntegration.Unregister();
    }

    partial void OnActivePaneChanged(PaneViewModel? oldValue, PaneViewModel newValue)
    {
        if (oldValue is not null)
            oldValue.IsActive = false;
        newValue.IsActive = true;
    }

    partial void OnIsSplitChanged(bool value)
    {
        if (!value)
            ActivePane = Left;
    }

    partial void OnIsDarkChanged(bool value)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public PaneViewModel OtherPane => ActivePane == Left ? Right : Left;

    /// <summary>Ctrl+F: the active pane's search box takes focus.</summary>
    public event Action? FocusSearchRequested;

    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke();

    /// <summary>The window comes to the front (another Rift.exe handed over its paths).</summary>
    public event Action? ActivateRequested;

    /// <summary>"Open in Rift" or a jump list item while Rift is already running: new tabs here.</summary>
    public void OpenFromAnotherInstance(StartupArgs args)
    {
        foreach (var path in args.Left)
            Left.OpenTab(path);
        foreach (var path in args.Right)
            Right.OpenTab(path);

        if (args.Right.Count > 0)
        {
            IsSplit = true;
            ActivePane = Right;
        }
        else if (args.Left.Count > 0)
        {
            ActivePane = Left;
        }
        ActivateRequested?.Invoke();
    }

    [RelayCommand]
    private Task OpenSidebarItem(SidebarItem item) => ActivePane.SelectedTab.NavigateTo(item.Path);

    [RelayCommand]
    private void ToggleTheme() => IsDark = !IsDark;

    public ViewOptions View => ViewOptions.Current;

    [RelayCommand]
    private void ToggleHidden() => View.ShowHidden = !View.ShowHidden;

    [RelayCommand]
    private void ShowSingle() => IsSplit = false;

    [RelayCommand]
    private void ShowSplit() => IsSplit = true;

    [RelayCommand]
    private void SwitchPane()
    {
        if (IsSplit)
            ActivePane = OtherPane;
    }
}
