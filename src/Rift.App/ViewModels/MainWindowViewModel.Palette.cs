using System.Diagnostics;
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

        foreach (var pane in Panes)
        foreach (var tab in pane.Tabs)
        {
            items.Add(new PaletteItem(tab.Title, tab.DisplayPath, "Icon.Split", PaletteKind.Tab, () =>
            {
                IsZoomed &= pane == ActivePane;
                ActivePane = pane;
                pane.SelectedTab = tab;
                FocusFilesRequested?.Invoke();
                return Task.CompletedTask;
            }));
        }

        items.AddRange(Commands());

        foreach (var name in SessionStore.Workspaces())
        {
            items.Add(new PaletteItem(Strings.WorkspaceOpen(name), null, "Icon.ViewGrid", PaletteKind.Command, () =>
            {
                OpenWorkspace(name);
                return Task.CompletedTask;
            }));
            items.Add(new PaletteItem(Strings.WorkspaceDelete(name), null, "Icon.Trash", PaletteKind.Command, () =>
            {
                DeleteWorkspace(name);
                return Task.CompletedTask;
            }));
        }

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
        foreach (var command in AppCommands.All)
        {
            yield return new PaletteItem(command.Title, null, command.Icon, PaletteKind.Command, () =>
            {
                command.Run(this);
                return Task.CompletedTask;
            }, command.KeyText);
        }

        // Fixed keys, not in Settings: Tab belongs to focus handling.
        yield return new PaletteItem(Strings.CmdSwitchPane, null, "Icon.Split", PaletteKind.Command, () =>
        {
            SwitchPane();
            return Task.CompletedTask;
        }, "Tab");
        yield return new PaletteItem(Strings.JumpNewWindow, null, "Icon.Plus", PaletteKind.Command, () =>
        {
            if (Environment.ProcessPath is { } exe)
                Process.Start(new ProcessStartInfo(exe, "--new-window") { UseShellExecute = false });
            return Task.CompletedTask;
        });
    }
}
