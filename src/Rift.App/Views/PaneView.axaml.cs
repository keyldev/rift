using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Rift.App.Controls;
using Rift.App.Services;
using Rift.App.ViewModels;
using Rift.Core;
using Rift.Core.Localization;
using Rift.Core.Operations;

namespace Rift.App.Views;

public partial class PaneView : UserControl
{
    /// <summary>Marks drags that started in Rift, so we know the source pane and skip shell round-trips.</summary>
    private static readonly DataFormat<RiftDrag> RiftFormat = DataFormat.CreateInProcessFormat<RiftDrag>("rift-files");

    private sealed record RiftDrag(PaneViewModel Source, IReadOnlyList<string> Paths);

    private const double DragThreshold = 6;

    private PaneViewModel? _pane;
    private TabViewModel? _tab;
    private PointerPressedEventArgs? _pressed;
    private Point _pressedAt;
    private FileItem? _selectOnRelease;

    public PaneView()
    {
        InitializeComponent();

        // A click anywhere in the pane activates it, even if a child control already handled the event.
        AddHandler(PointerPressedEvent, (_, _) => Activate(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => Activate(), RoutingStrategies.Bubble);

        Files.DoubleTapped += OnDoubleTapped;
        Files.KeyDown += OnFilesKeyDown;
        Files.SelectionChanged += (_, _) => SyncSelection();
        // Icons load lazily, for the rows that actually get on screen.
        Files.ContainerPrepared += (_, e) =>
        {
            // ItemFromContainer isn't wired up yet at this point; the DataContext is.
            if (e.Container.DataContext is FileItem item)
                FileIcons.Request(item);
        };

        // Inline rename: Enter commits, Esc cancels, clicking away commits.
        Files.AddHandler(KeyDownEvent, OnRenameKey, RoutingStrategies.Tunnel);
        Files.AddHandler(LostFocusEvent, OnRenameLostFocus, RoutingStrategies.Bubble);

        // Dragging rows out.
        Files.AddHandler(PointerPressedEvent, OnRowPressed, RoutingStrategies.Tunnel);
        Files.AddHandler(PointerMovedEvent, OnRowMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        Files.AddHandler(PointerReleasedEvent, OnRowReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Right-click menu: Rift's actions first, Explorer's full menu one click away.
        Files.AddHandler(PointerPressedEvent, OnRightPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Files.ContextRequested += OnContextRequested;

        // Dropping files in, from Rift or from Explorer.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private PaneViewModel? Pane => DataContext as PaneViewModel;
    private MainWindowViewModel? Main => TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    public void FocusFiles()
    {
        if (Files.ContainerFromItem(Files.SelectedItem ?? new object()) is { } container)
            container.Focus();
        else
            Files.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_pane is not null)
            _pane.PropertyChanged -= OnPanePropertyChanged;
        _pane = Pane;
        if (_pane is not null)
            _pane.PropertyChanged += OnPanePropertyChanged;
        WatchTab();
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.SelectedTab))
            WatchTab();
    }

    private void WatchTab()
    {
        if (_tab is not null)
            _tab.SelectRequested -= ApplySelection;
        _tab = _pane?.SelectedTab;
        if (_tab is not null)
            _tab.SelectRequested += ApplySelection;
    }

    private void ApplySelection(IReadOnlyList<FileItem> items)
    {
        Files.SelectedItems?.Clear();
        foreach (var item in items)
            Files.SelectedItems?.Add(item);
        if (items.Count > 0)
            Files.ScrollIntoView(items[0]);
    }

    private void Activate()
    {
        if (Main is { } main && Pane is { } pane)
            main.ActivePane = pane;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsInsideRenameBox(e.Source))
            return;
        OpenSelected();
    }

    private void OpenSelected()
    {
        if (Pane?.SelectedTab is { } tab && Files.SelectedItem is FileItem item)
            _ = tab.Open(item);
    }

    private void OnFilesKeyDown(object? sender, KeyEventArgs e)
    {
        if (Pane?.SelectedTab is not { } tab)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                OpenSelected();
                e.Handled = true;
                break;
            case Key.Back:
                tab.UpCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void SyncSelection()
    {
        if (Pane?.SelectedTab is not { } tab)
            return;
        tab.Selected.Clear();
        if (Files.SelectedItems is { } items)
        {
            foreach (var item in items.OfType<FileItem>())
                tab.Selected.Add(item);
        }
        tab.UpdateStatus();
    }

    // ---------- rename ----------

    private void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is not RenameBox { DataContext: FileItem item } || _tab is not { } tab)
            return;

        if (e.Key == Key.Enter)
        {
            _ = Main?.CommitRenameAsync(tab, item);
            e.Handled = true;
            FocusFiles();
        }
        else if (e.Key == Key.Escape)
        {
            tab.CancelRename();
            e.Handled = true;
            FocusFiles();
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is RenameBox { DataContext: FileItem { IsRenaming: true } item } && _tab is { } tab)
            _ = Main?.CommitRenameAsync(tab, item);
    }

    private static bool IsInsideRenameBox(object? source) =>
        source is Visual v && v.FindAncestorOfType<RenameBox>(includeSelf: true) is not null;

    // ---------- context menu ----------

    private bool _shiftRightClick;

    private void OnRightPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(Files).Properties.IsRightButtonPressed)
            _shiftRightClick = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Main is not { } main || _tab is not { } tab || IsInsideRenameBox(e.Source))
            return;
        e.Handled = true;
        Activate();

        // Right-click on a row selects it, unless it's part of the current selection; on empty space, clears it.
        var row = RowItem(e.Source);
        if (row is not null && Files.SelectedItems?.Contains(row) != true)
        {
            Files.SelectedItems?.Clear();
            Files.SelectedItems?.Add(row);
        }
        else if (row is null && e.TryGetPosition(Files, out _))
        {
            Files.SelectedItems?.Clear();
        }
        var forSelection = tab.Selected.Count > 0;

        // Where the menu goes: the pointer, or the focused row when opened from the keyboard (Shift+F10, Menu key).
        Control anchor = Files;
        if (!e.TryGetPosition(Files, out var point))
        {
            anchor = (forSelection ? Files.ContainerFromItem(tab.Selected[0]) : null) ?? Files;
            point = anchor.TranslatePoint(new Point(24, anchor.Bounds.Height), Files) ?? default;
        }
        var screen = Files.PointToScreen(point);

        // Shift+right-click goes straight to Explorer's menu with extended verbs, as in Explorer.
        if (_shiftRightClick && OperatingSystem.IsWindows())
        {
            _shiftRightClick = false;
            main.ShowShellMenu(forSelection, screen.X, screen.Y, extended: true);
            return;
        }

        var menu = new ContextMenu
        {
            ItemsSource = tab.Location switch
            {
                LocationKind.RecycleBin => RecycleBinMenu(main, forSelection),
                _ when forSelection => SelectionMenu(main, tab, screen),
                LocationKind.ThisPc => [Item(Strings.MenuRefresh, "Icon.Refresh", "Ctrl+R", tab.RefreshCommand)],
                _ => FolderMenu(main, screen),
            },
            Placement = anchor == Files && e.TryGetPosition(Files, out _) ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft,
        };
        menu.Closed += (_, _) => FocusFiles();
        menu.Open(anchor);
    }

    private List<Control> SelectionMenu(MainWindowViewModel main, TabViewModel tab, PixelPoint screen)
    {
        var folders = tab.Selected.Count(i => i.IsDirectory);
        var files = tab.Selected.Count - folders;
        var items = new List<Control>
        {
            Item(Strings.MenuOpen, null, null, main.OpenItemCommand, isDefault: true),
        };
        if (folders > 0)
        {
            items.Add(Item(Strings.MenuOpenNewTab, "Icon.Plus", null, main.OpenInNewTabCommand));
            items.Add(Item(Strings.MenuOpenOtherPane, "Icon.Split", null, main.OpenInOtherPaneCommand));
        }
        if (files == 1 && folders == 0 && OperatingSystem.IsWindows())
            items.Add(Item(Strings.MenuOpenWith, "Icon.OpenWith", null, main.OpenWithCommand));

        // This PC: drives can be opened and inspected, not cut, renamed or deleted.
        if (!tab.IsFileSystem)
        {
            items.Add(new Separator());
            items.Add(Item(Strings.MenuCopyPath, "Icon.Link", "Ctrl+Shift+C", main.CopyPathCommand));
            AddShellItems(items, main, forSelection: true, screen);
            return items;
        }

        items.Add(new Separator());
        items.Add(Item(Strings.MenuCut, "Icon.Cut", "Ctrl+X", main.CutToClipboardCommand));
        items.Add(Item(Strings.MenuCopy, "Icon.Copy", "Ctrl+C", main.CopyToClipboardCommand));
        if (main.IsSplit)
        {
            items.Add(Item(Strings.MenuCopyToOther, "Icon.Copy", "F5", main.CopyToOtherCommand));
            items.Add(Item(Strings.MenuMoveToOther, "Icon.Move", "F6", main.MoveToOtherCommand));
        }
        items.Add(Item(Strings.MenuCopyPath, "Icon.Link", "Ctrl+Shift+C", main.CopyPathCommand));

        items.Add(new Separator());
        items.Add(Item(Strings.MenuRename, "Icon.Rename", "F2", main.RenameCommand, enabled: tab.Selected.Count == 1));
        items.Add(Item(Strings.MenuDelete, "Icon.Trash", "Delete", main.DeleteCommand));

        AddShellItems(items, main, forSelection: true, screen);
        return items;
    }

    private List<Control> FolderMenu(MainWindowViewModel main, PixelPoint screen)
    {
        var hidden = Item(Strings.MenuShowHidden, null, "Ctrl+H", main.ToggleHiddenCommand);
        hidden.ToggleType = MenuItemToggleType.CheckBox;
        hidden.IsChecked = main.View.ShowHidden;

        var items = new List<Control>
        {
            Item(Strings.MenuNewFolder, "Icon.Folder", "Ctrl+Shift+N", main.NewFolderCommand),
            Item(Strings.MenuPaste, "Icon.Paste", "Ctrl+V", main.PasteCommand, enabled: main.CanPaste),
            new Separator(),
            Item(Strings.MenuRefresh, "Icon.Refresh", "Ctrl+R", _tab!.RefreshCommand),
            hidden,
            Item(Strings.MenuCopyPath, "Icon.Link", "Ctrl+Shift+C", main.CopyPathCommand),
        };
        AddShellItems(items, main, forSelection: false, screen);
        return items;
    }

    private static List<Control> RecycleBinMenu(MainWindowViewModel main, bool forSelection)
    {
        var items = new List<Control>();
        if (forSelection)
        {
            items.Add(Item(Strings.MenuRestore, "Icon.Restore", null, main.RestoreCommand, isDefault: true));
            items.Add(Item(Strings.MenuDeletePermanently, "Icon.Trash", "Delete", main.DeleteCommand));
            items.Add(Item(Strings.MenuCopyPath, "Icon.Link", "Ctrl+Shift+C", main.CopyPathCommand));
            items.Add(new Separator());
        }
        items.Add(Item(Strings.MenuEmptyBin, "Icon.Trash", null, main.EmptyBinCommand));
        return items;
    }

    private static void AddShellItems(List<Control> items, MainWindowViewModel main, bool forSelection, PixelPoint screen)
    {
        if (!OperatingSystem.IsWindows())
            return;
        items.Add(new Separator());
        items.Add(Item(Strings.MenuProperties, "Icon.Info", null, main.PropertiesCommand));
        var more = Item(Strings.MenuMoreOptions, "Icon.More", null, null);
        // Let our menu close first: Explorer's menu runs its own modal loop.
        more.Click += (_, _) => Dispatcher.UIThread.Post(() => main.ShowShellMenu(forSelection, screen.X, screen.Y, extended: false));
        items.Add(more);
    }

    private static MenuItem Item(string header, string? icon, string? gesture, System.Windows.Input.ICommand? command,
        bool enabled = true, bool isDefault = false)
    {
        var item = new MenuItem
        {
            Header = header,
            Command = command,
            IsEnabled = enabled,
            InputGesture = gesture is null ? null : KeyGesture.Parse(gesture),
            FontWeight = isDefault ? FontWeight.SemiBold : FontWeight.Normal,
        };
        if (icon is not null && Application.Current?.TryFindResource(icon, out var geometry) == true)
            item.Icon = new Icon { Data = geometry as Geometry, Width = 16, Height = 16 };
        return item;
    }

    // ---------- drag out ----------

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;
        _selectOnRelease = null;
        if (IsInsideRenameBox(e.Source) || !e.GetCurrentPoint(Files).Properties.IsLeftButtonPressed)
            return;
        if (RowItem(e.Source) is not { } item)
            return;

        _pressed = e;
        _pressedAt = e.GetPosition(Files);

        // Pressing a row that's part of a multi-selection must not collapse the selection,
        // otherwise you can't drag several files. Collapse on release instead, if no drag happened.
        if (e.KeyModifiers == KeyModifiers.None && Files.SelectedItems is { Count: > 1 } selected && selected.Contains(item))
        {
            _selectOnRelease = item;
            e.Handled = true;
        }
    }

    private async void OnRowMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } pressed || _pane is not { } pane || _tab is not { } tab)
            return;
        if (!e.GetCurrentPoint(Files).Properties.IsLeftButtonPressed)
        {
            _pressed = null;
            return;
        }

        var delta = e.GetPosition(Files) - _pressedAt;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        _pressed = null;
        _selectOnRelease = null;
        var paths = tab.SelectedPaths;
        // Drives and deleted files don't travel; the Recycle Bin's own Restore puts files back.
        if (paths.Count == 0 || !tab.IsFileSystem)
            return;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(RiftFormat, new RiftDrag(pane, paths)));
        if (TopLevel.GetTopLevel(this)?.StorageProvider is { } storage)
        {
            foreach (var path in paths)
            {
                IStorageItem? item = Directory.Exists(path)
                    ? await storage.TryGetFolderFromPathAsync(path)
                    : await storage.TryGetFileFromPathAsync(path);
                if (item is not null)
                    data.Add(DataTransferItem.CreateFile(item));
            }
        }

        var effect = await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Copy | DragDropEffects.Move);

        // Explorer performs moves itself; reload so the moved files disappear here.
        if (effect.HasFlag(DragDropEffects.Move))
            await tab.RefreshAsync();
    }

    private void OnRowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_selectOnRelease is { } item)
        {
            Files.SelectedItems?.Clear();
            Files.SelectedItems?.Add(item);
        }
        _selectOnRelease = null;
        _pressed = null;
    }

    private static FileItem? RowItem(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as FileItem;

    // ---------- drop in ----------

    private sealed record DropPlan(IReadOnlyList<string> Paths, string Destination, bool Copy);

    private DropPlan? Plan(DragEventArgs e)
    {
        if (_tab is not { IsFileSystem: true } tab)
            return null;

        IReadOnlyList<string> paths = e.DataTransfer.TryGetValue(RiftFormat) is { } own
            ? own.Paths
            : e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        if (paths.Count == 0)
            return null;

        // Dropping onto a folder row puts the files inside that folder.
        var destination = RowItem(e.Source) is { IsDirectory: true } row && !paths.Contains(row.FullPath, StringComparer.OrdinalIgnoreCase)
            ? row.FullPath
            : tab.Path;

        var sameFolder = paths.All(p => string.Equals(Path.GetDirectoryName(p), destination.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(Path.GetDirectoryName(p), destination, StringComparison.OrdinalIgnoreCase));
        var intoItself = paths.Any(p => Directory.Exists(p) && IsSameOrInside(destination, p));
        if (sameFolder || intoItself)
            return null;

        // Explorer's rule: same drive moves, another drive copies. Ctrl forces copy, Shift forces move.
        var copy = e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                   (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !FileTransfer.SameVolume(paths[0], destination));
        if (!e.DragEffects.HasFlag(DragDropEffects.Move))
            copy = true;
        return new DropPlan(paths, destination, copy);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var plan = Plan(e);
        e.DragEffects = plan is null ? DragDropEffects.None : plan.Copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
        if (_pane is not { } pane)
            return;

        pane.IsDropTarget = plan is not null;
        if (plan is null)
            return;
        var name = PathCrumbs.Title(plan.Destination);
        pane.DropIsCopy = plan.Copy;
        pane.DropTitle = plan.Copy ? Strings.DropCopy(name) : Strings.DropMove(name);
        pane.DropHint = plan.Copy ? Strings.DropHintCopy : Strings.DropHintMove;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (_pane is not null)
            _pane.IsDropTarget = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_pane is not null)
            _pane.IsDropTarget = false;
        var plan = Plan(e);
        e.DragEffects = plan is null ? DragDropEffects.None : plan.Copy ? DragDropEffects.Copy : DragDropEffects.Move;
        e.Handled = true;
        if (plan is null || Main is not { } main)
            return;

        Activate();
        _ = main.TransferAsync(plan.Copy ? TransferMode.Copy : TransferMode.Move, plan.Paths, plan.Destination);
    }

    private static bool IsSameOrInside(string path, string folder)
    {
        var f = folder.TrimEnd('\\') + '\\';
        return (path.TrimEnd('\\') + '\\').StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }
}
