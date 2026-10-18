using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.Core;
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
    }

    public PaneViewModel Left { get; }
    public PaneViewModel Right { get; }

    public IReadOnlyList<SidebarItem> QuickAccess { get; } = SidebarItem.QuickAccess();
    public IReadOnlyList<SidebarItem> Drives { get; } = SidebarItem.Drives();

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
            ExplorerIntegration.Register(Environment.ProcessPath!);
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

    [RelayCommand]
    private Task OpenSidebarItem(SidebarItem item) => ActivePane.SelectedTab.NavigateTo(item.Path);

    [RelayCommand]
    private void ToggleTheme() => IsDark = !IsDark;

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
