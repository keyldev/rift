using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Rift.App.Services;
using Rift.Core;
using Rift.Core.Localization;

namespace Rift.App.ViewModels;

/// <summary>What the command palette (Ctrl+K) offers.</summary>
public partial class MainWindowViewModel
{
    private CommandPaletteViewModel? _palette;

    public CommandPaletteViewModel Palette => _palette ??= new CommandPaletteViewModel(PaletteItems, PaletteGoTo);

    [RelayCommand]
    private void OpenPalette()
    {
        if (Dialog is null)
            Palette.Open();
    }

    private IReadOnlyList<PaletteItem> PaletteItems()
    {
        var items = new List<PaletteItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in RecentPlaces.Items.Take(8))
        {
            if (seen.Add(path))
                items.Add(Place(Locations.Title(path), path, "Icon.Folder", PaletteKind.Recent));
        }

        foreach (var tab in AllTabs)
        {
            var pane = Left.Tabs.Contains(tab) ? Left : Right;
            items.Add(new PaletteItem(tab.Title, tab.DisplayPath, "Icon.Split", PaletteKind.Tab, () =>
            {
                IsSplit |= pane == Right;
                ActivePane = pane;
                pane.SelectedTab = tab;
                FocusFilesRequested?.Invoke();
                return Task.CompletedTask;
            }));
        }

        items.AddRange(Commands());

        foreach (var place in Pinned.Concat(QuickAccess).Concat(Drives))
        {
            if (seen.Add(place.Path))
                items.Add(Place(place.Label, place.Path, place.Icon, PaletteKind.Place));
        }
        return items;
    }

    // Virtual places (This PC, Recycle Bin) show no path: "shell:…" means nothing to a reader.
    private PaletteItem Place(string title, string path, string icon, PaletteKind kind) =>
        new(title, Locations.IsVirtual(path) ? null : path, icon, kind, () => GoToAsync(path));

    /// <summary>Opens a place in the active tab.</summary>
    private async Task GoToAsync(string path)
    {
        await ActiveTab.NavigateTo(path);
        FocusFilesRequested?.Invoke();
    }

    private PaletteItem? PaletteGoTo(string query)
    {
        // Only for things that look like paths; "doc" should fuzzy-match Documents, not resolve against the folder.
        if (query.IndexOfAny(['\\', '/', ':', '%', '~']) < 0)
            return null;
        var (kind, target) = AddressResolver.Resolve(query, ActiveTab.Path);
        if (kind == AddressKind.NotFound)
            return null;
        return new PaletteItem(Strings.PaletteGoTo(target), null, kind == AddressKind.File ? "Icon.File" : "Icon.Folder", PaletteKind.GoTo,
            () => ActiveTab.CommitAddressAsync(target));
    }

    private IEnumerable<PaletteItem> Commands()
    {
        PaletteItem Cmd(string title, string icon, string? gesture, ICommand command, object? parameter = null) =>
            new(title, null, icon, PaletteKind.Command, () =>
            {
                if (command.CanExecute(parameter))
                    command.Execute(parameter);
                return Task.CompletedTask;
            }, gesture);

        var tab = ActiveTab;
        yield return Cmd(Strings.CmdNewTab, "Icon.Plus", "Ctrl+T", ActivePane.NewTabCommand);
        yield return Cmd(Strings.CmdCloseTab, "Icon.Close", "Ctrl+W", ActivePane.CloseTabCommand, tab);
        yield return Cmd(Strings.MenuNewFolder, "Icon.Folder", "Ctrl+Shift+N", NewFolderCommand);
        yield return Cmd(Strings.MenuCopyToOther, "Icon.Copy", "F5", CopyToOtherCommand);
        yield return Cmd(Strings.MenuMoveToOther, "Icon.Move", "F6", MoveToOtherCommand);
        yield return Cmd(Strings.MenuRename, "Icon.Rename", "F2", RenameCommand);
        yield return Cmd(Strings.MenuDelete, "Icon.Trash", "Del", DeleteCommand);
        yield return Cmd(Strings.MenuDeletePermanently, "Icon.Trash", "Shift+Del", DeletePermanentlyCommand);
        yield return Cmd(Strings.CmdUndo, "Icon.Restore", "Ctrl+Z", UndoCommand);
        yield return Cmd(Strings.MenuCut, "Icon.Cut", "Ctrl+X", CutToClipboardCommand);
        yield return Cmd(Strings.MenuCopy, "Icon.Copy", "Ctrl+C", CopyToClipboardCommand);
        yield return Cmd(Strings.MenuPaste, "Icon.Paste", "Ctrl+V", PasteCommand);
        yield return Cmd(Strings.MenuCopyPath, "Icon.Link", "Ctrl+Shift+C", CopyPathCommand);
        yield return Cmd(Strings.MenuProperties, "Icon.Info", "Alt+Enter", PropertiesCommand);
        yield return Cmd(Strings.MenuRefresh, "Icon.Refresh", "Ctrl+R", tab.RefreshCommand);
        yield return Cmd(Strings.CmdSearch, "Icon.Search", "Ctrl+F", FocusSearchCommand);
        yield return Cmd(Strings.CmdEditAddress, "Icon.Link", "Ctrl+L", tab.EditAddressCommand);
        yield return Cmd(Strings.CmdGoUp, "Icon.Up", "Alt+↑", tab.UpCommand);
        yield return Cmd(Strings.CmdBack, "Icon.Back", "Alt+←", tab.BackCommand);
        yield return Cmd(Strings.CmdForward, "Icon.Forward", "Alt+→", tab.ForwardCommand);
        yield return Cmd(Strings.CmdPinFolder, "Icon.Pin", null, PinCommand);
        yield return Cmd(Strings.CmdPreview, "Icon.Preview", "Alt+P", ActivePane.TogglePreviewCommand);
        yield return Cmd(Strings.CmdViewDetails, "Icon.ViewDetails", "Ctrl+Shift+1", tab.SetViewCommand, ViewMode.Details);
        yield return Cmd(Strings.CmdViewTiles, "Icon.ViewTiles", "Ctrl+Shift+2", tab.SetViewCommand, ViewMode.Tiles);
        yield return Cmd(Strings.CmdViewThumbnails, "Icon.ViewGrid", "Ctrl+Shift+3", tab.SetViewCommand, ViewMode.Thumbnails);
        yield return Cmd(Strings.MenuShowHidden, View.ShowHidden ? "Icon.Eye" : "Icon.EyeOff", "Ctrl+H", ToggleHiddenCommand);
        yield return Cmd(Strings.TipTheme, IsDark ? "Icon.Sun" : "Icon.Moon", null, ToggleThemeCommand);
        yield return Cmd(Strings.TipSinglePane, "Icon.Single", null, ShowSingleCommand);
        yield return Cmd(Strings.TipSplitPane, "Icon.Split", null, ShowSplitCommand);
        yield return Cmd(Strings.CmdSwitchPane, "Icon.Split", "Tab", SwitchPaneCommand);
        yield return Cmd(Strings.MenuEmptyBin, "Icon.Trash", null, EmptyBinCommand);
        yield return new PaletteItem(Strings.JumpNewWindow, null, "Icon.Plus", PaletteKind.Command, () =>
        {
            if (Environment.ProcessPath is { } exe)
                Process.Start(new ProcessStartInfo(exe, "--new-window") { UseShellExecute = false });
            return Task.CompletedTask;
        });
    }
}
