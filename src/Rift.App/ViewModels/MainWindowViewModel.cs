using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rift.App.Services;
using Rift.Core;
using Rift.Core.Localization;
using Rift.Shell;

namespace Rift.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private LayoutTree<PaneViewModel> _layout = null!;
    /// <summary>Panes by when they were last active, most recent first: "the other pane" is the second one.</summary>
    private readonly List<PaneViewModel> _recent = [];

    public MainWindowViewModel() : this(StartupArgs.Parse([]))
    {
    }

    /// <param name="session">The last session to bring back, used when <paramref name="args"/> name no paths.</param>
    public MainWindowViewModel(StartupArgs args, SessionState? session = null)
    {
        if (!args.HasPaths && session is not null)
        {
            ApplySession(session);
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var left = Track(new PaneViewModel(args.Left.Count > 0 ? [.. args.Left] : [Path.Combine(home, "Downloads"), home]));
            var right = Track(new PaneViewModel(args.Right.Count > 0 ? [.. args.Right] : [Path.GetPathRoot(home) ?? @"C:\"]));
            _layout = new LayoutTree<PaneViewModel>(left);
            _layout.Split(left, right, SplitDirection.Horizontal);
            _activePane = left;
            left.IsActive = true;
            _recent.AddRange([left, right]);
        }
        LoadPinned();
        LoadTags();
        Services.AppSettings.Changed += LoadTags;
        TrackTheme();
        RefreshDrives(devicesChanged: true);
    }

    /// <summary>The split layout; the view rebuilds itself from it on <see cref="LayoutChanged"/>.</summary>
    public LayoutNode<PaneViewModel> Layout => _layout.Root;

    public IReadOnlyList<PaneViewModel> Panes => _layout.Leaves;

    public event Action? LayoutChanged;

    public IReadOnlyList<SidebarItem> QuickAccess { get; } = SidebarItem.QuickAccess();

    public UpdatesViewModel Updates { get; } = new();

    /// <summary>The last run crashed and the user hasn't seen this report yet: a card offers to report it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCrashNotice))]
    private CrashReport? _crashNotice = CrashReports.Latest(Services.AppSettings.Current.CrashNoticeSeen);

    public bool HasCrashNotice => CrashNotice is not null;

    /// <summary>Opens GitHub's "new issue" page with the report filled in; the user reviews it there and sends it.</summary>
    [RelayCommand]
    private async Task ReportCrash()
    {
        if (CrashNotice is not { } report)
            return;
        try
        {
            var url = CrashReports.IssueUrl(report);
            await Task.Run(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            Log.Warn("Opening the issue page failed", ex);
        }
        DismissCrash();
    }

    [RelayCommand]
    private void DismissCrash()
    {
        if (CrashNotice is { } report)
            Services.AppSettings.Update(s => s.CrashNoticeSeen = report.Time);
        CrashNotice = null;
    }

    /// <summary>The logs and crash reports: the tab that already shows them, or a new one in the active pane.</summary>
    [RelayCommand]
    private void OpenLogs()
    {
        Directory.CreateDirectory(Log.Folder);
        Settings.IsOpen = false;
        foreach (var pane in Panes)
        {
            if (pane.Tabs.FirstOrDefault(t => string.Equals(t.Path, Log.Folder, StringComparison.OrdinalIgnoreCase)) is { } tab)
            {
                pane.SelectedTab = tab;
                ActivePane = pane;
                return;
            }
        }
        ActivePane.OpenTab(Log.Folder);
    }
    /// <summary>Filled in the background (see <see cref="RefreshDrives"/>): one sleeping network drive shouldn't hold up the window.</summary>
    [ObservableProperty] private IReadOnlyList<SidebarItem> _drives = [];
    private int _drivesVersion;
    private DateTime _drivesReadAt;

    /// <summary>
    /// Rereads the drives: at startup, when Windows reports a drive plugged in or removed, and when the window
    /// is activated (a share mapped meanwhile; at most every few seconds). Drives that are slow to answer show
    /// by their letter first and get their details when they finally reply. This PC tabs reload too.
    /// </summary>
    public void RefreshDrives(bool devicesChanged = false)
    {
        if (!devicesChanged && DateTime.UtcNow - _drivesReadAt < TimeSpan.FromSeconds(5))
            return;
        _drivesReadAt = DateTime.UtcNow;
        var version = ++_drivesVersion;
        _ = Task.Run(async () =>
        {
            var drives = DriveReader.Read(DriveReader.Patience);
            await Dispatcher.UIThread.InvokeAsync(() => ShowDrives(version, drives, devicesChanged));
            if (drives.Any(d => d.IsSlow))
            {
                var all = DriveReader.Read(TimeSpan.FromMinutes(1));
                await Dispatcher.UIThread.InvokeAsync(() => ShowDrives(version, all, reloadTabs: false));
            }
        });
    }

    private void ShowDrives(int version, IReadOnlyList<DriveFacts> drives, bool reloadTabs)
    {
        if (version != _drivesVersion)
            return;
        var items = SidebarItem.Drives(drives);
        if (!items.SequenceEqual(Drives))
            Drives = items;
        if (reloadTabs)
        {
            foreach (var tab in Panes.SelectMany(p => p.Tabs).Where(t => t.Location == LocationKind.ThisPc))
                _ = tab.RefreshAsync();
        }
    }

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
        LoadTags();
        Services.AppSettings.Changed += LoadTags;
        TrackTheme();
    }

    [RelayCommand]
    private void Unpin(SidebarItem item)
    {
        if (_pinnedFolders.Unpin(item.Path))
            LoadPinned();
        LoadTags();
        Services.AppSettings.Changed += LoadTags;
        TrackTheme();
    }

    [ObservableProperty] private PaneViewModel _activePane = null!;

    /// <summary>Only the active pane is on screen; the others wait in the layout.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTransfer))]
    private bool _isZoomed;

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
        _recent.Remove(newValue);
        _recent.Insert(0, newValue);
        OnPropertyChanged(nameof(OtherPane));
        OnPropertyChanged(nameof(CanTransfer));
        if (IsZoomed)
            LayoutChanged?.Invoke();
    }

    partial void OnIsZoomedChanged(bool value) => LayoutChanged?.Invoke();

    /// <summary>Follows the theme actually on screen (with "follow system" it changes when Windows does).</summary>
    private void TrackTheme()
    {
        if (Application.Current is not { } app)
            return;
        IsDark = app.ActualThemeVariant == ThemeVariant.Dark;
        app.ActualThemeVariantChanged += (_, _) => IsDark = app.ActualThemeVariant == ThemeVariant.Dark;
    }

    /// <summary>Where F5/F6 send files: the pane that was active before this one.</summary>
    public PaneViewModel? OtherPane => _recent.FirstOrDefault(p => p != ActivePane);

    /// <summary>Copy/move to the other pane makes sense: there is one, and it's on screen.</summary>
    public bool CanTransfer => OtherPane is not null && !IsZoomed;

    public bool HasManyPanes => Panes.Count > 1;

    // ---------- layout ----------

    private PaneViewModel Track(PaneViewModel pane)
    {
        pane.CloseRequested = ClosePane;
        pane.TabClosed = RememberClosed;
        return pane;
    }

    /// <summary>Ctrl+\ (right), Ctrl+Shift+\ (down): a new pane next to the active one, on the same folder.</summary>
    [RelayCommand]
    private void SplitRight() => SplitActive(SplitDirection.Horizontal);

    [RelayCommand]
    private void SplitDown() => SplitActive(SplitDirection.Vertical);

    private void SplitActive(SplitDirection direction)
    {
        var pane = Track(new PaneViewModel([ActivePane.SelectedTab.Clone()]));
        AddPane(pane, ActivePane, direction, before: false);
    }

    private void AddPane(PaneViewModel pane, PaneViewModel next, SplitDirection direction, bool before)
    {
        _layout.Split(next, pane, direction, before);
        _recent.Add(pane);
        IsZoomed = false;
        LayoutChanged?.Invoke();
        OnPropertyChanged(nameof(HasManyPanes));
        ActivePane = pane;
        FocusFilesRequested?.Invoke();
    }

    /// <summary>Removes a pane and its tabs; the neighbour takes its space. The last pane stays.</summary>
    public bool ClosePane(PaneViewModel pane)
    {
        if (Panes.Count < 2 || !_layout.Remove(pane))
            return false;
        _recent.Remove(pane);
        if (ActivePane == pane)
            ActivePane = _recent.FirstOrDefault(Panes.Contains) ?? Panes[0];
        foreach (var tab in pane.Tabs.ToList())
            tab.Dispose();
        LayoutChanged?.Invoke();
        OnPropertyChanged(nameof(HasManyPanes));
        OnPropertyChanged(nameof(OtherPane));
        OnPropertyChanged(nameof(CanTransfer));
        FocusFilesRequested?.Invoke();
        return true;
    }

    [RelayCommand]
    private void CloseActivePane() => ClosePane(ActivePane);

    /// <summary>Ctrl+Shift+Enter: show just this pane, or all of them again.</summary>
    [RelayCommand]
    private void ToggleZoom() => IsZoomed = !IsZoomed && HasManyPanes;

    /// <summary>Tab / Shift+Tab: the next pane in reading order.</summary>
    [RelayCommand]
    private void SwitchPane() => FocusPane(+1);

    public void FocusPane(int step)
    {
        if (!HasManyPanes || IsZoomed)
            return;
        ActivePane = _layout.Next(ActivePane, step);
        FocusFilesRequested?.Invoke();
    }

    /// <summary>A layout split moved; keeps the new ratio for the session.</summary>
    public void SetRatio(LayoutSplit<PaneViewModel> split, double ratio) => split.Ratio = ratio;

    // ---------- moving tabs ----------

    /// <summary>A tab dragged onto another pane's tab strip, or into its middle.</summary>
    public void MoveTab(TabViewModel tab, PaneViewModel from, PaneViewModel to, int index)
    {
        if (from == to)
        {
            var current = to.Tabs.IndexOf(tab);
            if (current < 0)
                return;
            to.Tabs.Move(current, Math.Clamp(index > current ? index - 1 : index, 0, to.Tabs.Count - 1));
            to.SelectedTab = tab;
            return;
        }
        var emptied = !from.Detach(tab);
        to.Attach(tab, index);
        if (emptied)
            ClosePaneKeepingTabs(from);
        ActivePane = to;
        FocusFilesRequested?.Invoke();
    }

    /// <summary>A tab dragged to a pane's edge: it becomes a new pane on that side.</summary>
    public void SplitWithTab(TabViewModel tab, PaneViewModel from, PaneViewModel target, TabDropZone zone)
    {
        if (zone == TabDropZone.Center)
        {
            MoveTab(tab, from, target, target.Tabs.Count);
            return;
        }
        // Splitting a pane with its own only tab would change nothing.
        if (from == target && from.Tabs.Count == 1)
            return;

        var emptied = !from.Detach(tab);
        if (emptied)
            ClosePaneKeepingTabs(from);
        var direction = zone is TabDropZone.Left or TabDropZone.Right ? SplitDirection.Horizontal : SplitDirection.Vertical;
        AddPane(Track(new PaneViewModel([tab])), target, direction, before: zone is TabDropZone.Left or TabDropZone.Top);
    }

    /// <summary>The pane gave its last tab away: remove it without disposing that tab.</summary>
    private void ClosePaneKeepingTabs(PaneViewModel pane)
    {
        if (!_layout.Remove(pane))
            return;
        _recent.Remove(pane);
        LayoutChanged?.Invoke();
        OnPropertyChanged(nameof(HasManyPanes));
    }

    /// <summary>Tab menu → "Split right/down": a copy of the tab in a new pane next to this one.</summary>
    public void SplitWithCopy(TabViewModel tab, PaneViewModel pane, SplitDirection direction) =>
        AddPane(Track(new PaneViewModel([tab.Clone()])), pane, direction, before: false);

    /// <summary>Tab menu → "Move to other pane"; splits the window first when there's only one pane.</summary>
    public void MoveTabToOtherPane(TabViewModel tab, PaneViewModel from)
    {
        if (OtherPane is { } other && other != from)
            MoveTab(tab, from, other, other.Tabs.Count);
        else if (from.Tabs.Count > 1)
            SplitWithTab(tab, from, from, TabDropZone.Right);
    }

    private SettingsViewModel? _settings;

    /// <summary>The Settings screen (Ctrl+,), drawn over the window.</summary>
    public SettingsViewModel Settings => _settings ??= new SettingsViewModel(this);

    [RelayCommand]
    private void OpenSettings()
    {
        Palette.Close();
        Settings.OpenCommand.Execute(null);
    }

    // ---------- other windows and the sidebar ----------

    /// <summary>Ctrl+F: the active pane's search box takes focus.</summary>
    public event Action? FocusSearchRequested;

    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke();

    /// <summary>The window comes to the front (another Rift.exe handed over its paths).</summary>
    public event Action? ActivateRequested;

    /// <summary>"Open in Rift" or a jump list item while Rift is already running: new tabs here.</summary>
    public void OpenFromAnotherInstance(StartupArgs args)
    {
        // --left goes to the first pane, --right to the second (made if needed); plain paths to the first.
        var first = Panes[0];
        foreach (var path in args.Left)
            first.OpenTab(path);
        if (args.Right.Count > 0)
        {
            if (Panes.Count < 2)
                AddPane(Track(new PaneViewModel([.. args.Right])), first, SplitDirection.Horizontal, before: false);
            else
            {
                foreach (var path in args.Right)
                    Panes[1].OpenTab(path);
                ActivePane = Panes[1];
            }
        }
        else if (args.Left.Count > 0)
        {
            ActivePane = first;
        }
        IsZoomed &= ActivePane == first;
        ActivateRequested?.Invoke();
    }

    [RelayCommand]
    private Task OpenSidebarItem(SidebarItem item) => ActivePane.SelectedTab.NavigateTo(item.Path);

    [RelayCommand]
    private void ToggleTheme() => Services.AppSettings.Update(s => s.Theme = IsDark ? "light" : "dark");

    public ViewOptions View => ViewOptions.Current;

    [RelayCommand]
    private void ToggleHidden() => View.ShowHidden = !View.ShowHidden;
}
