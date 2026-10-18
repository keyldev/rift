using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Rift.App.Controls;
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

        // Inline rename: Enter commits, Esc cancels, clicking away commits.
        Files.AddHandler(KeyDownEvent, OnRenameKey, RoutingStrategies.Tunnel);
        Files.AddHandler(LostFocusEvent, OnRenameLostFocus, RoutingStrategies.Bubble);

        // Dragging rows out.
        Files.AddHandler(PointerPressedEvent, OnRowPressed, RoutingStrategies.Tunnel);
        Files.AddHandler(PointerMovedEvent, OnRowMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        Files.AddHandler(PointerReleasedEvent, OnRowReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

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
        if (paths.Count == 0)
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
        if (_tab is not { } tab)
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
