using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Rift.Core;
using Rift.Shell;

namespace Rift.App.ViewModels;

/// <summary>Context menu commands and the bridges to Windows' own menu and dialogs.</summary>
public partial class MainWindowViewModel
{
    /// <summary>Puts text on the clipboard; set by the window (Avalonia's clipboard lives on the TopLevel).</summary>
    public Func<string, Task> SetClipboardText { get; set; } = _ => Task.CompletedTask;

    private IEnumerable<FileItem> SelectedFolders => ActiveTab.Selected.Where(i => i.IsDirectory);

    [RelayCommand]
    private Task OpenItem(FileItem? item) => ActiveTab.Open(item ?? ActiveTab.Selected.FirstOrDefault());

    [RelayCommand]
    private void OpenInNewTab()
    {
        TabViewModel? last = null;
        foreach (var folder in SelectedFolders.ToList())
            last = ActivePane.OpenTab(folder.FullPath);
        if (last is not null)
            ActivePane.SelectedTab = last;
    }

    [RelayCommand]
    private async Task OpenInOtherPane()
    {
        if (SelectedFolders.FirstOrDefault() is not { } folder)
            return;
        if (OtherPane is { } target && !IsZoomed)
        {
            await target.SelectedTab.NavigateTo(folder.FullPath);
            ActivePane = target;
            FocusFilesRequested?.Invoke();
        }
        else
        {
            // One pane on screen: open a new one on the right for it.
            IsZoomed = false;
            AddPane(Track(new PaneViewModel(folder.FullPath)), ActivePane, SplitDirection.Horizontal, before: false);
        }
    }

    /// <summary>Search hit → its folder in a new tab, with the file selected.</summary>
    [RelayCommand]
    private async Task OpenLocation()
    {
        if (ActiveTab.Selected.FirstOrDefault() is not { } item || Path.GetDirectoryName(item.FullPath) is not { } folder)
            return;
        var tab = ActivePane.OpenTab(folder);
        await tab.RefreshAsync([item.FullPath]);
    }

    /// <summary>Recursive search from the active tab's folder; results go to a new tab (or replace a search tab's results).</summary>
    public async Task SearchAsync(string query)
    {
        var tab = ActiveTab;
        if (string.IsNullOrWhiteSpace(query) || tab.SearchRoot is not { } root)
            return;
        var path = Locations.Search(root, query.Trim());
        if (tab.IsSearch)
            await tab.NavigateTo(path);
        else
            ActivePane.OpenTab(path); // a new tab starts loading (here: searching) by itself
    }

    [RelayCommand]
    private void OpenWith()
    {
        if (OperatingSystem.IsWindows() && ActiveTab.CanEditItems && ActiveTab.Selected.FirstOrDefault(i => !i.IsDirectory) is { } file)
            ShellDialogs.OpenWith(WindowHandle(), file.FullPath);
    }

    /// <summary>Like Explorer's "Copy as path": quoted, one per line.</summary>
    [RelayCommand]
    private Task CopyPath()
    {
        var tab = ActiveTab;
        var paths = tab.IsRecycleBin ? tab.SelectedRecycled.Select(i => i.OriginalPath).ToList() : tab.SelectedPaths;
        if (paths.Count == 0 && tab.IsFileSystem)
            paths = [tab.Path];
        if (paths.Count == 0)
            return Task.CompletedTask;
        return SetClipboardText(string.Join(Environment.NewLine, paths.Select(p => $"\"{p}\"")));
    }

    /// <summary>Alt+Enter: properties of the selection, or of the folder itself when nothing is selected.</summary>
    [RelayCommand]
    private void Properties()
    {
        var tab = ActiveTab;
        if (!OperatingSystem.IsWindows() || tab.IsRecycleBin)
            return;
        var paths = tab.SelectedPaths;
        if (paths.Count == 0 && !tab.IsFileSystem)
            return;
        // Several folders at once (search hits): the combined sheet needs one folder, so show the first item.
        if (paths.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            paths = [paths[0]];
        try
        {
            ShellDialogs.Properties(WindowHandle(), paths.Count > 0 ? paths : [tab.Path]);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Properties failed: {ex}");
        }
    }

    public bool CanPaste => OperatingSystem.IsWindows() && FileClipboard.HasFiles;

    /// <summary>
    /// Explorer's own menu at a screen point: for the selection, or for the folder background.
    /// Verbs Rift does better itself (inline rename, delete with undo, folders in a tab) are taken over.
    /// </summary>
    public void ShowShellMenu(bool forSelection, int screenX, int screenY, bool extended)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var tab = ActiveTab;
        var paths = tab.SelectedPaths;
        // Recycle Bin rows are $R files: Explorer's menu for them would show nonsense names.
        if (tab.IsRecycleBin || tab.Location == LocationKind.Libraries || (!tab.IsFileSystem && (!forSelection || paths.Count == 0)))
            return;
        // Explorer's menu works on items of one folder; search hits from several folders get Rift's menu only.
        if (forSelection && paths.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            return;
        try
        {
            if (forSelection && paths.Count > 0)
                ShellContextMenu.ShowForItems(WindowHandle(), paths, screenX, screenY, extended, verb => InterceptItemVerb(tab, verb));
            else
                ShellContextMenu.ShowForFolder(WindowHandle(), tab.Path, screenX, screenY, extended, InterceptFolderVerb);
        }
        catch (Exception ex)
        {
            // Shell extensions are third-party code; whatever they throw, Rift keeps running.
            Trace.WriteLine($"Shell menu failed: {ex}");
        }
    }

    private bool InterceptItemVerb(TabViewModel tab, string verb)
    {
        var single = tab.Selected.Count == 1 ? tab.Selected[0] : null;
        switch (verb)
        {
            case "rename":
                Rename();
                return true;
            case "delete":
                DeleteCommand.Execute(null);
                return true;
            case "open" or "explore" when single is { IsDirectory: true }:
                _ = tab.Open(single);
                return true;
            case "opennewwindow" or "opennewtab" when single is { IsDirectory: true }:
                OpenInNewTab();
                return true;
            case "paste":
                PasteCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private bool InterceptFolderVerb(string verb)
    {
        switch (verb)
        {
            case "paste":
                PasteCommand.Execute(null);
                return true;
            case "refresh":
                _ = ActiveTab.RefreshAsync();
                return true;
            case "newfolder":
                NewFolderCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }
}
