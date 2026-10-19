using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    /// <summary>A tab being dragged: to reorder, move to another pane, or split.</summary>
    private static readonly DataFormat<TabDrag> TabFormat = DataFormat.CreateInProcessFormat<TabDrag>("rift-tab");

    private sealed record TabDrag(TabViewModel Tab, PaneViewModel Source);

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
        // Tunnel: the ListBox itself swallows Enter before a bubbling handler would see it.
        Files.AddHandler(KeyDownEvent, OnFilesKeyDown, RoutingStrategies.Tunnel);
        Files.SelectionChanged += (_, _) => SyncSelection();
        Files.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                _pane?.Columns.Fit(Files.Bounds.Width - 30); // list padding, row padding, scrollbar
        };
        // Icons load lazily, for the rows that actually get on screen.
        Files.ContainerPrepared += (_, e) =>
        {
            // ItemFromContainer isn't wired up yet at this point; the DataContext is.
            if (e.Container.DataContext is not FileItem item)
                return;
            FileIcons.Request(item);
            if (_tab is { IsDetails: false } tab)
                FileThumbnails.Request(item, ThumbnailPixels(tab.ViewMode));
        };
        Files.ContainerClearing += (_, e) =>
        {
            if (e.Container.DataContext is FileItem item)
                FileThumbnails.Forget(item);
        };
        // Ctrl+wheel switches between details, tiles and thumbnails, like zooming in Explorer.
        Files.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && _tab is { } tab)
            {
                tab.StepView(e.Delta.Y > 0 ? 1 : -1);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

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

        // Typing letters filters the folder; Backspace edits the filter, Esc clears it.
        Files.AddHandler(TextInputEvent, OnFilesTextInput, RoutingStrategies.Tunnel);
        Header.ContextRequested += OnHeaderContextRequested;

        // Address bar: a click on empty space in the breadcrumbs edits the path.
        AddressBar.PointerPressed += (_, e) =>
        {
            if (!e.Handled && e.GetCurrentPoint(AddressBar).Properties.IsLeftButtonPressed && _tab is { IsEditingAddress: false } tab)
            {
                tab.EditAddressCommand.Execute(null);
                e.Handled = true;
            }
        };
        AddressBox.AsyncPopulator = (text, ct) => Task.Run(() => (IEnumerable<object>)AddressResolver.Suggest(text ?? "", _tab?.Path ?? ""), ct);
        AddressBox.AddHandler(KeyDownEvent, OnAddressKey, RoutingStrategies.Tunnel);
        SearchBox.AddHandler(KeyDownEvent, OnSearchKey, RoutingStrategies.Tunnel);
        AddressBox.LostFocus += (_, _) =>
        {
            // Clicking a suggestion briefly moves focus into the popup; only give up when focus really left.
            Dispatcher.UIThread.Post(() =>
            {
                if (!AddressBox.IsKeyboardFocusWithin && !AddressBox.IsDropDownOpen)
                    _tab?.CancelAddress();
            });
        };

        // Tabs: drag, middle-click to close, right-click menu.
        TabStrip.AddHandler(PointerPressedEvent, OnTabPressed, RoutingStrategies.Tunnel);
        TabStrip.AddHandler(PointerMovedEvent, OnTabMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        TabStrip.AddHandler(PointerReleasedEvent, (_, _) => _tabPressed = null, RoutingStrategies.Tunnel, handledEventsToo: true);
        TabStrip.ContextRequested += OnTabContextRequested;
        TabStrip.SelectionChanged += (_, _) =>
        {
            if (TabStrip.SelectedItem is { } selected)
                TabStrip.ScrollIntoView(selected);
        };

        // A narrow pane hides its view buttons so the path stays readable.
        PaneRoot.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                PaneRoot.Classes.Set("narrow", PaneRoot.Bounds.Width < 430);
        };

        // Dropping files in, from Rift or from Explorer.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private PaneViewModel? Pane => DataContext as PaneViewModel;
    private MainWindowViewModel? Main => TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    /// <summary>Ctrl+F: into the search box, with what's there selected.</summary>
    public void FocusSearch()
    {
        if (!SearchBox.IsEnabled)
            return;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private async void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Main is { } main)
        {
            e.Handled = true;
            Activate();
            await main.SearchAsync(SearchBox.Text ?? "");
            FocusFiles();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            SearchBox.Text = Locations.ParseSearch(_tab?.Path ?? "")?.Query ?? "";
            FocusFiles();
        }
    }

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
        else if (e.PropertyName == nameof(PaneViewModel.IsPreviewOpen))
            UpdatePreview();
    }

    private void WatchTab()
    {
        if (_tab is not null)
        {
            _tab.SelectRequested -= ApplySelection;
            _tab.PropertyChanged -= OnTabPropertyChanged;
            _tab.CancelAddress();
        }
        _tab = _pane?.SelectedTab;
        if (_tab is not null)
        {
            _tab.SelectRequested += ApplySelection;
            _tab.PropertyChanged += OnTabPropertyChanged;
        }
        ShowSearchQuery();
    }

    /// <summary>A search tab shows its query in the box; other tabs start empty.</summary>
    private void ShowSearchQuery() => SearchBox.Text = Locations.ParseSearch(_tab?.Path ?? "")?.Query ?? "";

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.Path) && !SearchBox.IsKeyboardFocusWithin)
            ShowSearchQuery();
        if (e.PropertyName != nameof(TabViewModel.IsEditingAddress) || _tab is not { } tab)
            return;
        if (tab.IsEditingAddress)
        {
            Activate();
            Dispatcher.UIThread.Post(() =>
            {
                AddressBox.Focus();
                AddressBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.SelectAll();
            }, DispatcherPriority.Loaded);
        }
        else if (AddressBox.IsKeyboardFocusWithin)
        {
            FocusFiles();
        }
    }

    /// <summary>A suggestion was chosen with the arrows; otherwise Enter takes the typed text as it is.</summary>
    private bool _addressPicked;

    private async void OnAddressKey(object? sender, KeyEventArgs e)
    {
        if (_tab is not { } tab)
            return;
        switch (e.Key)
        {
            case Key.Up or Key.Down:
                _addressPicked = true;
                break;
            case Key.Escape:
                e.Handled = true;
                if (AddressBox.IsDropDownOpen)
                    AddressBox.IsDropDownOpen = false;
                else
                    tab.CancelAddress();
                break;
            case Key.Enter:
                e.Handled = true;
                // AutoCompleteBox also "selects" a suggestion that merely equals the text halfway through typing.
                var text = _addressPicked && AddressBox.IsDropDownOpen && AddressBox.SelectedItem is string picked ? picked : AddressBox.Text ?? "";
                _addressPicked = false;
                AddressBox.IsDropDownOpen = false;
                if (!await tab.CommitAddressAsync(text))
                    AddressBox.Classes.Set("error", true);
                break;
            default:
                _addressPicked = false;
                AddressBox.Classes.Set("error", false);
                break;
        }
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
        if (e.Handled || Pane?.SelectedTab is not { } tab || IsInsideRenameBox(e.Source) || e.KeyModifiers != KeyModifiers.None)
            return;

        switch (e.Key)
        {
            case Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown:
                DropBigSelection(); // the list then selects the next row as usual
                break;
            case Key.Enter:
                OpenSelected();
                e.Handled = true;
                break;
            case Key.Back when tab.IsFiltered:
                tab.Filter = tab.Filter[..^1];
                e.Handled = true;
                KeepFocusAfterFilter();
                break;
            case Key.Back:
                tab.UpCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape when tab.IsFiltered:
                tab.Filter = "";
                e.Handled = true;
                KeepFocusAfterFilter();
                break;
        }
    }

    /// <summary>
    /// Avalonia collapses a selection row by row, each one a search and a shift through its list of selected
    /// items: after Ctrl+A in 100,000 files, the next click or arrow key froze the window for a second.
    /// Handing it a new empty list skips that; call it right before the list collapses the selection itself.
    /// </summary>
    private void DropBigSelection()
    {
        if (Files.Selection.Count > 1000)
            Files.SelectedItems = new AvaloniaList<object>();
    }

    private void SyncSelection()
    {
        if (Pane?.SelectedTab is not { } tab)
            return;
        tab.Selected.Clear();
        if (Files.SelectedItems is { } items)
        {
            // Right after a tab switch the list can still report the previous tab's selection.
            // Checked only then: hashing 100,000 rows on every arrow key adds up.
            var shown = ReferenceEquals(Files.ItemsSource, tab.Items) ? null : tab.Items.ToHashSet();
            foreach (var item in items.OfType<FileItem>().Where(i => shown?.Contains(i) ?? true))
                tab.Selected.Add(item);
        }
        tab.UpdateStatus();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_pane is not { IsPreviewOpen: true } pane || _tab is not { } tab)
            return;
        var item = tab.Selected.Count == 1 ? tab.Selected[0] : null;
        _ = pane.Preview.ShowAsync(item, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    }

    private void OnPreviewResize(object? sender, VectorEventArgs e) => _pane?.ResizePreview(-e.Vector.X);

    /// <summary>Thumbnail size in device pixels, so pictures stay sharp on high-DPI screens.</summary>
    private int ThumbnailPixels(ViewMode mode) =>
        (int)Math.Ceiling((mode == ViewMode.Tiles ? 48 : 112) * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));

    // ---------- columns and filter ----------

    private void OnHeaderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: SortColumn column } && _tab is { } tab)
            tab.SortBy(column);
    }

    private void OnColumnResize(object? sender, VectorEventArgs e)
    {
        if (sender is Control { Tag: SortColumn column })
            ViewOptions.Current.Columns.Resize(column, -e.Vector.X);
    }

    private void OnHeaderContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        var columns = ViewOptions.Current.Columns;
        var items = new List<Control>();
        foreach (var (column, label) in new[]
                 {
                     (SortColumn.Modified, Strings.ColumnModified),
                     (SortColumn.Created, Strings.ColumnCreated),
                     (SortColumn.Type, Strings.ColumnType),
                     (SortColumn.Size, Strings.ColumnSize),
                 })
        {
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = columns.IsShown(column) };
            item.Click += (_, _) => columns.Toggle(column);
            items.Add(item);
        }
        new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer }.Open(Header);
    }

    private void OnFilesTextInput(object? sender, TextInputEventArgs e)
    {
        if (_tab is not { } tab || IsInsideRenameBox(e.Source) || string.IsNullOrEmpty(e.Text) || tab.Location == LocationKind.ThisPc)
            return;
        // A space only continues a filter; on its own it belongs to the list (toggles selection).
        if (e.Text == " " && !tab.IsFiltered)
            return;
        if (e.Text.Any(char.IsControl))
            return;
        tab.Filter += e.Text;
        e.Handled = true;
        KeepFocusAfterFilter();
    }

    /// <summary>
    /// The focused row may have just been filtered out, taking keyboard focus with it:
    /// select the first match instead, so typing goes on and Enter opens it.
    /// </summary>
    private void KeepFocusAfterFilter() => Dispatcher.UIThread.Post(() =>
    {
        if (Files.SelectedItems is { Count: 0 } && Files.ItemCount > 0)
            Files.SelectedIndex = 0;
        FocusFiles();
    }, DispatcherPriority.Loaded);

    private void OnClearFilter(object? sender, RoutedEventArgs e)
    {
        if (_tab is { } tab)
            tab.Filter = "";
        FocusFiles();
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
                _ when !tab.IsFileSystem => [Item(Strings.MenuRefresh, "Icon.Refresh", "Ctrl+R", tab.RefreshCommand)],
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

        if (tab.IsSearch || tab.Location == LocationKind.Tag)
            items.Add(Item(Strings.MenuOpenLocation, "Icon.Folder", null, main.OpenLocationCommand));

        if (folders == 1 && files == 0 && main.CanPin(tab.Selected[0].FullPath))
            items.Add(Item(Strings.MenuPin, "Icon.Pin", null, main.PinCommand, parameter: tab.Selected[0].FullPath));

        // Drives, computers, shares, libraries: they can be opened and inspected, not cut, renamed or deleted.
        if (tab.IsPlaceList)
        {
            items.Add(new Separator());
            items.Add(Item(Strings.MenuCopyPath, "Icon.Link", "Ctrl+Shift+C", main.CopyPathCommand));
            AddShellItems(items, main, forSelection: true, screen);
            return items;
        }

        items.Add(new Separator());
        items.Add(Item(Strings.MenuCut, "Icon.Cut", "Ctrl+X", main.CutToClipboardCommand));
        items.Add(Item(Strings.MenuCopy, "Icon.Copy", "Ctrl+C", main.CopyToClipboardCommand));
        if (main.CanTransfer)
        {
            items.Add(Item(Strings.MenuCopyToOther, "Icon.Copy", "F5", main.CopyToOtherCommand));
            items.Add(Item(Strings.MenuMoveToOther, "Icon.Move", "F6", main.MoveToOtherCommand));
        }
        items.Add(Item(Strings.MenuCopyPath, "Icon.Link", "Ctrl+Shift+C", main.CopyPathCommand));
        items.Add(TagsMenu(main, tab));

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
            Item(Strings.MenuPaste, "Icon.Paste", "Ctrl+V", main.PasteCommand, enabled: main.CanPaste && _tab!.IsFileSystem),
            new Separator(),
            Item(Strings.MenuRefresh, "Icon.Refresh", "Ctrl+R", _tab!.RefreshCommand),
            hidden,
            Item(Strings.MenuCopyPath, "Icon.Link", "Ctrl+Shift+C", main.CopyPathCommand),
        };
        if (main.CanPin(_tab.Path))
            items.Add(Item(Strings.MenuPin, "Icon.Pin", null, main.PinCommand, parameter: _tab.Path));
        AddShellItems(items, main, forSelection: false, screen);
        return items;
    }

    /// <summary>Tags ▸ every tag as a check (checked when all selected items have it), then "New tag…".</summary>
    private static MenuItem TagsMenu(MainWindowViewModel main, TabViewModel tab)
    {
        var paths = tab.SelectedPaths;
        var entries = new List<Control>();
        foreach (var tag in AppSettings.Current.Tags)
        {
            var item = new MenuItem
            {
                Header = tag.Name,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = paths.Count > 0 && paths.All(p => AppSettings.Tags.Has(p, tag.Id)),
                Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.Parse(tag.Color)) },
            };
            item.Click += (_, _) => main.ToggleTag(tag.Id);
            entries.Add(item);
        }
        if (entries.Count > 0)
            entries.Add(new Separator());
        entries.Add(Item(Strings.TagNewMenu, "Icon.Plus", null, main.NewTagCommand));
        var menu = Item(Strings.MenuTags, "Icon.Tag", null, null);
        menu.ItemsSource = entries;
        return menu;
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
        bool enabled = true, bool isDefault = false, object? parameter = null)
    {
        var item = new MenuItem
        {
            Header = header,
            Command = command,
            CommandParameter = parameter,
            IsEnabled = enabled,
            InputGesture = gesture is null ? null : KeyGesture.Parse(gesture),
            FontWeight = isDefault ? FontWeight.SemiBold : FontWeight.Normal,
        };
        if (icon is not null && Application.Current?.TryFindResource(icon, out var geometry) == true)
            item.Icon = new Icon { Data = geometry as Geometry, Width = 16, Height = 16 };
        return item;
    }

    // ---------- tabs ----------

    private PointerPressedEventArgs? _tabPressed;
    private Point _tabPressedAt;

    private static TabViewModel? TabItem(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as TabViewModel;

    private void OnTabPressed(object? sender, PointerPressedEventArgs e)
    {
        _tabPressed = null;
        if (TabItem(e.Source) is not { } tab || _pane is not { } pane)
            return;
        var point = e.GetCurrentPoint(TabStrip).Properties;
        if (point.IsMiddleButtonPressed)
        {
            pane.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
        else if (point.IsLeftButtonPressed && (e.Source as Visual)?.FindAncestorOfType<Button>() is null)
        {
            _tabPressed = e;
            _tabPressedAt = e.GetPosition(TabStrip);
        }
    }

    private async void OnTabMoved(object? sender, PointerEventArgs e)
    {
        if (_tabPressed is not { } pressed || _pane is not { } pane || TabItem(pressed.Source) is not { } tab)
            return;
        if (!e.GetCurrentPoint(TabStrip).Properties.IsLeftButtonPressed)
        {
            _tabPressed = null;
            return;
        }
        var delta = e.GetPosition(TabStrip) - _tabPressedAt;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        _tabPressed = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TabFormat, new TabDrag(tab, pane)));
        await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move);
    }

    /// <summary>
    /// Over the tab strip: a tab (reorder or move here). Over the pane: its outer quarters split it,
    /// the middle takes the tab in.
    /// </summary>
    private TabDropZone? TabZone(DragEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBox>(includeSelf: true) == TabStrip)
            return TabDropZone.Center;
        var p = e.GetPosition(this);
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return null;
        double x = p.X / w, y = p.Y / h;
        var edge = Math.Min(Math.Min(x, 1 - x), Math.Min(y, 1 - y));
        if (edge > 0.25)
            return TabDropZone.Center;
        return edge == x ? TabDropZone.Left : edge == 1 - x ? TabDropZone.Right : edge == y ? TabDropZone.Top : TabDropZone.Bottom;
    }

    /// <summary>Where in the strip a dropped tab goes: before the tab under the pointer's left half, after its right half.</summary>
    private int TabIndexAt(DragEventArgs e)
    {
        if (_pane is not { } pane)
            return 0;
        var x = e.GetPosition(TabStrip).X;
        for (var i = 0; i < pane.Tabs.Count; i++)
        {
            if (TabStrip.ContainerFromIndex(i) is { } container && container.TranslatePoint(default, TabStrip) is { } origin
                && x < origin.X + container.Bounds.Width / 2)
                return i;
        }
        return pane.Tabs.Count;
    }

    private void ShowTabDrop(TabDropZone? zone)
    {
        TabDropOverlay.IsVisible = zone is not null;
        if (zone is not { } z)
            return;
        double w = PaneRoot.Bounds.Width / 2, h = PaneRoot.Bounds.Height / 2;
        TabDropOverlay.Margin = z switch
        {
            TabDropZone.Left => new Thickness(4, 4, w, 4),
            TabDropZone.Right => new Thickness(w, 4, 4, 4),
            TabDropZone.Top => new Thickness(4, 4, 4, h),
            TabDropZone.Bottom => new Thickness(4, h, 4, 4),
            _ => new Thickness(4),
        };
    }

    private void OnTabContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (TabItem(e.Source) is not { } tab || _pane is not { } pane || Main is not { } main)
            return;
        e.Handled = true;
        Activate();
        pane.SelectedTab = tab;

        MenuItem Action(string header, string? icon, string? gesture, Action run, bool enabled = true)
        {
            var item = Item(header, icon, gesture, null, enabled);
            item.Click += (_, _) => run();
            return item;
        }

        var index = pane.Tabs.IndexOf(tab);
        var items = new List<Control>
        {
            Action(Strings.MenuTabClose, "Icon.Close", tab.IsPinned ? null : "Ctrl+W", () => pane.Close(tab)),
            Action(Strings.MenuTabCloseOthers, null, null, () => pane.CloseOthers(tab), pane.Tabs.Any(t => t != tab && !t.IsPinned)),
            Action(Strings.MenuTabCloseRight, null, null, () => pane.CloseToTheRight(tab), pane.Tabs.Skip(index + 1).Any(t => !t.IsPinned)),
            new Separator(),
            Action(tab.IsPinned ? Strings.MenuTabUnpin : Strings.MenuTabPin, "Icon.PinTab", null, () => pane.TogglePin(tab)),
            Action(Strings.MenuTabDuplicate, "Icon.Copy", null, () => pane.Duplicate(tab)),
            new Separator(),
            Action(Strings.MenuTabSplitRight, "Icon.Split", null, () => main.SplitWithCopy(tab, pane, SplitDirection.Horizontal)),
            Action(Strings.MenuTabSplitDown, "Icon.SplitDown", null, () => main.SplitWithCopy(tab, pane, SplitDirection.Vertical)),
            Action(Strings.MenuTabMoveOther, "Icon.Move", null, () => main.MoveTabToOtherPane(tab, pane),
                main.Panes.Count > 1 || pane.Tabs.Count > 1),
        };
        new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer }.Open(TabStrip);
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
        else if (e.KeyModifiers == KeyModifiers.None)
        {
            DropBigSelection();
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
        if (paths.Count == 0 || !tab.CanEditItems)
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
        if (e.DataTransfer.TryGetValue(TabFormat) is not null)
        {
            ShowTabDrop(TabZone(e));
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }
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
        ShowTabDrop(null);
        if (_pane is not null)
            _pane.IsDropTarget = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(TabFormat) is { } tabDrag)
        {
            var zone = TabZone(e);
            ShowTabDrop(null);
            e.Handled = true;
            if (Main is not { } m || _pane is not { } target)
                return;
            if (zone is { } z and not TabDropZone.Center)
                m.SplitWithTab(tabDrag.Tab, tabDrag.Source, target, z);
            else
                m.MoveTab(tabDrag.Tab, tabDrag.Source, target, TabIndexAt(e));
            return;
        }
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
