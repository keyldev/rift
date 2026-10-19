using System.Windows.Input;
using Avalonia.Input;
using Rift.App.Services;
using Rift.Core.Localization;
using Rift.Core;

namespace Rift.App.ViewModels;

/// <summary>
/// A command the user can run from the keyboard and the palette. Its keys come from <see cref="DefaultKeys"/>
/// unless the user changed them in Settings.
/// </summary>
/// <param name="DefaultKeys">Gestures in Avalonia syntax, comma-separated when there are several ("Ctrl+K, Ctrl+Shift+P").</param>
public sealed record AppCommand(string Id, Func<string> Label, string Icon, string DefaultKeys, Func<MainWindowViewModel, ICommand> Command,
    Func<MainWindowViewModel, object?>? Parameter = null)
{
    public string Title => Label();

    /// <summary>The keys in effect: the user's choice, or the default.</summary>
    public IReadOnlyList<KeyGesture> Keys
    {
        get
        {
            var text = AppSettings.Current.Hotkeys.TryGetValue(Id, out var custom) ? custom : DefaultKeys;
            var keys = new List<KeyGesture>();
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    keys.Add(KeyGesture.Parse(part));
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException)
                {
                    Log.Warn($"Bad gesture '{part}' for {Id}: {ex.Message}");
                }
            }
            return keys;
        }
    }

    public bool IsCustomized => AppSettings.Current.Hotkeys.ContainsKey(Id);

    /// <summary>The first key as people write it ("Ctrl+\", "Alt+←", "Del"), or null.</summary>
    public string? KeyText => Keys.Count > 0 ? Gestures.Format(Keys[0]) : null;

    public void Run(MainWindowViewModel vm)
    {
        var command = Command(vm);
        var parameter = Parameter?.Invoke(vm);
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }
}

/// <summary>Every command with a name, in the order the palette and Settings list them.</summary>
public static class AppCommands
{
    public static IReadOnlyList<AppCommand> All { get; } =
    [
        new("palette", () => Strings.CmdPalette, "Icon.Search", "Ctrl+K, Ctrl+Shift+P", vm => vm.OpenPaletteCommand),
        new("settings", () => Strings.CmdSettings, "Icon.Settings", "Ctrl+OemComma", vm => vm.OpenSettingsCommand),
        new("tab.new", () => Strings.CmdNewTab, "Icon.Plus", "Ctrl+T", vm => vm.ActivePane.NewTabCommand),
        new("tab.close", () => Strings.CmdCloseTab, "Icon.Close", "Ctrl+W", vm => vm.ActivePane.CloseTabCommand, vm => vm.ActivePane.SelectedTab),
        new("tab.reopen", () => Strings.CmdReopenTab, "Icon.Restore", "Ctrl+Shift+T", vm => vm.ReopenTabCommand),
        new("tab.duplicate", () => Strings.CmdDuplicateTab, "Icon.Copy", "", vm => vm.DuplicateTabCommand),
        new("tab.pin", () => Strings.CmdPinTab, "Icon.PinTab", "", vm => vm.TogglePinTabCommand),
        new("tab.closeOthers", () => Strings.CmdCloseOtherTabs, "Icon.Close", "", vm => vm.CloseOtherTabsCommand),
        new("nav.back", () => Strings.CmdBack, "Icon.Back", "Alt+Left", vm => vm.ActivePane.SelectedTab.BackCommand),
        new("nav.forward", () => Strings.CmdForward, "Icon.Forward", "Alt+Right", vm => vm.ActivePane.SelectedTab.ForwardCommand),
        new("nav.up", () => Strings.CmdGoUp, "Icon.Up", "Alt+Up", vm => vm.ActivePane.SelectedTab.UpCommand),
        new("nav.refresh", () => Strings.MenuRefresh, "Icon.Refresh", "Ctrl+R", vm => vm.ActivePane.SelectedTab.RefreshCommand),
        new("nav.editPath", () => Strings.CmdEditAddress, "Icon.Link", "Ctrl+L, Alt+D, F4", vm => vm.ActivePane.SelectedTab.EditAddressCommand),
        new("nav.search", () => Strings.CmdSearch, "Icon.Search", "Ctrl+F, Ctrl+E", vm => vm.FocusSearchCommand),
        new("file.copyToOther", () => Strings.MenuCopyToOther, "Icon.Copy", "F5", vm => vm.CopyToOtherCommand),
        new("file.moveToOther", () => Strings.MenuMoveToOther, "Icon.Move", "F6", vm => vm.MoveToOtherCommand),
        new("file.rename", () => Strings.MenuRename, "Icon.Rename", "F2", vm => vm.RenameCommand),
        new("file.delete", () => Strings.MenuDelete, "Icon.Trash", "Delete", vm => vm.DeleteCommand),
        new("file.deletePermanently", () => Strings.MenuDeletePermanently, "Icon.Trash", "Shift+Delete", vm => vm.DeletePermanentlyCommand),
        new("file.newFolder", () => Strings.MenuNewFolder, "Icon.Folder", "Ctrl+Shift+N", vm => vm.NewFolderCommand),
        new("file.undo", () => Strings.CmdUndo, "Icon.Restore", "Ctrl+Z", vm => vm.UndoCommand),
        new("file.cut", () => Strings.MenuCut, "Icon.Cut", "Ctrl+X", vm => vm.CutToClipboardCommand),
        new("file.copy", () => Strings.MenuCopy, "Icon.Copy", "Ctrl+C", vm => vm.CopyToClipboardCommand),
        new("file.paste", () => Strings.MenuPaste, "Icon.Paste", "Ctrl+V", vm => vm.PasteCommand),
        new("file.copyPath", () => Strings.MenuCopyPath, "Icon.Link", "Ctrl+Shift+C", vm => vm.CopyPathCommand),
        new("file.properties", () => Strings.MenuProperties, "Icon.Info", "Alt+Enter", vm => vm.PropertiesCommand),
        new("file.pinFolder", () => Strings.CmdPinFolder, "Icon.Pin", "", vm => vm.PinCommand),
        new("view.details", () => Strings.CmdViewDetails, "Icon.ViewDetails", "Ctrl+Shift+D1", vm => vm.ActivePane.SelectedTab.SetViewCommand, _ => ViewMode.Details),
        new("view.tiles", () => Strings.CmdViewTiles, "Icon.ViewTiles", "Ctrl+Shift+D2", vm => vm.ActivePane.SelectedTab.SetViewCommand, _ => ViewMode.Tiles),
        new("view.thumbnails", () => Strings.CmdViewThumbnails, "Icon.ViewGrid", "Ctrl+Shift+D3", vm => vm.ActivePane.SelectedTab.SetViewCommand, _ => ViewMode.Thumbnails),
        new("view.preview", () => Strings.CmdPreview, "Icon.Preview", "Alt+P", vm => vm.ActivePane.TogglePreviewCommand),
        new("view.hidden", () => Strings.MenuShowHidden, "Icon.EyeOff", "Ctrl+H", vm => vm.ToggleHiddenCommand),
        new("view.theme", () => Strings.TipTheme, "Icon.Moon", "", vm => vm.ToggleThemeCommand),
        new("pane.splitRight", () => Strings.CmdSplitRight, "Icon.Split", "Ctrl+OemPipe", vm => vm.SplitRightCommand),
        new("pane.splitDown", () => Strings.CmdSplitDown, "Icon.SplitDown", "Ctrl+Shift+OemPipe", vm => vm.SplitDownCommand),
        new("pane.zoom", () => Strings.CmdZoom, "Icon.Single", "Ctrl+Shift+Enter", vm => vm.ToggleZoomCommand),
        new("pane.close", () => Strings.CmdClosePane, "Icon.Close", "Ctrl+Shift+W", vm => vm.CloseActivePaneCommand),
        new("workspace.save", () => Strings.CmdSaveWorkspace, "Icon.Plus", "", vm => vm.SaveWorkspaceCommand),
        new("bin.empty", () => Strings.MenuEmptyBin, "Icon.Trash", "", vm => vm.EmptyBinCommand),
    ];

    public static AppCommand? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>The command that already uses a gesture, other than <paramref name="except"/>.</summary>
    public static AppCommand? Using(KeyGesture gesture, AppCommand? except = null) =>
        All.FirstOrDefault(c => c != except && c.Keys.Any(k => k.Equals(gesture)));
}

public static class Gestures
{
    /// <summary>A gesture the way it's printed on keys: "Ctrl+\" not "Ctrl+OemPipe", "Del" not "Delete".</summary>
    public static string Format(KeyGesture gesture)
    {
        var parts = new List<string>();
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(gesture.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(gesture.Key - Key.D0)).ToString(),
            Key.OemPipe or Key.OemBackslash => "\\",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemTilde => "`",
            Key.Left => "←",
            Key.Right => "→",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Enter => "Enter",
            Key.Delete => "Del",
            Key.Back => "Backspace",
            Key.Escape => "Esc",
            Key.PageUp => "PgUp",
            Key.PageDown => "PgDn",
            _ => gesture.Key.ToString(),
        });
        return string.Join('+', parts);
    }

    /// <summary>A key press usable as a shortcut: needs a modifier, unless it's a function key or a few safe others.</summary>
    public static KeyGesture? FromKeyPress(Key key, KeyModifiers modifiers)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.None or Key.System)
            return null;
        var plain = modifiers == KeyModifiers.None || modifiers == KeyModifiers.Shift;
        var allowedPlain = key is >= Key.F1 and <= Key.F24 or Key.Delete or Key.Insert;
        return plain && !allowedPlain ? null : new KeyGesture(key, modifiers);
    }
}
